using System;
using CUE4Parse.UE4.Assets.Exports;
using Serilog;

namespace CUE4Parse.UE4.Assets;

public sealed class ExportInfo
{
    private readonly Func<UObject> _createSparse;
    private readonly Action<UObject> _deserialize;
    private readonly object _loadLock = new();

    private UObject? _sparse;
    private Exception? _createException;
    private Exception? _loadException;
    private bool _sparseConstructing;
    private volatile bool _loaded;

    public IPackage Package { get; }
    public int Index { get; }
    public string Name { get; }
    public string ClassName { get; }

    internal ExportInfo(
        IPackage package,
        int index,
        string name,
        string className,
        Func<UObject> createSparse,
        Action<UObject> deserialize)
    {
        Package = package;
        Index = index;
        Name = name;
        ClassName = className;
        _createSparse = createSparse;
        _deserialize = deserialize;
    }

    // Re-entrant via _sparseConstructing: if Create needs another same-package
    // export's sparse mid-flight, the cycle returns early with a null _sparse
    // and completes after recursion unwinds.
    internal void EnsureSparse()
    {
        if (_sparse != null || _sparseConstructing) return;
        _sparseConstructing = true;
        try
        {
            _sparse = _createSparse();
        }
        catch (Exception ex)
        {
            _createException = ex;
            _sparse = new UObject { Name = Name };
            Log.Warning("ExportInfo Create failed for export {0} ({1}) in package {2}: {3}",
                Index, Name, Package.Name, ex.GetType().Name);
        }
        finally
        {
            _sparseConstructing = false;
        }
    }

    public bool IsA<T>() where T : UObject => _sparse is T;

    public UObject Load()
    {
        // Same-package class refs reach this through LoadAsStruct → other.Load
        // before pass 2 has reached the target slot. EnsureSparse is idempotent
        // and self-cycle-guarded; calling it here covers that recursion.
        EnsureSparse();

        if (_createException != null)
            throw new InvalidOperationException(
                $"ExportInfo Create failed for export {Index} ({Name}) in package {Package.Name}",
                _createException);
        if (_sparse == null)
            throw new InvalidOperationException(
                $"ExportInfo cycle: sparse for export {Index} ({Name}) in package {Package.Name} is being constructed up the call stack");

        if (_loaded)
        {
            if (_loadException != null) throw _loadException;
            return _sparse;
        }

        lock (_loadLock)
        {
            if (_loaded)
            {
                if (_loadException != null) throw _loadException;
                return _sparse;
            }

            try
            {
                _deserialize(_sparse);
            }
            catch (Exception ex)
            {
                _loadException = ex;
                _loaded = true;
                throw;
            }
            _loaded = true;
        }
        return _sparse;
    }
}
