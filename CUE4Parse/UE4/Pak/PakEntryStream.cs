using System;
using System.IO;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Pak.Objects;

namespace CUE4Parse.UE4.Pak;

/// <summary>
/// A read-only Stream backed by a pak entry that reads on demand via partial extraction.
/// Avoids loading the entire entry into memory — critical for multi-GB files like shader archives.
/// </summary>
public sealed class PakEntryStream : Stream
{
    private readonly FPakEntry _entry;
    private long _position;

    public PakEntryStream(FPakEntry entry)
    {
        _entry = entry;
        _position = 0;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _entry.UncompressedSize;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= Length) return 0;

        var remaining = Length - _position;
        var toRead = (int)Math.Min(count, remaining);

        var bulk = new FByteBulkDataHeader(
            EBulkDataFlags.BULKDATA_NoOffsetFixUp, 0, (uint)toRead, _position, default);
        var data = _entry.Read(bulk);

        Array.Copy(data, 0, buffer, offset, data.Length);
        _position += data.Length;
        return data.Length;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
