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
    internal static byte[] DecodeWithPartialBlocks(ReadOnlySpan<byte> input, int sizeX, int sizeY, int sizeZ, BlockDecode decode)
    {
        var output = GC.AllocateUninitializedArray<byte>(checked(sizeX * sizeY * sizeZ * 4));
        DecodeWithPartialBlocks(input, sizeX, sizeY, sizeZ, output, decode);
        return output;
    }

    internal static void DecodeWithPartialBlocks(ReadOnlySpan<byte> input, int sizeX, int sizeY, int sizeZ, Span<byte> output, BlockDecode decode)
    {
        var outputSize = checked(sizeX * sizeY * sizeZ * 4);
        if (output.Length < outputSize)
            throw new ArgumentException($"Output length {output.Length} is smaller than expected size {outputSize}");

        var alignedX = checked((sizeX + 3) & ~3);
        var alignedY = checked((sizeY + 3) & ~3);
        if (alignedX == sizeX && alignedY == sizeY)
        {
            decode(input, sizeX, sizeY, sizeZ, output[..outputSize]);
            return;
        }

        var alignedSize = checked(alignedX * alignedY * sizeZ * 4);
        var aligned = System.Buffers.ArrayPool<byte>.Shared.Rent(alignedSize);
        try
        {
            decode(input, alignedX, alignedY, sizeZ, aligned.AsSpan(0, alignedSize));

            var source = aligned.AsSpan(0, alignedSize);
            var sourceRowSize = alignedX * 4;
            var outputRowSize = sizeX * 4;
            for (var z = 0; z < sizeZ; z++)
            {
                var sourceSliceOffset = z * alignedX * alignedY * 4;
                var outputSliceOffset = z * sizeX * sizeY * 4;
                for (var y = 0; y < sizeY; y++)
                {
                    source.Slice(sourceSliceOffset + y * sourceRowSize, outputRowSize)
                        .CopyTo(output.Slice(outputSliceOffset + y * outputRowSize, outputRowSize));
                }
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(aligned);
        }
    }
}
