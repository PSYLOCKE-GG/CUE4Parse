using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.VirtualFileSystem;

namespace CUE4Parse.FileProvider.Vfs
{
    public class FileProviderDictionary : IReadOnlyDictionary<string, GameFile>
    {
        private readonly ConcurrentBag<KeyValuePair<long, IReadOnlyDictionary<string, GameFile>>> _indicesBag = new ();

        private readonly ConcurrentDictionary<FPackageId, GameFile> _byId = new ();
        public IReadOnlyDictionary<FPackageId, GameFile> ById => _byId;

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
        public void AddFiles(IReadOnlyDictionary<string, GameFile> newFiles, long readOrder = 0)
        {
            foreach (var file in newFiles.Values)
            {
                // packages, their optional variant and their respective payloads share the same id
                // only load the normal package in this dict for later use by IoPackage.ImportedPackages
                if (file is FIoStoreEntry { IsUePackage: true } ioEntry && !file.NameWithoutExtension.EndsWith(".o"))
                {
                    var packageId = ioEntry.ChunkId.AsPackageId();
                    _byId.AddOrUpdate(packageId, file, (_, existing) =>
                        existing is VfsEntry existingVfs && file is VfsEntry newVfs
                            && existingVfs.Vfs.ReadOrder >= newVfs.Vfs.ReadOrder
                            ? existing : file);
                }
            }
            _indicesBag.Add(new KeyValuePair<long, IReadOnlyDictionary<string, GameFile>>(readOrder, newFiles));
            InvalidateSortCache();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            _indicesBag.Clear();
            _byId.Clear();
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

        public int Count => _indicesBag.Sum(it => it.Value.Count);

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