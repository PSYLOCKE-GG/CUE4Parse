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

    private readonly Lock _lock = new();
    private readonly Dictionary<GameFile, LinkedListNode<(GameFile File, IPackage Package)>> _residents = [];
    private readonly LinkedList<(GameFile File, IPackage Package)> _lru = [];
    private readonly ConcurrentDictionary<GameFile, Lazy<Task<IPackage>>> _inFlight = [];

    public IPackage GetOrLoad(GameFile file, Func<GameFile, IPackage> loader)
    {
        if (TryGetResident(file, out var resident)) return resident;
        var lazy = _inFlight.GetOrAdd(file, _ => new Lazy<Task<IPackage>>(
            () => Task.FromResult(LoadAndRegister(file, loader)), LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value.GetAwaiter().GetResult();
    }

    public Task<IPackage> GetOrLoadAsync(GameFile file, Func<GameFile, Task<IPackage>> loader)
    {
        if (TryGetResident(file, out var resident)) return Task.FromResult(resident);
        var lazy = _inFlight.GetOrAdd(file, _ => new Lazy<Task<IPackage>>(
            () => LoadAndRegisterAsync(file, loader), LazyThreadSafetyMode.ExecutionAndPublication));
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

    private IPackage LoadAndRegister(GameFile file, Func<GameFile, IPackage> loader)
    {
        try
        {
            Interlocked.Increment(ref _misses);
            var package = loader(file);
            Register(file, package);
            return package;
        }
        finally
        {
            // Removed only after Register, so late callers find the package resident
            // rather than starting a second load.
            _inFlight.TryRemove(file, out _);
        }
    }

    private async Task<IPackage> LoadAndRegisterAsync(GameFile file, Func<GameFile, Task<IPackage>> loader)
    {
        try
        {
            Interlocked.Increment(ref _misses);
            var package = await loader(file).ConfigureAwait(false);
            Register(file, package);
            return package;
        }
        finally
        {
            _inFlight.TryRemove(file, out _);
        }
    }

    private bool TryGetResident(GameFile file, out IPackage package)
    {
        lock (_lock)
        {
            if (_residents.TryGetValue(file, out var node))
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

    private void Register(GameFile file, IPackage package)
    {
        lock (_lock)
        {
            if (_residents.ContainsKey(file)) return;
            _residents[file] = _lru.AddFirst((file, package));
            while (_residents.Count > MaxSize)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _residents.Remove(last.Value.File);
                Interlocked.Increment(ref _evictions);
            }
        }
    }
}
