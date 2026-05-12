using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CUE4Parse.Compression;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.VirtualFileCache;

namespace CUE4Parse.FileProvider.Objects
{
    public class VfcGameFile : VersionedGameFile
    {
        public readonly FBlockFile[] BlockFiles;
        public readonly FRangeId[] Ranges;

        private readonly string _persistentDownloadDir;

        public VfcGameFile(FBlockFile[] blockFiles, FDataReference dataReference, string persistentDownloadDir, string path, VersionContainer versions)
            : base(path, dataReference.TotalSize, versions)
        {
            BlockFiles = blockFiles;
            Ranges = dataReference.Ranges;

            _persistentDownloadDir = persistentDownloadDir;
        }

        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override byte[] Read(FByteBulkDataHeader? header = null)
        {
            var offset = 0;
            var data = new byte[Size];
            foreach (var r in Ranges)
            {
                var blockSize = BlockFiles.First(x => x.FileId == r.FileId).BlockSize;
                using var fs = new FileStream(System.IO.Path.Combine(_persistentDownloadDir, r.GetPersistentDownloadPath()), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.Seek(r.Range.StartIndex * blockSize, SeekOrigin.Begin);
                offset += fs.Read(data, offset, r.Range.NumBlocks * blockSize);
            }
            return data;
        }

        public override async Task<byte[]> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = 0;
            var data = new byte[Size];
            foreach (var r in Ranges)
            {
                var blockSize = BlockFiles.First(x => x.FileId == r.FileId).BlockSize;
                await using var fs = new FileStream(System.IO.Path.Combine(_persistentDownloadDir, r.GetPersistentDownloadPath()), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                fs.Seek(r.Range.StartIndex * blockSize, SeekOrigin.Begin);
                offset += await fs.ReadAsync(data.AsMemory(offset, r.Range.NumBlocks * blockSize), cancellationToken).ConfigureAwait(false);
            }
            return data;
        }

        public override async Task<FArchive> CreateReaderAsync(CancellationToken cancellationToken)
        {
            var bytes = await ReadAsync(cancellationToken).ConfigureAwait(false);
            return new FByteArchive(Path, bytes, Versions);
        }
    }
}
