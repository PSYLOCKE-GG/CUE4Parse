using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;

namespace CUE4Parse.FileProvider;

/// <summary>
/// Gives loaded packages identity: while a package is resident, LoadPackage/LoadPackageAsync
/// return the same instance instead of re-reading, re-decrypting and re-decompressing it from
/// the container. Without this, every import resolution rebuilds its target package, so assets
/// shared by thousands of packages (compression settings, skeletons, ...) are re-read forever.
/// </summary>
/// <remarks>
/// Disabled by default. When enabled, residency is bounded by <see cref="MaxBytes"/> with LRU
/// eviction. A package weighs its stored file size: the serialized bytes it keeps resident. Concurrent misses may load the same package independently, then converge on the
/// first resident instance. Package construction can synchronously resolve other packages, so
/// sharing incomplete loads would deadlock when concurrent dependency paths cross. Failed loads
/// are never cached. <see cref="HitCount"/>/<see cref="MissCount"/>/<see cref="EvictionCount"/>
/// make residency distinguishable from thrash.
/// </remarks>
public class PackageCache
{
    /// <summary>Turns residency on. Off, every load constructs a fresh package (historical behavior).</summary>
    public bool Enabled { get; set; }

    /// <summary>Residency ceiling in stored bytes. A package that alone exceeds it is returned but not kept.</summary>
    public long MaxBytes { get; set; } = 1L << 30;

    private long _hits, _misses, _evictions;
    public long HitCount => Interlocked.Read(ref _hits);
    public long MissCount => Interlocked.Read(ref _misses);
    public long EvictionCount => Interlocked.Read(ref _evictions);
    public int ResidentCount
    {
        get { lock (_lock) return _residents.Count; }
    }
    public long ResidentBytes
    {
        get { lock (_lock) return _residentBytes; }
    }

    // Reference equality on GameFile keeps identical paths from different containers distinct;
    // flags participate so a metadata-only load never satisfies a full-fidelity one.
    private readonly record struct Key(GameFile File, EPackageReadFlags Flags);

    private readonly Lock _lock = new();
    private readonly Dictionary<Key, LinkedListNode<(Key Key, IPackage Package)>> _residents = [];
    private readonly LinkedList<(Key Key, IPackage Package)> _lru = [];
    private long _residentBytes;
    public IPackage GetOrLoad(GameFile file, EPackageReadFlags readFlags, Func<GameFile, IPackage> loader)
    {
        var key = new Key(file, readFlags);
        if (TryGetResident(key, out var resident)) return resident;
        Interlocked.Increment(ref _misses);
        return RegisterOrGet(key, loader(file));
    }

    public async Task<IPackage> GetOrLoadAsync(
        GameFile file, EPackageReadFlags readFlags, Func<GameFile, Task<IPackage>> loader)
    {
        var key = new Key(file, readFlags);
        if (TryGetResident(key, out var resident)) return resident;
        Interlocked.Increment(ref _misses);
        var loaded = await loader(file).ConfigureAwait(false);
        return RegisterOrGet(key, loaded);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _residents.Clear();
            _lru.Clear();
            _residentBytes = 0;
        }
    }

    /// <summary>Evicts one resident package while leaving unrelated hot packages intact.</summary>
    public bool Remove(GameFile file, EPackageReadFlags readFlags = EPackageReadFlags.None)
    {
        var key = new Key(file, readFlags);
        lock (_lock)
        {
            if (!_residents.Remove(key, out var node)) return false;
            _lru.Remove(node);
            _residentBytes -= key.File.Size;
            return true;
        }
    }

    private bool TryGetResident(Key key, out IPackage package)
    {
        lock (_lock)
        {
            if (_residents.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                Interlocked.Increment(ref _hits);
                package = node.Value.Package;
                return true;
            }
        }

        package = null!;
        return false;
    }

    private IPackage RegisterOrGet(Key key, IPackage package)
    {
        lock (_lock)
        {
            if (_residents.TryGetValue(key, out var resident))
            {
                _lru.Remove(resident);
                _lru.AddFirst(resident);
                return resident.Value.Package;
            }

            _residents[key] = _lru.AddFirst((key, package));
            _residentBytes += key.File.Size;
            while (_residentBytes > MaxBytes)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _residents.Remove(last.Value.Key);
                _residentBytes -= last.Value.Key.File.Size;
                Interlocked.Increment(ref _evictions);
            }

            return package;
        }
    }
}
