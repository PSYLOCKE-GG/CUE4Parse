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
public struct VertexColorXTextureX : IVertexMaterial, IEquatable<VertexColorXTextureX>
{
    public int MaxColors => 1; // Do we need more?
    public int MaxTextCoords => _numTexCoords;

    public Vector4 Color;

    // public List<Vector2> TexCoords;
    public Vector2 TexCoord0;
    public Vector2 TexCoord1;
    public Vector2 TexCoord2;
    public Vector2 TexCoord3;
    public Vector2 TexCoord4;
    public Vector2 TexCoord5;
    public Vector2 TexCoord6;
    public Vector2 TexCoord7;

    private byte _numTexCoords;

    public VertexColorXTextureX(Vector2[] texCoords, Vector4? color = null) : this(texCoords, texCoords.Length, color)
    {
    }

    /// <param name="numTexCoords">
    /// How many UV sets the source LOD populates. Slots past this count stay at zero and are
    /// never written to the glTF output.
    /// </param>
    public VertexColorXTextureX(Vector2[] texCoords, int numTexCoords, Vector4? color = null)
    {
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

    public VertexMaterialDelta Subtract(IVertexMaterial baseValue)
    {
        return new VertexMaterialDelta(this).Subtract(new VertexMaterialDelta(baseValue));
    }

    public IEnumerable<KeyValuePair<string, AttributeFormat>> GetEncodingAttributes()
    {
        yield return new KeyValuePair<string, AttributeFormat>("COLOR_0", new AttributeFormat(DimensionType.VEC4, EncodingType.UNSIGNED_BYTE, true));

        // Emit exactly the UV slots the source LOD populates. MeshBuilder reads attributes off
        // the first vertex fragment it sees; every vertex in a mesh is built with the same UV
        // count, so the schema stays uniform.
        var count = _numTexCoords == 0 ? 1 : _numTexCoords;
        for (var i = 0; i < count; i++)
            yield return new KeyValuePair<string, AttributeFormat>($"TEXCOORD_{i}", new AttributeFormat(DimensionType.VEC2));
    }

    public Vector2 GetTexCoord(int index)
    {
        switch (index)
        {
            case 0: return TexCoord0;
            case 1: return TexCoord1;
            case 2: return TexCoord2;
            case 3: return TexCoord3;
            case 4: return TexCoord4;
            case 5: return TexCoord5;
            case 6: return TexCoord6;
            case 7: return TexCoord7;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public Vector4 GetColor(int index)
    {
        if (index != 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        return Color;
    }

    private static void Resize<T>(List<T> list, int size, T val)
    {
        if (size > list.Count)
            while (size - list.Count > 0)
                list.Add(val);
        else if (size < list.Count)
            while (list.Count - size > 0)
                list.RemoveAt(list.Count-1);
    }

    public bool Equals(VertexColorXTextureX other)
    {
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
