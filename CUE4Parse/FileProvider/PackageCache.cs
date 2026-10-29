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
/// eviction. A package weighs the live managed bytes it keeps: the bytes its parse allocated, plus
/// the bytes each export deserialization allocates (<see cref="Grow"/>). Nested package loads and
/// export loads weigh their own residents, so a dependency is not counted twice. Ubulk payload reads
/// are not counted. An evicted package that is still referenced leaves the LRU; its objects live as
/// long as their holders. Concurrent misses may load the same package independently, then converge on the
/// first resident instance. Package construction can synchronously resolve other packages, so
/// sharing incomplete loads would deadlock when concurrent dependency paths cross. Failed loads
/// are never cached. <see cref="HitCount"/>/<see cref="MissCount"/>/<see cref="EvictionCount"/>
/// make residency distinguishable from thrash.
/// </remarks>
public class PackageCache
{
    /// <summary>Turns residency on. Off, every load constructs a fresh package (historical behavior).</summary>
    public bool Enabled { get; set; }

    /// <summary>Residency ceiling in parse-allocated managed bytes. A package that alone exceeds it is returned but not kept.</summary>
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
    private sealed class Entry(Key key, IPackage package, long bytes)
    {
        public Key Key { get; } = key;
        public IPackage Package { get; } = package;
        public long Bytes { get; set; } = bytes;
    }

    private readonly Dictionary<Key, LinkedListNode<Entry>> _residents = [];
    private readonly Dictionary<IPackage, LinkedListNode<Entry>> _byPackage = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<Entry> _lru = [];
    private long _residentBytes;

    [ThreadStatic] private static long t_nestedBytes;

    /// <summary>
    /// Runs a synchronous parse or export load and returns the managed bytes it allocated on this thread. The bytes of
    /// nested measured loads are not included, so a package and its dependencies are not counted twice.
    /// </summary>
    public static (T Value, long Bytes) MeasureParse<T>(Func<T> parse)
    {
        var outerNested = t_nestedBytes;
        t_nestedBytes = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            var value = parse();
            return (value, GC.GetAllocatedBytesForCurrentThread() - before - t_nestedBytes);
        }
        finally
        {
            t_nestedBytes = outerNested + GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    public IPackage GetOrLoad(GameFile file, EPackageReadFlags readFlags, Func<GameFile, IPackage> loader)
    {
        var key = new Key(file, readFlags);
        if (TryGetResident(key, out var resident)) return resident;
        Interlocked.Increment(ref _misses);
        var (package, bytes) = MeasureParse(() => loader(file));
        return RegisterOrGet(key, package, bytes);
    }

    public async Task<IPackage> GetOrLoadAsync(
        GameFile file, EPackageReadFlags readFlags, Func<GameFile, Task<(IPackage Package, long Bytes)>> loader)
    {
        var key = new Key(file, readFlags);
        if (TryGetResident(key, out var resident)) return resident;
        Interlocked.Increment(ref _misses);
        var (package, bytes) = await loader(file).ConfigureAwait(false);
        return RegisterOrGet(key, package, bytes);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _residents.Clear();
            _byPackage.Clear();
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
            _byPackage.Remove(node.Value.Package);
            _lru.Remove(node);
            _residentBytes -= node.Value.Bytes;
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

    private IPackage RegisterOrGet(Key key, IPackage package, long bytes)
    {
        lock (_lock)
        {
            if (_residents.TryGetValue(key, out var resident))
            {
                _lru.Remove(resident);
                _lru.AddFirst(resident);
                return resident.Value.Package;
            }

            var node = _lru.AddFirst(new Entry(key, package, bytes));
            _residents[key] = node;
            _byPackage[package] = node;
            _residentBytes += bytes;
            EvictBeyondMax();
            return package;
        }
    }

    /// <summary>Adds the bytes a resident package allocated after its parse. A package that is not resident is ignored.</summary>
    public void Grow(IPackage package, long bytes)
    {
        lock (_lock)
        {
            if (!_byPackage.TryGetValue(package, out var node)) return;
            node.Value.Bytes += bytes;
            _residentBytes += bytes;
            EvictBeyondMax();
        }
    }

    private void EvictBeyondMax()
    {
        while (_residentBytes > MaxBytes)
        {
            var last = _lru.Last!;
            _lru.RemoveLast();
            _residents.Remove(last.Value.Key);
            _byPackage.Remove(last.Value.Package);
            _residentBytes -= last.Value.Bytes;
            Interlocked.Increment(ref _evictions);
        }
    }
}
