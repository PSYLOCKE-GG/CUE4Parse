using System.Buffers.Binary;
using System.Reflection;
using CUE4Parse_Conversion.Textures;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;

namespace CUE4Parse.Tests;

// A cooked mip holds its slices one after another, each a whole number of blocks: a volume mip holds Mip.SizeZ slices
// and a cube mip holds the platform data's slice count (TextureDerivedData.cpp:2289-2295).
public class SliceDecodeTests
{
    private sealed class InlineBulkData(byte[] data) : TBulkData<byte>(data);

    private sealed class Dxt5Volume : UVolumeTexture
    {
        public Dxt5Volume() => Format = EPixelFormat.PF_DXT5;
    }

    private sealed class Dxt5Cube : UTextureCube
    {
        public Dxt5Cube() => Format = EPixelFormat.PF_DXT5;
    }

    private static readonly ushort[] Colors = [0xF800, 0x07E0, 0x001F, 0xFFE0, 0xF81F, 0x07FF];

    [Fact]
    public void Each_slice_of_a_volume_mip_decodes_from_its_own_blocks()
    {
        // The top mip has two slices, so the platform data holds two (TextureDerivedData.cpp:2923-2927).
        var texture = WithSlices(new Dxt5Volume(), 2);
        texture.PlatformData.Mips =
        [
            new FTexture2DMipMap(new InlineBulkData([.. SolidBlock(Colors[0]), .. SolidBlock(Colors[1])]), 2, 2, 2),
            new FTexture2DMipMap(new InlineBulkData(SolidBlock(Colors[2])), 1, 1, 1),
        ];

        AssertDecodes(texture.DecodeMip(0), 2, 4, [.. Texels(4, Colors[0]), .. Texels(4, Colors[1])]);
        AssertDecodes(texture.DecodeMip(1), 1, 1, Texels(1, Colors[2]));
    }

    [Fact]
    public void Each_face_of_a_cube_mip_decodes_from_its_own_block()
    {
        var texture = WithSlices(new Dxt5Cube(), 6);
        texture.PlatformData.Mips =
            [new FTexture2DMipMap(new InlineBulkData([.. Colors.SelectMany(SolidBlock)]), 2, 2, 1)];

        AssertDecodes(texture.DecodeMip(0), 2, 12, [.. Colors.SelectMany(color => Texels(4, color))]);
    }

    private static T WithSlices<T>(T texture, uint slices) where T : UTexture
    {
        typeof(FTexturePlatformData).GetField(nameof(FTexturePlatformData.PackedData))!.SetValue(texture.PlatformData, slices);
        return texture;
    }

    private static void AssertDecodes(CTexture? decoded, int width, int height, byte[] texels)
    {
        Assert.NotNull(decoded);
        Assert.Equal((width, height, EPixelFormat.PF_R8G8B8A8), (decoded.Width, decoded.Height, decoded.PixelFormat));
        Assert.Equal(texels, decoded.Data);
    }

    // A BC3 block of one opaque colour: both alpha endpoints 255, both colour endpoints the RGB565 value, all indices 0.
    private static byte[] SolidBlock(ushort rgb565)
    {
        var block = new byte[16];
        block[0] = block[1] = 255;
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(8), rgb565);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(10), rgb565);
        return block;
    }

    private static byte[] Texels(int count, ushort rgb565)
    {
        var (r, g, b) = (Expand(rgb565 >> 11, 5), Expand((rgb565 >> 5) & 0x3F, 6), Expand(rgb565 & 0x1F, 5));
        return [.. Enumerable.Range(0, count).SelectMany(_ => new[] { r, g, b, (byte)255 })];
    }

    private static byte Expand(int value, int bits) => (byte)((value << (8 - bits)) | (value >> (2 * bits - 8)));
}
