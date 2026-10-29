using System;
using CUE4Parse.UE4.Assets;
using CUE4Parse.FileProvider.Objects;

namespace CUE4Parse.FileProvider;

/// <summary>
/// One lifetime of loaded packages. A package belongs to the owner it loaded into, and it resolves
/// its imports through that owner, so the import web of an owner is one island: when the owner is
/// dropped, its packages are unreachable together. The provider's shared owner lives as long as the
/// provider and holds the packages that every lifetime reads.
/// </summary>
/// <remarks>
/// Owners nest. A child lives inside its parent, so a load in a child finds the packages of the child,
/// then of each ancestor, then of the shared owner, and a miss loads into the child.
/// Edges go from a package to its own owner, an ancestor or the shared owner, never to a sibling or a
/// descendant. A shared package resolves its imports in the shared owner, so the shared owner never
/// holds a shorter lifetime.
/// </remarks>
public sealed class PackageOwner : IDisposable
{
    internal PackageOwner(string name, PackageCache store, bool isShared, PackageOwner? parent = null)
    {
        Name = name;
        Store = store;
        IsShared = isShared;
        Parent = parent;
    }

    /// <summary>The owner this one lives inside; null for a root. Dropping a child never touches its parent.</summary>
    public PackageOwner? Parent { get; }

    /// <summary>The lifetime this owner stands for, for diagnostics.</summary>
    public string Name { get; }

    /// <summary>
    /// The packages this owner keeps resident. The shared owner bounds it; other owners keep every package,
    /// including each shared package they resolved, so its instance does not change under them.
    /// </summary>
    public PackageCache Store { get; }

    /// <summary>Whether this is the provider's long-lived shared owner.</summary>
    public bool IsShared { get; }

    /// <summary>Whether this owner was dropped. A dropped owner refuses every load.</summary>
    public bool IsDropped { get; private set; }

    /// <summary>
    /// Releases the packages of this owner and refuses later loads into it. A package that still
    /// resolves an import through a dropped owner outlived its lifetime, so the load throws.
    /// The shared owner is never released.
    /// </summary>
    public void Dispose()
    {
        if (IsShared) return;
        IsDropped = true;
        Store.Clear();
    }

    /// <summary>Finds a resident package in this owner or, failing that, in the nearest ancestor that holds it.</summary>
    internal bool TryFind(GameFile file, EPackageReadFlags readFlags, out IPackage package)
    {
        for (var owner = this; owner is not null; owner = owner.Parent)
            if (owner.Store.TryGet(file, readFlags, out package)) return true;
        package = null!;
        return false;
    }

    /// <summary>Whether a package of this owner may hold objects of <paramref name="other"/>: itself, an ancestor or the shared owner.</summary>
    internal bool Reaches(PackageOwner other)
    {
        if (other.IsShared) return true;
        for (var owner = this; owner is not null; owner = owner.Parent)
            if (owner == other) return true;
        return false;
    }

    internal void ThrowIfDropped()
    {
        if (IsDropped) throw new ObjectDisposedException($"package owner '{Name}'");
    }

    public override string ToString() => Name;
}
