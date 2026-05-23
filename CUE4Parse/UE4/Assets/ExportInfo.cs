using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CUE4Parse.UE4.Assets.Exports;

namespace CUE4Parse.UE4.Assets;

public sealed class ExportInfo
{
    private readonly Lazy<UObject> _lazy;

    public IPackage Package { get; }
    public int Index { get; }
    public string Name { get; }

    // Empty when the producer cannot resolve the class without triggering
    // cross-package deserialization (the cost this API exists to avoid).
    public string ClassName { get; }

    public ExportInfo(IPackage package, int index, string name, string className, Lazy<UObject> lazy)
    {
        Package = package;
        Index = index;
        Name = name;
        ClassName = className;
        _lazy = lazy;
    }

    public UObject Load() => _lazy.Value;

    public Task<UObject> LoadAsync() => Task.FromResult(_lazy.Value);

    // Fallback used by IPackage/AbstractUePackage when a concrete package
    // doesn't override EnumerateExports — defeats the cheap-pre-filter goal
    // by forcing every Lazy, but stays correct for arbitrary IPackage impls.
    internal static IEnumerable<ExportInfo> EnumerateByForcing(IPackage package)
    {
        var lazies = package.ExportsLazy;
        for (var i = 0; i < lazies.Length; i++)
        {
            var lazy = lazies[i];
            var obj = lazy.Value;
            yield return new ExportInfo(package, i, obj.Name, obj.ExportType, lazy);
        }
    }
}
