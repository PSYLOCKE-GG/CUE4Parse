using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CUE4Parse.Compression;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.Utils;
using Serilog;

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

    // hashset for quick lookup
    public static readonly HashSet<string> UePackageExtensionsSet = UePackageExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
    public static readonly HashSet<string> UePackagePayloadExtensionsSet = UePackagePayloadExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
    public static readonly HashSet<string> UeKnownExtensionsSet = UeKnownExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);

    // so we don't end up with a lot of duplicate "uasset"s in memory
    private static readonly ConcurrentDictionary<string, string> _internedExtensions = new(StringComparer.OrdinalIgnoreCase);

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

    // Cache frequently accessed path properties for better performance
    public string Directory => _directory ??= Path.SubstringBeforeLast('/');
    public string PathWithoutExtension => _pathWithoutExtension ??= Path.SubstringBeforeLast('.');
    public string Name => _name ??= Path.SubstringAfterLast('/');
    public string NameWithoutExtension => _nameWithoutExtension ??= Name.SubstringBeforeLast('.');
    public string Extension => _extension ??= InternExtension(Name.SubstringAfterLast('.'));

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
            Log.Error(e, $"Could not read GameFile {this}");
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
            Log.Error(e, $"Could not create reader for GameFile {this}");
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

    public virtual Task<byte[]> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read());
    }

    public virtual Task<FArchive> CreateReaderAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateReader());
    }

    public virtual async Task<byte[]?> SafeReadAsync(CancellationToken cancellationToken)
    {
        try { return await ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception e)
        {
            Log.Error(e, $"Could not read GameFile {this}");
            return null;
        }
    }

    public virtual async Task<FArchive?> SafeCreateReaderAsync(CancellationToken cancellationToken)
    {
        try { return await CreateReaderAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception e)
        {
            Log.Error(e, $"Could not create reader for GameFile {this}");
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
    private static string InternExtension(string extension)
    {
        if (_internedExtensions.TryGetValue(extension, out var interned))
            return interned;

        _internedExtensions[extension] = extension;
        return extension;
    }
}
