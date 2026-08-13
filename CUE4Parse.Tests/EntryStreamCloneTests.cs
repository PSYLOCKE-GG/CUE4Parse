using System;
using System.IO;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.Pak;
using CUE4Parse.UE4.Readers;

namespace CUE4Parse.Tests;

/// <summary>
/// A package's exports each deserialize from a clone of the package archive, and those loads run
/// concurrently. Every stream that can back a package archive therefore has to clone into an
/// independent cursor; a clone that shares one makes concurrent exports read at each other's
/// offsets and silently returns the wrong bytes.
/// </summary>
public class EntryStreamCloneTests
{
    private sealed class CloneableStream(byte[] data) : Stream, ICloneable
    {
        private long _position;

        public object Clone() => new CloneableStream(data) { _position = _position };

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => data.Length;

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var toRead = (int) Math.Min(count, Length - _position);
            Buffer.BlockCopy(data, (int) _position, buffer, offset, toRead);
            _position += toRead;
            return toRead;
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

    [Theory]
    [InlineData(typeof(IoStoreEntryStream))]
    [InlineData(typeof(PakEntryStream))]
    public void EntryStreamsAreCloneable(Type streamType)
    {
        Assert.True(
            typeof(ICloneable).IsAssignableFrom(streamType),
            $"{streamType.Name} backs package archives, so it must clone into an independent " +
            "cursor instead of being shared by FStreamArchive.Clone");
    }

    [Fact]
    public void CloningKeepsCursorsIndependent()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var original = new FStreamArchive("entry", new CloneableStream(data));
        original.Position = 2;

        var clone = (FStreamArchive) original.Clone();
        clone.Position = 6;

        Assert.Equal(2, original.Position);
        Assert.Equal(3, original.Read<byte>());
        Assert.Equal(7, clone.Read<byte>());
        Assert.NotSame(original.BaseStream, clone.BaseStream);
    }
}
