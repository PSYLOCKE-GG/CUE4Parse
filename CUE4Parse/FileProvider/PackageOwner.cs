using System;

namespace CUE4Parse.FileProvider;

/// <summary>
/// One lifetime of loaded packages. A package belongs to the owner it loaded into, and it resolves
/// its imports through that owner, so the import web of an owner is one island: when the owner is
/// dropped, its packages are unreachable together. The provider's shared owner lives as long as the
/// provider and holds the packages that every lifetime reads.
/// </summary>
/// <remarks>
/// Edges go from a package to its own owner or to the shared owner, never to another owner. A shared
/// package resolves its imports in the shared owner, so the shared owner never holds a shorter lifetime.
/// </remarks>
public sealed class PackageOwner : IDisposable
{
    internal PackageOwner(string name, PackageCache store, bool isShared)
    {
        Name = name;
        Store = store;
        IsShared = isShared;
    }

    /// <summary>The lifetime this owner stands for, for diagnostics.</summary>
    public string Name { get; }

    /// <summary>
    /// The packages this owner keeps resident. The shared owner bounds it; other owners keep every package,
    /// including each shared package they resolved, so its instance does not change under them.
    /// </summary>
    public PackageCache Store { get; }

    /// <summary>Whether this is the provider's long-lived shared owner.</summary>
    public bool IsShared { get; }

    /// <summary>Releases the packages of this owner. The shared owner is never released.</summary>
    public void Dispose()
    {
        if (!IsShared) Store.Clear();
    }

    public override string ToString() => Name;
}
