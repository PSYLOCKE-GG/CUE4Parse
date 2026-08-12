using System;

namespace CUE4Parse_Conversion.Textures.BC;

public static partial class BCDecoder
{
    internal delegate void BlockDecode(ReadOnlySpan<byte> input, int sizeX, int sizeY, int sizeZ, Span<byte> output);

    /// <summary>
    /// Decodes block-compressed data whose pixel dimensions may not be multiples of the 4x4
    /// block size. Compressed storage always holds whole blocks, and the per-format decoders
    /// assume block-aligned dimensions, so this decodes at the aligned size and crops the
    /// result to the true pixel dimensions. All BC decoders emit 4 bytes per pixel.
    /// </summary>
    internal static byte[] DecodeWithPartialBlocks(byte[] input, int sizeX, int sizeY, int sizeZ, BlockDecode decode)
    {
        var alignedX = (sizeX + 3) & ~3;
        var alignedY = (sizeY + 3) & ~3;
        var aligned = new byte[alignedX * alignedY * sizeZ * 4];
        decode(input, alignedX, alignedY, sizeZ, aligned);
        if (alignedX == sizeX && alignedY == sizeY)
            return aligned;

        var output = new byte[sizeX * sizeY * sizeZ * 4];
        var srcRow = alignedX * 4;
        var dstRow = sizeX * 4;
        for (var z = 0; z < sizeZ; z++)
        {
            var src = z * alignedX * alignedY * 4;
            var dst = z * sizeX * sizeY * 4;
            for (var y = 0; y < sizeY; y++)
                Buffer.BlockCopy(aligned, src + y * srcRow, output, dst + y * dstRow, dstRow);
        }
        return output;
    }
}
