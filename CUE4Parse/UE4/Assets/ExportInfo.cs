using System;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;

namespace CUE4Parse.UE4.Assets;

public sealed class ExportInfo
{
    private readonly Lazy<UObject> _sparseLazy;
    private readonly Lazy<UObject> _loadLazy;
    private readonly Action<UObject> _deserialize;
    private readonly object _loadLock = new();
    private UObject? _loaded;

    public IPackage Package { get; }
    public int Index { get; }
    public string Name { get; }

    private UObject Sparse => _sparseLazy.Value;

    public ResolvedObject? Class => Sparse.Class;
    public ResolvedObject? Outer => Sparse.Outer;
    public ResolvedObject? Super => Sparse.Super;
    public ResolvedObject? Template => Sparse.Template;
    public EObjectFlags Flags => Sparse.Flags;

    public bool IsA<T>() where T : UObject => Sparse is T;

    public UObject Load() => _loadLazy.Value;

    private UObject LoadOnce()
    {
        lock (_loadLock)
        {
            if (_loaded is not null) return _loaded;
            var sparse = _sparseLazy.Value;
            var (_, bytes) = PackageCache.MeasureParse(() => { _deserialize(sparse); return sparse; });
            (Package as AbstractUePackage)?.Owner?.Store.Grow(Package, bytes);
            return _loaded = sparse;
        }
    }

    internal ExportInfo(
        IPackage package,
        int index,
        string name,
        Func<UObject> createSparse,
        Action<UObject> deserialize)
    {
        Package = package;
        Index = index;
        Name = name;
        _sparseLazy = new Lazy<UObject>(createSparse, System.Threading.LazyThreadSafetyMode.PublicationOnly);
        _deserialize = deserialize;
        _loadLazy = new Lazy<UObject>(LoadOnce, System.Threading.LazyThreadSafetyMode.PublicationOnly);
    }
}
