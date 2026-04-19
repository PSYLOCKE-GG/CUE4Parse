using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CUE4Parse.Compression;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.FileProvider.Objects;

public class OsGameFile : VersionedGameFile
{
    public readonly FileInfo ActualFile;

    public OsGameFile(FileInfo info, VersionContainer versions) : base(info.FullName.Replace('\\', '/'), info.Length, versions)
    {
        ActualFile = info;
    }

    public OsGameFile(DirectoryInfo baseDir, FileInfo info, string mountPoint, VersionContainer versions)
        : base(System.IO.Path.GetRelativePath(baseDir.FullName, info.FullName).Replace('\\', '/'), info.Length, versions)
    {
        ActualFile = info;
    }

    public override bool IsEncrypted => false;
    public override CompressionMethod CompressionMethod => CompressionMethod.None;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override byte[] Read(FByteBulkDataHeader? header = null)
    {
        if (header != null)
        {
            using var stream = ActualFile.OpenRead();
            stream.Seek(header.Value.OffsetInFile, SeekOrigin.Begin);
            var buffer = new byte[header.Value.SizeOnDisk];
            stream.ReadExactly(buffer, 0, buffer.Length);
            return buffer;
        }

        return File.ReadAllBytes(ActualFile.FullName);
    }

    public override Task<byte[]> ReadAsync(CancellationToken cancellationToken)
        => File.ReadAllBytesAsync(ActualFile.FullName, cancellationToken);

    // Match sync CreateReader shape: read bytes fully into memory, wrap in FByteArchive.
    // An FStreamArchive would change Dispose semantics (closes the file) and streaming behavior —
    // a silent divergence from sync would surprise consumers migrating to the async API.
    public override async Task<FArchive> CreateReaderAsync(CancellationToken cancellationToken)
        => new FByteArchive(Path, await ReadAsync(cancellationToken).ConfigureAwait(false), Versions);
}
