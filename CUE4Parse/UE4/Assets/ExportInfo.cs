using System;
using CUE4Parse.UE4.Assets.Exports;

namespace CUE4Parse.UE4.Assets;

public sealed class ExportInfo
{
    private readonly Lazy<UObject> _sparseLazy;
    private readonly Lazy<UObject> _loadLazy;

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
        _sparseLazy = new Lazy<UObject>(createSparse);
        _loadLazy = new Lazy<UObject>(() =>
        {
            var sparse = _sparseLazy.Value;
            deserialize(sparse);
            return sparse;
        });
    }
}
