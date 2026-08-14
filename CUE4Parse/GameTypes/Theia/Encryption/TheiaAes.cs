using CUE4Parse.UE4.VirtualFileSystem;

namespace CUE4Parse.GameTypes.Theia.Encryption;

public static class TheiaAes
{
    /// <summary>
    /// Passthrough for Theia (Marvel Tokon) containers. The archive itself decrypts
    /// transparently via <see cref="FTheiaArchive"/>; this delegate exists only so the
    /// provider's encrypted-reader gate accepts the container.
    /// </summary>
    public static byte[] TheiaPassthrough(byte[] bytes, int beginOffset, int count, bool isIndex, IAesVfsReader reader)
    {
        if (bytes.Length < beginOffset + count)
            throw new IndexOutOfRangeException("beginOffset + count is larger than the length of bytes");
        if (beginOffset == 0 && count == bytes.Length)
            return bytes;
        var output = new byte[count];
        Buffer.BlockCopy(bytes, beginOffset, output, 0, count);
        return output;
    }
}
