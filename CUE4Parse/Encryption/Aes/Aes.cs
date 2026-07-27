using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace CUE4Parse.Encryption.Aes;

public static class Aes
{
    public const int ALIGN = 16;
    public const int BLOCK_SIZE = 16 * 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Decrypt(this byte[] encrypted, FAesKey key) =>
        Decrypt(encrypted, 0, encrypted.Length, key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Decrypt(this ArraySegment<byte> encrypted, FAesKey key)
    {
        if (encrypted.Array is null) throw new ArgumentException("ArraySegment has no backing array.", nameof(encrypted));

        return Decrypt(encrypted.Array, encrypted.Offset, encrypted.Count, key);
    }

    // One-shot span decrypt into an uninitialized output buffer. The ICryptoTransform path
    // routes every block through the BCL's pooled transform buffers, whose mandatory
    // ZeroMemory scrub dwarfs the decrypt itself on multi-MB container blocks.
    public static byte[] Decrypt(this byte[] encrypted, int beginOffset, int count, FAesKey key)
    {
        var output = GC.AllocateUninitializedArray<byte>(count);
        key.Provider.DecryptEcb(encrypted.AsSpan(beginOffset, count), output, PaddingMode.None);
        return output;
    }
}
