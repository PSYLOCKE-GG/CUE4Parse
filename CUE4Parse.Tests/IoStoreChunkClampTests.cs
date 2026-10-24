using CUE4Parse.UE4.IO;

namespace CUE4Parse.Tests;

public class IoStoreChunkClampTests
{
    [Theory]
    [InlineData(2097152, 0, 4194304, 2097152)]
    [InlineData(2097152, 0, 1048576, 1048576)]
    [InlineData(2097152, 1048576, 4194304, 1048576)]
    [InlineData(2097152, 2097152, 16, 0)]
    [InlineData(2097152, 3145728, 16, 0)]
    public void Request_past_the_chunk_end_is_clamped_to_the_chunk(long chunkSize, long offset, long requested, long expected)
        => Assert.Equal(expected, IoStoreReader.ClampToChunk(chunkSize, offset, requested));
}
