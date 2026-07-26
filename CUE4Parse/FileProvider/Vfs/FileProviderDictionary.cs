using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.VirtualFileSystem;

namespace CUE4Parse.FileProvider.Vfs
{
    public class FileProviderDictionary : IReadOnlyDictionary<string, GameFile>
    {
        private readonly ConcurrentBag<KeyValuePair<long, IReadOnlyDictionary<string, GameFile>>> _indicesBag = new ();

        private ConcurrentDictionary<FPackageId, GameFile>? _byId;
        private int _count;
        public IReadOnlyDictionary<FPackageId, GameFile> ById => GetPackageIndex();

        private readonly KeyEnumerable _keys;
        public IEnumerable<string> Keys => _keys;

        private readonly ValueEnumerable _values;
        public IEnumerable<GameFile> Values => _values;

        private volatile KeyValuePair<long, IReadOnlyDictionary<string, GameFile>>[]? _sortedIndicesCache;
        private readonly object _sortCacheLock = new object();

        public FileProviderDictionary()
        {
            _keys = new KeyEnumerable(this);
            _values = new ValueEnumerable(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private KeyValuePair<long, IReadOnlyDictionary<string, GameFile>>[] GetSortedIndices()
        {
            var cache = _sortedIndicesCache;
            if (cache != null)
                return cache;

            lock (_sortCacheLock)
            {
                cache = _sortedIndicesCache;
                if (cache != null)
                    return cache;

                cache = _indicesBag.OrderByDescending(kvp => kvp.Key).ToArray();
                _sortedIndicesCache = cache;
                return cache;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvalidateSortCache()
        {
            _sortedIndicesCache = null;
        }

        private static readonly IReadOnlyList<GameFile> _emptyGameFileList = new List<GameFile>().AsReadOnly();

        internal void PreallocatePackageIndex(int capacity)
        {
            if (capacity <= 0 || Volatile.Read(ref _byId) is not null)
                return;

            var packageIndex = new ConcurrentDictionary<FPackageId, GameFile>(
                Environment.ProcessorCount, capacity);
            Interlocked.CompareExchange(ref _byId, packageIndex, null);
        }

        // A lower-read-order vfs must not override a package already claimed by a higher-read-order one.
        private static void AddPackage(ConcurrentDictionary<FPackageId, GameFile> packageIndex, FPackageId packageId, GameFile file) =>
            packageIndex.AddOrUpdate(packageId, file, (_, existing) =>
                existing is VfsEntry existingVfs && file is VfsEntry newVfs
                    && existingVfs.Vfs.ReadOrder >= newVfs.Vfs.ReadOrder
                    ? existing : file);

        private ConcurrentDictionary<FPackageId, GameFile> GetPackageIndex()
        {
            var packageIndex = Volatile.Read(ref _byId);
            if (packageIndex is not null)
                return packageIndex;

            var newPackageIndex = new ConcurrentDictionary<FPackageId, GameFile>();
            return Interlocked.CompareExchange(ref _byId, newPackageIndex, null) ?? newPackageIndex;
        }

        public void FindPayloads(GameFile file, out GameFile? uexp, out IReadOnlyList<GameFile> ubulks, out IReadOnlyList<GameFile> uptnls, bool cookedIndexLookup = false)
        {
            uexp = null;
            ubulks = uptnls = _emptyGameFileList;
            if (!file.IsUePackage) return;

            List<GameFile>? ubulkList = null;
            List<GameFile>? uptnlList = null;

            var path = file.PathWithoutExtension;
            if (cookedIndexLookup && file is FIoStoreEntry { IsUePackage: true } entry)
            {
                foreach (var payload in entry.IoStoreReader.Files.Values)
                {
                    if (!payload.IsUePackagePayload || payload is not FIoStoreEntry y || y.ChunkId.ChunkId != entry.ChunkId.ChunkId)
                        continue;
                    switch (payload.Extension)
                    {
                        case "ubulk":
                            (ubulkList ??= new List<GameFile>()).Add(payload);
                            break;
                        case "uptnl":
                            (uptnlList ??= new List<GameFile>()).Add(payload);
                            break;
                    }
                }
            }
            else if (file is VfsEntry {Vfs: { } vfs})
            {
                vfs.Files.TryGetValue(path + ".uexp", out uexp);
                if (vfs.Files.TryGetValue(path + ".ubulk", out var ubulkVfs))
                    (ubulkList ??= new List<GameFile>()).Add(ubulkVfs);
            }

            if (uexp == null) TryGetValue(path + ".uexp", out uexp);
            if (ubulkList == null && TryGetValue(path + ".ubulk", out var ubulk))
                (ubulkList ??= new List<GameFile>()).Add(ubulk);
            if (uptnlList == null && TryGetValue(path + ".uptnl", out var uptnl))
                (uptnlList ??= new List<GameFile>()).Add(uptnl);

            if (ubulkList != null) ubulks = ubulkList;
            if (uptnlList != null) uptnls = uptnlList;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddFiles(IReadOnlyDictionary<string, GameFile> newFiles, long readOrder = 0,
            IReadOnlyDictionary<FPackageId, GameFile>? packageFiles = null)
        {
            if (packageFiles is null)
            {
                ConcurrentDictionary<FPackageId, GameFile>? packageIndex = null;
                foreach (var file in newFiles.Values)
                {
                    // packages, their optional variant and their respective payloads share the same id
                    // only load the normal package in this dict for later use by IoPackage.ImportedPackages
                    if (file is FIoStoreEntry { IsPackageData: true, IsOptionalPackage: false } ioEntry)
                    {
                        AddPackage(packageIndex ??= GetPackageIndex(), ioEntry.ChunkId.AsPackageId(), file);
                    }
                }
            }
            else
            {
                var packageIndex = GetPackageIndex();
                foreach (var (packageId, file) in packageFiles)
                    AddPackage(packageIndex, packageId, file);
            }

            _indicesBag.Add(new KeyValuePair<long, IReadOnlyDictionary<string, GameFile>>(readOrder, newFiles));
            Interlocked.Add(ref _count, newFiles.Count);
            InvalidateSortCache();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _indicesBag.Clear();
            Volatile.Read(ref _byId)?.Clear();
            Interlocked.Exchange(ref _count, 0);
            InvalidateSortCache();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(string key)
        {
            var sortedIndices = GetSortedIndices();
            foreach (var files in sortedIndices)
            {
                if (files.Value.ContainsKey(key))
                    return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(string key, [MaybeNullWhen(false)] out GameFile value)
        {
            var sortedIndices = GetSortedIndices();
            foreach (var files in sortedIndices)
            {
                if (files.Value.TryGetValue(key, out value))
                    return true;
            }

            value = null;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValues(string key, out List<GameFile> values)
        {
            values = [];
            var sortedIndices = GetSortedIndices();
            foreach (var files in sortedIndices)
            {
                if (files.Value.TryGetValue(key, out var value))
                {
                    values.Add(value);
                }
            }
            return values.Count > 0;
        }

        public GameFile this[string path] => TryGetValue(path, out var value) ? value : throw new KeyNotFoundException();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerator<KeyValuePair<string, GameFile>> GetEnumerator()
        {
            var sortedIndices = GetSortedIndices();
            foreach (var index in sortedIndices)
            {
                foreach (var entry in index.Value)
                {
                    yield return entry;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public int Count => Volatile.Read(ref _count);

        private class KeyEnumerable : IEnumerable<string>
        {
            private readonly FileProviderDictionary _orig;

            internal KeyEnumerable(FileProviderDictionary orig)
            {
                _orig = orig;
            }

            public IEnumerator<string> GetEnumerator()
            {
                var sortedIndices = _orig.GetSortedIndices();
                foreach (var index in sortedIndices)
                {
                    foreach (var key in index.Value.Keys)
                    {
                        yield return key;
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private class ValueEnumerable : IEnumerable<GameFile>
        {
            private readonly FileProviderDictionary _orig;

            internal ValueEnumerable(FileProviderDictionary orig)
            {
                _orig = orig;
            }

            public IEnumerator<GameFile> GetEnumerator()
            {
                var sortedIndices = _orig.GetSortedIndices();
                foreach (var index in sortedIndices)
                {
                    foreach (var key in index.Value.Values)
                    {
                        yield return key;
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}