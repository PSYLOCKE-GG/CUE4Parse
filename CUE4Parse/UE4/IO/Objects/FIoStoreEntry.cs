using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CUE4Parse.Compression;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.VirtualFileSystem;
using CUE4Parse.UE4.IO;

namespace CUE4Parse.UE4.IO.Objects
{
    public class FIoStoreEntry : VfsEntry
    {
        public override bool IsEncrypted => IoStoreReader.IsEncrypted;
        public override CompressionMethod CompressionMethod
        {
            get
            {
                var tocResource = IoStoreReader.TocResource;
                var firstBlockIndex = (int) (Offset / tocResource.Header.CompressionBlockSize);
                return tocResource.CompressionMethods[tocResource.CompressionBlocks[firstBlockIndex].CompressionMethodIndex];
            }
        }

        private readonly uint _tocEntryIndex;
        public FIoChunkId ChunkId => IoStoreReader.TocResource.ChunkIds[_tocEntryIndex];

        /// <summary>
        /// Per-entry chunk hash from the TOC's ChunkMetas array. Requires the IoStoreReader
        /// to have been opened with <see cref="EIoStoreTocReadOptions.ReadTocMeta"/> (or
        /// <see cref="EIoStoreTocReadOptions.ReadAll"/>); throws otherwise so callers see a
        /// clear failure instead of a silent default.
        /// </summary>
        public FSHAHash ChunkHash
        {
            get
            {
                var metas = IoStoreReader.TocResource.ChunkMetas
                    ?? throw new InvalidOperationException(
                        "FIoStoreEntry.ChunkHash requires the TOC to be loaded with EIoStoreTocReadOptions.ReadTocMeta. "
                        + "Construct the IoStoreReader with EIoStoreTocReadOptions.ReadAll.");
                return metas[_tocEntryIndex].ChunkHash;
            }
        }

        public FIoStoreEntry(IoStoreReader reader, string path, uint tocEntryIndex) : base(reader, path)
        {
            _tocEntryIndex = tocEntryIndex;
            ref var offsetLength = ref reader.TocResource.ChunkOffsetLengths[tocEntryIndex];
            Offset = (long) offsetLength.Offset;
            Size = (long) offsetLength.Length;
        }

        public FIoStoreEntry(IoStoreReader reader, uint tocEntryIndex) : base(reader, "NonIndexed/")
        {
            _tocEntryIndex = tocEntryIndex;
            Path += $"0x{ChunkId.ChunkId:X8}.{ChunkId.GetExtension(reader)}";

            ref var offsetLength = ref reader.TocResource.ChunkOffsetLengths[tocEntryIndex];
            Offset = (long) offsetLength.Offset;
            Size = (long) offsetLength.Length;
        }

        public IoStoreReader IoStoreReader
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (IoStoreReader) Vfs;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override byte[] Read(FByteBulkDataHeader? header = null) => Vfs.Extract(this, header);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override FArchive CreateReader(FByteBulkDataHeader? header = null) => new FByteArchive(Path, Read(header), Vfs.Versions);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Task<byte[]> ReadAsync(CancellationToken cancellationToken)
            => IoStoreReader.ExtractAsync(this, null, cancellationToken);

        public override async Task<FArchive> CreateReaderAsync(CancellationToken cancellationToken)
        {
            var bytes = await ReadAsync(cancellationToken).ConfigureAwait(false);
            return new FByteArchive(Path, bytes, Vfs.Versions);
        }

        public FStreamArchive CreateStreamingReader()
            => new(Path, new IoStoreEntryStream(this), Vfs.Versions);
    }
}
