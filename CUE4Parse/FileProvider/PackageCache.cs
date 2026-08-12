using System.Collections.Concurrent;
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
/// Disabled by default. When enabled, residency is bounded by <see cref="MaxSize"/> with LRU
/// eviction, loads are single-flight per file (two threads racing on the same package produce
/// one load), and <see cref="HitCount"/>/<see cref="MissCount"/>/<see cref="EvictionCount"/>
/// make residency distinguishable from thrash. Failed loads are never cached.
/// </remarks>
public class PackageCache
{
    /// <summary>Turns residency on. Off, every load constructs a fresh package (historical behavior).</summary>
    public bool Enabled { get; set; }

    /// <summary>Residency ceiling in packages. Kept bounded because packages can pin large buffers (vertex data, bulk data).</summary>
    public int MaxSize { get; set; } = 256;

    private long _hits, _misses, _evictions;
    public long HitCount => Interlocked.Read(ref _hits);
    public long MissCount => Interlocked.Read(ref _misses);
    public long EvictionCount => Interlocked.Read(ref _evictions);
    public int ResidentCount
    {
        get { lock (_lock) return _residents.Count; }
    }

    // Reference equality on GameFile keeps identical paths from different containers distinct;
    // flags participate so a metadata-only load never satisfies a full-fidelity one.
    private readonly record struct Key(GameFile File, EPackageReadFlags Flags);

    private readonly Lock _lock = new();
    private readonly Dictionary<Key, LinkedListNode<(Key Key, IPackage Package)>> _residents = [];
    private readonly LinkedList<(Key Key, IPackage Package)> _lru = [];
    private readonly ConcurrentDictionary<Key, Lazy<Task<IPackage>>> _inFlight = [];

    public IPackage GetOrLoad(GameFile file, EPackageReadFlags readFlags, Func<GameFile, IPackage> loader)
    {
        var key = new Key(file, readFlags);
        if (TryGetResident(key, out var resident)) return resident;
        var lazy = _inFlight.GetOrAdd(key, _ => new Lazy<Task<IPackage>>(
            () => Task.FromResult(LoadAndRegister(key, loader)), LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value.GetAwaiter().GetResult();
    }

    public Task<IPackage> GetOrLoadAsync(GameFile file, EPackageReadFlags readFlags, Func<GameFile, Task<IPackage>> loader)
    {
        var key = new Key(file, readFlags);
        if (TryGetResident(key, out var resident)) return Task.FromResult(resident);
        var lazy = _inFlight.GetOrAdd(key, _ => new Lazy<Task<IPackage>>(
            () => LoadAndRegisterAsync(key, loader), LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _residents.Clear();
            _lru.Clear();
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
            return true;
        }
    }

    private IPackage LoadAndRegister(Key key, Func<GameFile, IPackage> loader)
    {
        try
        {
            Interlocked.Increment(ref _misses);
            var package = loader(key.File);
            Register(key, package);
            return package;
        }
        finally
        {
            // Removed only after Register, so late callers find the package resident
            // rather than starting a second load.
            _inFlight.TryRemove(key, out _);
        }
    }

    private async Task<IPackage> LoadAndRegisterAsync(Key key, Func<GameFile, Task<IPackage>> loader)
    {
        try
        {
            Interlocked.Increment(ref _misses);
            var package = await loader(key.File).ConfigureAwait(false);
            Register(key, package);
            return package;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
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

    private void Register(Key key, IPackage package)
    {
        lock (_lock)
        {
            if (_residents.ContainsKey(key)) return;
            _residents[key] = _lru.AddFirst((key, package));
            while (_residents.Count > MaxSize)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _residents.Remove(last.Value.Key);
                Interlocked.Increment(ref _evictions);
            }
        }
    }
}
