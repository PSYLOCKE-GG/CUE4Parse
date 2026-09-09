using CUE4Parse.UE4.Readers;

namespace CUE4Parse.Tests;

public class ArchiveReadAtTests
{
    [Fact]
    public async Task CursorBackedReadAtSerializesConcurrentReads()
    {
        using var stream = new InterleavingStream([10, 20], TestContext.Current.CancellationToken);
        using var archive = new FStreamArchive("interleaving", stream);
        var first = new byte[1];
        var second = new byte[1];

        var firstRead = Task.Run(() => archive.ReadAt(0, first, 0, 1));
        Assert.True(stream.FirstReadEntered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        var secondTaskStarted = new ManualResetEventSlim();
        var secondRead = Task.Run(() =>
        {
            secondTaskStarted.Set();
            return archive.ReadAt(1, second, 0, 1);
        });
        Assert.True(secondTaskStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        var cursorWasOverwritten = stream.SecondPositionSet.Wait(
            TimeSpan.FromMilliseconds(250),
            TestContext.Current.CancellationToken);
        stream.ReleaseSecondPosition.Set();
        stream.ReleaseFirstRead.Set();

        Assert.Equal(1, await firstRead);
        Assert.Equal(1, await secondRead);
        Assert.False(cursorWasOverwritten);
        Assert.Equal(10, first[0]);
        Assert.Equal(20, second[0]);
    }

    private sealed class InterleavingStream(byte[] data, CancellationToken cancellationToken) : Stream
    {
        private long _position;
        private int _reads;

        public ManualResetEventSlim FirstReadEntered { get; } = new();
        public ManualResetEventSlim ReleaseFirstRead { get; } = new();
        public ManualResetEventSlim SecondPositionSet { get; } = new();
        public ManualResetEventSlim ReleaseSecondPosition { get; } = new();

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => data.Length;

        public override long Position
        {
            get => _position;
            set
            {
                _position = value;
                if (value == 1 && FirstReadEntered.IsSet && !ReleaseFirstRead.IsSet)
                {
                    SecondPositionSet.Set();
                    Assert.True(ReleaseSecondPosition.Wait(TimeSpan.FromSeconds(5), cancellationToken));
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Interlocked.Increment(ref _reads) == 1)
            {
                FirstReadEntered.Set();
                Assert.True(ReleaseFirstRead.Wait(TimeSpan.FromSeconds(5), cancellationToken));
            }

            var read = Math.Min(count, data.Length - (int)_position);
            data.AsSpan((int)_position, read).CopyTo(buffer.AsSpan(offset, read));
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            return Position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
