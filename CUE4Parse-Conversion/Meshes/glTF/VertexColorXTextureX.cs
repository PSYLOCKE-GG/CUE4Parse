using System;
using System.Collections.Generic;
using System.Numerics;
using CUE4Parse.UE4.Objects.Meshes;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Schema2;

namespace CUE4Parse_Conversion.Meshes.glTF
{
    public struct VertexColorXTextureX: IVertexMaterial, IEquatable<VertexColorXTextureX>
    {
        public int MaxColors => 1; // Do we need more?
        public int MaxTextCoords => Constants.MAX_MESH_UV_SETS;

        public Vector4 Color;

        public Vector2 TexCoord0;
        public Vector2 TexCoord1;
        public Vector2 TexCoord2;
        public Vector2 TexCoord3;
        public Vector2 TexCoord4;
        public Vector2 TexCoord5;
        public Vector2 TexCoord6;
        public Vector2 TexCoord7;

        public VertexColorXTextureX(Vector4 color, List<Vector2> texCoords)
        {
            Color = color;

            texCoords.Capacity = Math.Max(texCoords.Count, /*MaxTextCoords*/ 8);
            Resize(texCoords, texCoords.Capacity, new Vector2(0, 0));
            TexCoord0 = texCoords[0];
            TexCoord1 = texCoords[1];
            TexCoord2 = texCoords[2];
            TexCoord3 = texCoords[3];
            TexCoord4 = texCoords[4];
            TexCoord5 = texCoords[5];
            TexCoord6 = texCoords[6];
            TexCoord7 = texCoords[7];
        }

        /// <summary>
        /// Constructs from a primary UV and extra UV layers without allocating a List.
        /// </summary>
        public VertexColorXTextureX(Vector4 color, Vector2 primaryUv, FMeshUVFloat[][] extraUvs, uint vertIndex)
        {
            Color = color;
            TexCoord0 = primaryUv;
            TexCoord1 = extraUvs.Length > 0 ? (Vector2)extraUvs[0][vertIndex] : default;
            TexCoord2 = extraUvs.Length > 1 ? (Vector2)extraUvs[1][vertIndex] : default;
            TexCoord3 = extraUvs.Length > 2 ? (Vector2)extraUvs[2][vertIndex] : default;
            TexCoord4 = extraUvs.Length > 3 ? (Vector2)extraUvs[3][vertIndex] : default;
            TexCoord5 = extraUvs.Length > 4 ? (Vector2)extraUvs[4][vertIndex] : default;
            TexCoord6 = extraUvs.Length > 5 ? (Vector2)extraUvs[5][vertIndex] : default;
            TexCoord7 = extraUvs.Length > 6 ? (Vector2)extraUvs[6][vertIndex] : default;
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

        private static void Resize<T>(List<T> list, int size, T val)
        {
            if (size > list.Count)
                while (size - list.Count > 0)
                    list.Add(val);
            else if (size < list.Count)
                while (list.Count - size > 0)
                    list.RemoveAt(list.Count-1);
        }

        IEnumerable<KeyValuePair<string, SharpGLTF.Memory.AttributeFormat>> IVertexReflection.GetEncodingAttributes()
        {
            yield return new KeyValuePair<string, SharpGLTF.Memory.AttributeFormat>("COLOR_0", new SharpGLTF.Memory.AttributeFormat(DimensionType.VEC4, EncodingType.UNSIGNED_BYTE, true));
            yield return new KeyValuePair<string, SharpGLTF.Memory.AttributeFormat>("TEXCOORD_0", new SharpGLTF.Memory.AttributeFormat(DimensionType.VEC2));
            yield return new KeyValuePair<string, SharpGLTF.Memory.AttributeFormat>("TEXCOORD_1", new SharpGLTF.Memory.AttributeFormat(DimensionType.VEC2));
            yield return new KeyValuePair<string, SharpGLTF.Memory.AttributeFormat>("TEXCOORD_2", new SharpGLTF.Memory.AttributeFormat(DimensionType.VEC2));
            yield return new KeyValuePair<string, SharpGLTF.Memory.AttributeFormat>("TEXCOORD_3", new SharpGLTF.Memory.AttributeFormat(DimensionType.VEC2));
            yield return new KeyValuePair<string, SharpGLTF.Memory.AttributeFormat>("TEXCOORD_4", new SharpGLTF.Memory.AttributeFormat(DimensionType.VEC2));
            yield return new KeyValuePair<string, SharpGLTF.Memory.AttributeFormat>("TEXCOORD_5", new SharpGLTF.Memory.AttributeFormat(DimensionType.VEC2));
            yield return new KeyValuePair<string, SharpGLTF.Memory.AttributeFormat>("TEXCOORD_6", new SharpGLTF.Memory.AttributeFormat(DimensionType.VEC2));
            yield return new KeyValuePair<string, SharpGLTF.Memory.AttributeFormat>("TEXCOORD_7", new SharpGLTF.Memory.AttributeFormat(DimensionType.VEC2));
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
            return other.Color == Color &&
                other.TexCoord0 == TexCoord0 &&
                other.TexCoord1 == TexCoord1 &&
                other.TexCoord2 == TexCoord2 &&
                other.TexCoord3 == TexCoord3 &&
                other.TexCoord4 == TexCoord4 &&
                other.TexCoord5 == TexCoord5 &&
                other.TexCoord6 == TexCoord6 &&
                other.TexCoord7 == TexCoord7;
        }
    }
}
