using System.Numerics;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;

namespace CUE4Parse_Conversion.Writers.Gltf;

/// <summary>
/// Variable-UV-count material vertex. Holds up to <see cref="Constants.MAX_MESH_UV_SETS"/>
/// UV slots on the stack, but only the first <see cref="MaxTextCoords"/> are emitted
/// into the glTF accessors via <see cref="GetEncodingAttributes"/>. Callers pass the
/// source LOD's UV count into the constructor so downstream meshes declare exactly the
/// TEXCOORD_N attributes they actually populate.
/// </summary>
/// <remarks>
/// SharpGLTF reads the values of TEXCOORD_0..3 through <see cref="GetTexCoord"/>, but it reads
/// every other attribute through <see cref="IVertexCustom"/> and writes zeros when the vertex
/// does not supply it. So this type serves TEXCOORD_4 and up as custom attributes.
/// </remarks>
public struct VertexColorXTextureX : IVertexMaterial, IVertexCustom, IEquatable<VertexColorXTextureX>
{
    private const string TexCoordPrefix = "TEXCOORD_";
    private const int FirstCustomTexCoord = 4;

    // Preserves vertex color masks without affecting the material's color or opacity
    // Custom attributes must start with an underscore!!!
    private const string ShaderMaskAttribute = "_UE_COLOR_0";
    private readonly bool _shaderMask;

    internal uint? SourceIndex { readonly get; set; }

    public readonly int MaxColors => _shaderMask ? 0 : 1;
    public readonly int MaxTextCoords => _numTexCoords;

    public Vector4 Color;

    public Vector2 TexCoord0;
    public Vector2 TexCoord1;
    public Vector2 TexCoord2;
    public Vector2 TexCoord3;
    public Vector2 TexCoord4;
    public Vector2 TexCoord5;
    public Vector2 TexCoord6;
    public Vector2 TexCoord7;

    private readonly byte _numTexCoords;

    public VertexColorXTextureX(Vector2[] texCoords, Vector4? color = null, bool shaderMask = false) : this(texCoords, texCoords.Length, color, shaderMask)
    {
    }

    /// <param name="numTexCoords">
    /// How many UV sets the source LOD populates. Slots past this count stay at zero and are
    /// never written to the glTF output.
    /// </param>
    public VertexColorXTextureX(Vector2[] texCoords, int numTexCoords, Vector4? color = null, bool shaderMask = false)
    {
        _shaderMask = shaderMask;
        // A vertex without colours must not multiply the base colour away, so default to white.
        Color = color ?? Vector4.One;
        _numTexCoords = ClampTexCoordCount(numTexCoords);
        TexCoord0 = texCoords.Length > 0 ? texCoords[0] : Vector2.Zero;
        TexCoord1 = texCoords.Length > 1 ? texCoords[1] : Vector2.Zero;
        TexCoord2 = texCoords.Length > 2 ? texCoords[2] : Vector2.Zero;
        TexCoord3 = texCoords.Length > 3 ? texCoords[3] : Vector2.Zero;
        TexCoord4 = texCoords.Length > 4 ? texCoords[4] : Vector2.Zero;
        TexCoord5 = texCoords.Length > 5 ? texCoords[5] : Vector2.Zero;
        TexCoord6 = texCoords.Length > 6 ? texCoords[6] : Vector2.Zero;
        TexCoord7 = texCoords.Length > 7 ? texCoords[7] : Vector2.Zero;
    }

    private static byte ClampTexCoordCount(int count)
    {
        if (count < 1) return 1;
        if (count > Constants.MAX_MESH_UV_SETS) return Constants.MAX_MESH_UV_SETS;
        return (byte) count;
    }

    void IVertexMaterial.SetColor(int setIndex, Vector4 color)
    {
        Color = color;
    }

    void IVertexMaterial.SetTexCoord(int setIndex, Vector2 coord)
    {
        switch (setIndex)
        {
            case 0: TexCoord0 = coord; break;
            case 1: TexCoord1 = coord; break;
            case 2: TexCoord2 = coord; break;
            case 3: TexCoord3 = coord; break;
            case 4: TexCoord4 = coord; break;
            case 5: TexCoord5 = coord; break;
            case 6: TexCoord6 = coord; break;
            case 7: TexCoord7 = coord; break;
        }
    }

    public void Add(in VertexMaterialDelta delta)
    {
        Color += delta.GetColor(0);
        TexCoord0 += delta.GetTexCoord(0);
        TexCoord1 += delta.GetTexCoord(1);
        TexCoord2 += delta.GetTexCoord(2);
        TexCoord3 += delta.GetTexCoord(3);
    }

    public readonly IEnumerable<string> CustomAttributes
    {
        get
        {
            if (_shaderMask) yield return ShaderMaskAttribute;
            for (var i = FirstCustomTexCoord; i < _numTexCoords; i++)
                yield return TexCoordPrefix + i;
        }
    }

    public readonly bool TryGetCustomAttribute(string attributeName, out object value)
    {
        if (_shaderMask && attributeName == ShaderMaskAttribute)
        {
            value = Color;
            return true;
        }

        if (TryParseCustomTexCoord(attributeName, out var index) && index < _numTexCoords)
        {
            value = GetTexCoord(index);
            return true;
        }

        value = null!;
        return false;
    }

    public void SetCustomAttribute(string attributeName, object value)
    {
        if (_shaderMask && attributeName == ShaderMaskAttribute && value is Vector4 color)
            Color = color;
        else if (TryParseCustomTexCoord(attributeName, out var index) && value is Vector2 coord)
            ((IVertexMaterial) this).SetTexCoord(index, coord);
    }

    // The cooked UVs pass through unchanged. A cooked channel can hold NaN, and the export keeps it.
    public readonly void Validate() { }

    private static bool TryParseCustomTexCoord(string attributeName, out int index)
    {
        index = -1;
        return attributeName.StartsWith(TexCoordPrefix, StringComparison.Ordinal)
            && int.TryParse(attributeName.AsSpan(TexCoordPrefix.Length), out index)
            && index >= FirstCustomTexCoord && index < Constants.MAX_MESH_UV_SETS;
    }

    public readonly VertexMaterialDelta Subtract(IVertexMaterial baseValue)
    {
        return new VertexMaterialDelta(this).Subtract(new VertexMaterialDelta(baseValue));
    }

    public readonly IEnumerable<KeyValuePair<string, AttributeFormat>> GetEncodingAttributes()
    {
        // glTF COLOR_0 multiplies base color and opacity, shader masks use a custom attribute
        var colorAttribute = _shaderMask ? ShaderMaskAttribute : "COLOR_0";
        yield return new(colorAttribute, new AttributeFormat(DimensionType.VEC4, EncodingType.UNSIGNED_BYTE, true));

        // Emit exactly the UV slots the source LOD populates. MeshBuilder reads attributes off
        // the first vertex fragment it sees; every vertex in a mesh is built with the same UV
        // count, so the schema stays uniform.
        var count = _numTexCoords == 0 ? 1 : _numTexCoords;
        for (var i = 0; i < count; i++)
            yield return new($"TEXCOORD_{i}", new AttributeFormat(DimensionType.VEC2));
    }

    public readonly Vector2 GetTexCoord(int index)
    {
        return index switch
        {
            0 => TexCoord0,
            1 => TexCoord1,
            2 => TexCoord2,
            3 => TexCoord3,
            4 => TexCoord4,
            5 => TexCoord5,
            6 => TexCoord6,
            7 => TexCoord7,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    public readonly Vector4 GetColor(int index)
        => index != 0 ? throw new ArgumentOutOfRangeException(nameof(index)) : Color;

    public readonly bool Equals(VertexColorXTextureX other)
    {
        if (other.SourceIndex != SourceIndex || other._shaderMask != _shaderMask) return false;
        if (_numTexCoords != other._numTexCoords) return false;
        if (other.Color != Color) return false;

        // Only compare the UV slots that count. Slots beyond _numTexCoords are unused padding —
        // letting them compare unequal would defeat vertex deduplication inside MeshBuilder.
        for (var i = 0; i < _numTexCoords; i++)
        {
            if (GetTexCoord(i) != other.GetTexCoord(i)) return false;
        }

        return true;
    }
}
