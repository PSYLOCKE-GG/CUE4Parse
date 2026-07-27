using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;

namespace CUE4Parse.Tests;

public class PackageCacheTests
{
    private sealed class FakeGameFile(string path) : GameFile(path, 0)
    {
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;
        public override byte[] Read(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
        public override Task<byte[]> ReadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public override Task<FArchive> CreateReaderAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakePackage : IPackage
    {
        public string Name { get; set; } = "";
        public IFileProvider? Provider => null;
        public TypeMappings? Mappings => null;
        public FPackageFileSummary Summary => throw new NotSupportedException();
        public FNameEntrySerialized[] NameMap => [];
        public int ImportMapLength => 0;
        public int ExportMapLength => 0;
        public IReadOnlyList<ExportInfo> Exports => [];
#pragma warning disable CS0618
        public Lazy<UObject>[] ExportsLazy => [];
#pragma warning restore CS0618
        public bool IsFullyLoaded => true;
        public bool CanDeserialize => false;
        public bool HasFlags(EPackageFlags flags) => false;
        public int GetExportIndex(string name, StringComparison comparisonType = StringComparison.Ordinal) => -1;
        public ResolvedObject? ResolvePackageIndex(FPackageIndex? index) => null;
    }

    [Fact]
    public void ReturnsResidentInstanceOnSecondLoad()
    {
        var cache = new PackageCache { Enabled = true };
        var file = new FakeGameFile("Game/A.uasset");
        var loads = 0;

        var first = cache.GetOrLoad(file, _ => { loads++; return new FakePackage(); });
        var second = cache.GetOrLoad(file, _ => { loads++; return new FakePackage(); });

        Assert.Same(first, second);
        Assert.Equal(1, loads);
        Assert.Equal(1, cache.HitCount);
        Assert.Equal(1, cache.MissCount);
    }

    [Fact]
    public void ConcurrentLoadsOfSamePathAreSingleFlight()
    {
        var cache = new PackageCache { Enabled = true };
        var file = new FakeGameFile("Game/A.uasset");
        var loads = 0;
        using var gate = new ManualResetEventSlim();

        var results = new IPackage[16];
        var threads = Enumerable.Range(0, 16).Select(i => new Thread(() =>
        {
            gate.Wait();
            results[i] = cache.GetOrLoad(file, _ =>
            {
                Interlocked.Increment(ref loads);
                Thread.Sleep(50);
                return new FakePackage();
            });
        })).ToArray();
        foreach (var t in threads) t.Start();
        gate.Set();
        foreach (var t in threads) t.Join();

        Assert.Equal(1, loads);
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public void DistinctFilesWithSamePathAreDistinctEntries()
    {
        var cache = new PackageCache { Enabled = true };
        var a = cache.GetOrLoad(new FakeGameFile("Game/A.uasset"), _ => new FakePackage());
        var b = cache.GetOrLoad(new FakeGameFile("Game/A.uasset"), _ => new FakePackage());
        Assert.NotSame(a, b);
    }

    [Fact]
    public void EvictsLeastRecentlyUsedBeyondMaxSize()
    {
        var cache = new PackageCache { Enabled = true, MaxSize = 2 };
        var a = new FakeGameFile("Game/A.uasset");
        var b = new FakeGameFile("Game/B.uasset");
        var c = new FakeGameFile("Game/C.uasset");

        var pa = cache.GetOrLoad(a, _ => new FakePackage());
        cache.GetOrLoad(b, _ => new FakePackage());
        cache.GetOrLoad(a, _ => new FakePackage()); // touch A so B is LRU
        cache.GetOrLoad(c, _ => new FakePackage()); // evicts B

        Assert.Equal(1, cache.EvictionCount);
        Assert.Same(pa, cache.GetOrLoad(a, _ => new FakePackage()));
        var reloads = 0;
        cache.GetOrLoad(b, _ => { reloads++; return new FakePackage(); });
        Assert.Equal(1, reloads);
    }

    [Fact]
    public void FailedLoadIsNotCachedAndRetries()
    {
        var cache = new PackageCache { Enabled = true };
        var file = new FakeGameFile("Game/A.uasset");

        Assert.Throws<InvalidOperationException>(() =>
            cache.GetOrLoad(file, _ => throw new InvalidOperationException("boom")));

        var pkg = cache.GetOrLoad(file, _ => new FakePackage());
        Assert.NotNull(pkg);
    }

    [Fact]
    public async Task AsyncAndSyncLoadsShareResidency()
    {
        var cache = new PackageCache { Enabled = true };
        var file = new FakeGameFile("Game/A.uasset");

        var fromAsync = await cache.GetOrLoadAsync(file, _ => Task.FromResult<IPackage>(new FakePackage()));
        var fromSync = cache.GetOrLoad(file, _ => new FakePackage());

        Assert.Same(fromAsync, fromSync);
    }

    [Fact]
    public void ClearDropsResidencyWithoutCountingEvictions()
    {
        var cache = new PackageCache { Enabled = true };
        var file = new FakeGameFile("Game/A.uasset");
        var first = cache.GetOrLoad(file, _ => new FakePackage());
        cache.Clear();
        var second = cache.GetOrLoad(file, _ => new FakePackage());

        Assert.NotSame(first, second);
        Assert.Equal(0, cache.EvictionCount);
    }
}
