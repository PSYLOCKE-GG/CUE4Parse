using System.Numerics;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Writers.Gltf;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Meshes;
using CUE4Parse.UE4.Objects.RenderCore;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.Tests;

// UE keeps the bitangent sign in TangentZ.W and writes TangentX.W as the maximum
// (StaticMeshVertexBuffer.h:39-43, PackedNormal.h:122 and :366). The glTF tangent
// carries the sign negated for the Y/Z swap (Gltf.SwapYZAndNormalize).
public class PackedTangentSignTests
{
    // Biased SNORM8 lanes, as FPackedNormal holds them after its XOR: X = +1, Y = Z = 0.
    private const uint TangentX = 0xFF_80_80_FF;
    private const uint TangentZPositive = 0xFF_FF_80_80;
    private const uint TangentZNegative = 0x01_FF_80_80;

    [Theory]
    [InlineData(TangentZNegative, 1f)]
    [InlineData(TangentZPositive, -1f)]
    public void A_skinned_vertex_exports_the_sign_of_tangent_z(uint tangentZ, float gltfW)
    {
        var item = new FStaticMeshUVItem([new FPackedNormal(TangentX), new FPackedNormal(0), new FPackedNormal(tangentZ)],
            [new FMeshUVFloat(0, 0)]);
        var vertex = new SkinnedMeshVertex(new FGPUVertFloat(FVector.ZeroVector, null, item), []);

        Assert.Equal(gltfW, Gltf.SwapYZAndNormalize((Vector4) vertex.Tangent).W);
    }

    [Theory]
    [InlineData(TangentZNegative, 1f)]
    [InlineData(TangentZPositive, -1f)]
    public void A_static_vertex_exports_the_sign_of_tangent_z(uint tangentZ, float gltfW)
    {
        var vertex = new MeshVertex(FVector.ZeroVector, new FPackedNormal(tangentZ), new FPackedNormal(TangentX),
            new FMeshUVFloat(0, 0));

        Assert.Equal(gltfW, Gltf.SwapYZAndNormalize((Vector4) vertex.Tangent).W);
    }

    [Theory]
    [InlineData(short.MinValue + 1, 1f)]
    [InlineData(short.MaxValue, -1f)]
    public void A_high_precision_tangent_z_keeps_its_sign(short tangentZW, float gltfW)
    {
        // Two FPackedRGBA16N in SNORM16, as cooked: TangentX (+1, 0, 0, max) and TangentZ (0, 0, 1, w).
        short[] lanes = [short.MaxValue, 0, 0, short.MaxValue, 0, 0, short.MaxValue, tangentZW];
        var bytes = lanes.SelectMany(BitConverter.GetBytes).ToArray();
        using var archive = new FByteArchive("tangents", bytes, new VersionContainer(EGame.GAME_UE5_3));
        var item = new FStaticMeshUVItem(FStaticMeshUVItem.SerializeTangents(archive, true), [new FMeshUVFloat(0, 0)]);
        var vertex = new SkinnedMeshVertex(new FGPUVertFloat(FVector.ZeroVector, null, item), []);

        Assert.Equal(gltfW, Gltf.SwapYZAndNormalize((Vector4) vertex.Tangent).W);
    }
}
