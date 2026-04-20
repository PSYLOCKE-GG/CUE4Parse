using System;
using System.Collections.Generic;
using System.Numerics;
using CUE4Parse.UE4.Objects.Meshes;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Schema2;

namespace CUE4Parse_Conversion.Meshes.glTF
{
    /// <summary>
    /// Variable-UV-count material vertex. Holds up to <see cref="Constants.MAX_MESH_UV_SETS"/>
    /// UV slots on the stack, but only the first <see cref="MaxTextCoords"/> are emitted
    /// into the glTF accessors via <see cref="GetEncodingAttributes"/>. Callers pass the
    /// source mesh's <c>NumTexCoords</c> into the constructor so downstream meshes
    /// declare exactly the TEXCOORD_N attributes they actually populate.
    /// </summary>
    public struct VertexColorXTextureX: IVertexMaterial, IEquatable<VertexColorXTextureX>
    {
        public int MaxColors => 1; // Do we need more?
        public int MaxTextCoords => _numTexCoords;

        public Vector4 Color;

        public Vector2 TexCoord0;
        public Vector2 TexCoord1;
        public Vector2 TexCoord2;
        public Vector2 TexCoord3;
        public Vector2 TexCoord4;
        public Vector2 TexCoord5;
        public Vector2 TexCoord6;
        public Vector2 TexCoord7;

        private byte _numTexCoords;

        public VertexColorXTextureX(Vector4 color, List<Vector2> texCoords)
            : this(color, texCoords, ClampTexCoordCount(texCoords.Count))
        {
        }

        public VertexColorXTextureX(Vector4 color, List<Vector2> texCoords, int numTexCoords)
        {
            Color = color;
            _numTexCoords = ClampTexCoordCount(numTexCoords);

            // Read up to 8 UVs from the source list; any trailing slots within _numTexCoords
            // that the caller didn't supply, and all slots beyond _numTexCoords, stay at
            // zero (stack-default). No List mutation, no allocation.
            var count = texCoords.Count;
            TexCoord0 = count > 0 ? texCoords[0] : default;
            TexCoord1 = count > 1 ? texCoords[1] : default;
            TexCoord2 = count > 2 ? texCoords[2] : default;
            TexCoord3 = count > 3 ? texCoords[3] : default;
            TexCoord4 = count > 4 ? texCoords[4] : default;
            TexCoord5 = count > 5 ? texCoords[5] : default;
            TexCoord6 = count > 6 ? texCoords[6] : default;
            TexCoord7 = count > 7 ? texCoords[7] : default;
        }

        /// <summary>
        /// Constructs from a primary UV and extra UV layers without allocating a List.
        /// <paramref name="numTexCoords"/> is typically <c>extraUvs.Length + 1</c> (or
        /// the owning LOD's <c>NumTexCoords</c>) and controls how many TEXCOORD_
        /// attributes get emitted into the glTF output.
        /// </summary>
        public VertexColorXTextureX(Vector4 color, Vector2 primaryUv, FMeshUVFloat[][] extraUvs, uint vertIndex)
            : this(color, primaryUv, extraUvs, vertIndex, extraUvs.Length + 1)
        {
        }

        public VertexColorXTextureX(Vector4 color, Vector2 primaryUv, FMeshUVFloat[][] extraUvs, uint vertIndex, int numTexCoords)
        {
            Color = color;
            _numTexCoords = ClampTexCoordCount(numTexCoords);
            TexCoord0 = primaryUv;
            TexCoord1 = extraUvs.Length > 0 ? (Vector2)extraUvs[0][vertIndex] : default;
            TexCoord2 = extraUvs.Length > 1 ? (Vector2)extraUvs[1][vertIndex] : default;
            TexCoord3 = extraUvs.Length > 2 ? (Vector2)extraUvs[2][vertIndex] : default;
            TexCoord4 = extraUvs.Length > 3 ? (Vector2)extraUvs[3][vertIndex] : default;
            TexCoord5 = extraUvs.Length > 4 ? (Vector2)extraUvs[4][vertIndex] : default;
            TexCoord6 = extraUvs.Length > 5 ? (Vector2)extraUvs[5][vertIndex] : default;
            TexCoord7 = extraUvs.Length > 6 ? (Vector2)extraUvs[6][vertIndex] : default;
        }

        private static byte ClampTexCoordCount(int count)
        {
            if (count < 1) return 1;
            if (count > Constants.MAX_MESH_UV_SETS) return (byte)Constants.MAX_MESH_UV_SETS;
            return (byte)count;
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

        IEnumerable<KeyValuePair<string, SharpGLTF.Memory.AttributeFormat>> IVertexReflection.GetEncodingAttributes()
        {
            yield return new KeyValuePair<string, SharpGLTF.Memory.AttributeFormat>("COLOR_0", new SharpGLTF.Memory.AttributeFormat(DimensionType.VEC4, EncodingType.UNSIGNED_BYTE, true));

            // Emit exactly the UV slots the source mesh populates. MeshBuilder reads
            // attributes off the first vertex fragment it sees; every vertex in a mesh
            // is built with the same lod.NumTexCoords so the schema is uniform.
            var count = _numTexCoords == 0 ? 1 : _numTexCoords;
            for (var i = 0; i < count; i++)
                yield return new KeyValuePair<string, SharpGLTF.Memory.AttributeFormat>($"TEXCOORD_{i}", new SharpGLTF.Memory.AttributeFormat(DimensionType.VEC2));
        }

        public VertexMaterialDelta Subtract(IVertexMaterial baseValue)
        {
            var baseColor = baseValue.MaxColors > 0 ? baseValue.GetColor(0) : Vector4.Zero;
            var baseTC0 = baseValue.MaxTextCoords > 0 ? baseValue.GetTexCoord(0) : Vector2.Zero;
            var baseTC1 = baseValue.MaxTextCoords > 1 ? baseValue.GetTexCoord(1) : Vector2.Zero;
            var baseTC2 = baseValue.MaxTextCoords > 2 ? baseValue.GetTexCoord(2) : Vector2.Zero;
            var baseTC3 = baseValue.MaxTextCoords > 3 ? baseValue.GetTexCoord(3) : Vector2.Zero;

            return new VertexMaterialDelta(
                Color - baseColor, Vector4.Zero,
                TexCoord0 - baseTC0, TexCoord1 - baseTC1, TexCoord2 - baseTC2, TexCoord3 - baseTC3);
        }

        public void Add(in VertexMaterialDelta delta)
        {
            Color += delta.Color0Delta;
            TexCoord0 += delta.TexCoord0Delta;
            TexCoord1 += delta.TexCoord1Delta;
            TexCoord2 += delta.TexCoord2Delta;
            TexCoord3 += delta.TexCoord3Delta;
        }

        public bool Equals(VertexColorXTextureX other)
        {
            if (_numTexCoords != other._numTexCoords) return false;
            if (other.Color != Color) return false;

            // Only compare the UV slots that actually count. Slots beyond _numTexCoords
            // are unused padding — letting them compare unequal would defeat vertex
            // deduplication inside MeshBuilder.
            switch (_numTexCoords)
            {
                case 0:
                case 1: return other.TexCoord0 == TexCoord0;
                case 2: return other.TexCoord0 == TexCoord0 && other.TexCoord1 == TexCoord1;
                case 3: return other.TexCoord0 == TexCoord0 && other.TexCoord1 == TexCoord1 && other.TexCoord2 == TexCoord2;
                case 4: return other.TexCoord0 == TexCoord0 && other.TexCoord1 == TexCoord1 && other.TexCoord2 == TexCoord2 && other.TexCoord3 == TexCoord3;
                case 5: return other.TexCoord0 == TexCoord0 && other.TexCoord1 == TexCoord1 && other.TexCoord2 == TexCoord2 && other.TexCoord3 == TexCoord3 && other.TexCoord4 == TexCoord4;
                case 6: return other.TexCoord0 == TexCoord0 && other.TexCoord1 == TexCoord1 && other.TexCoord2 == TexCoord2 && other.TexCoord3 == TexCoord3 && other.TexCoord4 == TexCoord4 && other.TexCoord5 == TexCoord5;
                case 7: return other.TexCoord0 == TexCoord0 && other.TexCoord1 == TexCoord1 && other.TexCoord2 == TexCoord2 && other.TexCoord3 == TexCoord3 && other.TexCoord4 == TexCoord4 && other.TexCoord5 == TexCoord5 && other.TexCoord6 == TexCoord6;
                default: return other.TexCoord0 == TexCoord0 && other.TexCoord1 == TexCoord1 && other.TexCoord2 == TexCoord2 && other.TexCoord3 == TexCoord3 && other.TexCoord4 == TexCoord4 && other.TexCoord5 == TexCoord5 && other.TexCoord6 == TexCoord6 && other.TexCoord7 == TexCoord7;
            }
        }
    }
}
