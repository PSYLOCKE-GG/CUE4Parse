using System.Collections.Frozen;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CUE4Parse.Compression;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.Utils;

namespace CUE4Parse.FileProvider.Objects;

public abstract class GameFile
{
    
    public static readonly string[] UePackageExtensions = ["uasset", "umap"];
    public static readonly string[] UePackagePayloadExtensions = ["uexp", "ubulk", "uptnl"];
    public static readonly string[] UeKnownExtensions =
    [
        ..UePackageExtensions, ..UePackagePayloadExtensions,
        "bin", "ini", "uplugin", "upluginmanifest", "locres", "locmeta",
        "wem", "bnk", "pck", "bank", "awb", "acb"
    ];

    // Immutable lookup tables optimized once during startup.
    public static readonly FrozenSet<string> UePackageExtensionsSet = UePackageExtensions.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    public static readonly FrozenSet<string> UePackagePayloadExtensionsSet = UePackagePayloadExtensions.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    public static readonly FrozenSet<string> UeKnownExtensionsSet = UeKnownExtensions.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // Avoid retaining duplicate extension and directory strings for every file.
    private static readonly ConcurrentDictionary<string, string> _internedExtensions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> _internedDirectories = new(StringComparer.Ordinal);

    private string _path;
    private string? _directory;
    private string? _pathWithoutExtension;
    private string? _name;
    private string? _nameWithoutExtension;
    private string? _extension;

    protected GameFile() { }
    protected GameFile(string path, long size)
    {
        Path = path;
        Size = size;
    }

    public abstract bool IsEncrypted { get; }
    public abstract CompressionMethod CompressionMethod { get; }

    public string Path
    {
        get => _path;
        protected internal set
        {
            _path = value;

            _directory = null;
            _pathWithoutExtension = null;
            _name = null;
            _nameWithoutExtension = null;
            _extension = null;
        }
    }
    public long Size { get; protected init; }

    public string Directory => _directory ??= Intern(_internedDirectories, Path.SubstringBeforeLast('/'));
    public string PathWithoutExtension => _pathWithoutExtension ??= Path.SubstringBeforeLast('.');
    public string Name => _name ??= Path.SubstringAfterLast('/');
    public string NameWithoutExtension
    {
        get
        {
            if (_nameWithoutExtension is not null) return _nameWithoutExtension;

            var nameStart = Path.LastIndexOf('/') + 1;
            var extensionSeparator = Path.LastIndexOf('.');
            return _nameWithoutExtension = extensionSeparator < nameStart
                ? Name
                : Path.Substring(nameStart, extensionSeparator - nameStart);
        }
    }
    public string Extension => _extension ??= Intern(_internedExtensions, Name.SubstringAfterLast('.'));

    public bool IsUePackage => UePackageExtensionsSet.Contains(Extension);
    public bool IsUePackagePayload => UePackagePayloadExtensionsSet.Contains(Extension);

    public abstract byte[] Read(FByteBulkDataHeader? header = null);
    public abstract FArchive CreateReader(FByteBulkDataHeader? header = null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRead([MaybeNullWhen(false)] out byte[] data, FByteBulkDataHeader? header = null)
    {
        try
        {
            data = Read(header);
        }
        catch (Exception e)
        {
            Log.Error(e, "Could not read GameFile {GameFile}", this);
            data = null;
        }
        return data != null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryCreateReader([MaybeNullWhen(false)] out FArchive reader, FByteBulkDataHeader? header = null)
    {
        try
        {
            reader = CreateReader(header);
        }
        catch (Exception e)
        {
            Log.Error(e, "Could not create reader for GameFile {GameFile}", this);
            reader = null;
        }
        return reader != null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[]? SafeRead(FByteBulkDataHeader? header = null)
    {
        TryRead(out var data, header);
        return data;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FArchive? SafeCreateReader(FByteBulkDataHeader? header = null)
    {
        TryCreateReader(out var reader, header);
        return reader;
    }

    // Async entry points — the CT-taking virtuals are the real primitives. Subclasses with
    // a genuine async path (FPakEntry, FIoStoreEntry, OsGameFile) override these. The
    // parameterless overloads exist for API compatibility and forward with CancellationToken.None.

    public abstract Task<byte[]> ReadAsync(CancellationToken cancellationToken);

    public abstract Task<FArchive> CreateReaderAsync(CancellationToken cancellationToken);

    public virtual async Task<byte[]?> SafeReadAsync(CancellationToken cancellationToken)
    {
        try { return await ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception e)
        {
            Log.Error(e, "Could not read GameFile {GameFile}", this);
            return null;
        }
    }

    public virtual async Task<FArchive?> SafeCreateReaderAsync(CancellationToken cancellationToken)
    {
        try { return await CreateReaderAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception e)
        {
            Log.Error(e, "Could not create reader for GameFile {GameFile}", this);
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<byte[]> ReadAsync() => ReadAsync(CancellationToken.None);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<FArchive> CreateReaderAsync() => CreateReaderAsync(CancellationToken.None);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<byte[]?> SafeReadAsync() => SafeReadAsync(CancellationToken.None);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<FArchive?> SafeCreateReaderAsync() => SafeCreateReaderAsync(CancellationToken.None);

    public override string ToString() => Path;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string Intern(ConcurrentDictionary<string, string> pool, string value) =>
        pool.GetOrAdd(value, static candidate => candidate);
}
