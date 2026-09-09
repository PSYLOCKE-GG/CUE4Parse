using System;
using System.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.Utils;

namespace CUE4Parse.UE4.IO;

public sealed class IoStoreEntryStream : Stream, ICloneable
{
    private readonly IoStoreReader _reader;
    private readonly long _entryOffset;
    private readonly long _entrySize;
    private readonly long _compressionBlockSize;

    private IoStoreBlock? _cachedBlock;
    private int _cachedBlockIndex = -1;
    private long _position;

    public IoStoreEntryStream(FIoStoreEntry entry)
    {
        _reader = entry.IoStoreReader;
        _entryOffset = entry.Offset;
        _entrySize = entry.Size;
        _compressionBlockSize = _reader.TocResource.Header.CompressionBlockSize;
    }

    private IoStoreEntryStream(IoStoreEntryStream other)
    {
        _reader = other._reader;
        _entryOffset = other._entryOffset;
        _entrySize = other._entrySize;
        _compressionBlockSize = other._compressionBlockSize;
        _position = other._position;
    }

    /// <summary>
    /// Returns a stream over the same entry with its own cursor. Lazily deserialized exports
    /// each clone the package archive and seek independently, so they must not share a cursor.
    /// </summary>
    public object Clone() => new IoStoreEntryStream(this);

    public void ReleaseCache()
    {
        _cachedBlock?.Dispose();
        _cachedBlock = null;
        _cachedBlockIndex = -1;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _entrySize;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _entrySize) return 0;

        var remaining = _entrySize - _position;
        var toRead = (int) Math.Min(count, remaining);
        var totalRead = 0;

        while (toRead > 0)
        {
            var absoluteOffset = _entryOffset + _position;
            var blockIndex = (int) (absoluteOffset / _compressionBlockSize);
            var offsetInBlock = (int) (absoluteOffset % _compressionBlockSize);

            if (_cachedBlockIndex != blockIndex)
            {
                _cachedBlock?.Dispose();
                _cachedBlock = _reader.DecompressBlock(blockIndex);
                _cachedBlockIndex = blockIndex;
            }

            var availableInBlock = _cachedBlock!.Length - offsetInBlock;
            var toCopy = Math.Min(toRead, availableInBlock);

            _cachedBlock.Memory.Span.Slice(offsetInBlock, toCopy).CopyTo(buffer.AsSpan(offset, toCopy));
            offset += toCopy;
            _position += toCopy;
            toRead -= toCopy;
            totalRead += toCopy;
        }

        return totalRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _entrySize + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        return _position;
    }

    public override void Flush() { }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ReleaseCache();
        base.Dispose(disposing);
    }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
