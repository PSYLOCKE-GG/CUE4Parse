using System.Buffers;
using System.Threading;

namespace CUE4Parse.UE4.IO;

internal sealed class IoStoreBlock : IDisposable
{
    private byte[]? _buffer;

    private IoStoreBlock(byte[] buffer, int length)
    {
        _buffer = buffer;
        Length = length;
    }

    internal byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(IoStoreBlock));
    internal int Length { get; }
    internal Memory<byte> Memory => Buffer.AsMemory(0, Length);

    internal static IoStoreBlock Rent(int length)
        => new(ArrayPool<byte>.Shared.Rent(length), length);

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
            ArrayPool<byte>.Shared.Return(buffer);
    }
}
