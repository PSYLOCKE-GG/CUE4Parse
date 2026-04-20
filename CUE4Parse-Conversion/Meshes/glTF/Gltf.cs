using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CUE4Parse_Conversion.Materials;
using CUE4Parse_Conversion.Meshes.PSK;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Meshes;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Writers;
using CUE4Parse.Utils;
using System.Text.Json.Nodes;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace CUE4Parse_Conversion.Meshes.glTF
{
    using VERTEX = VertexPositionNormalTangent;
    public class Gltf
    {
        public readonly ModelRoot Model;

        public Gltf(string name, CStaticMeshLod lod, List<MaterialExporter2>? materialExports, ExporterOptions options)
        {
            var mesh = new MeshBuilder<VERTEX, VertexColorXTextureX, VertexEmpty>(name);

            for (var i = 0; i < lod.Sections.Value.Length; i++)
            {
                ExportStaticMeshSections(i, lod, lod.Sections.Value[i], materialExports, mesh, options);
            }

            var sceneBuilder = new SceneBuilder();
            sceneBuilder.AddRigidMesh(mesh, Matrix4x4.Identity);
            Model = sceneBuilder.ToGltf2();
        }

        public Gltf(string name, CSkelMeshLod lod, List<CSkelMeshBone> bones, List<MaterialExporter2>? materialExports, ExporterOptions options, FPackageIndex[]? morphTargets = null, int lodIndex = -1)
            : this(name, lod, bones, materialExports, options, null, morphTargets, lodIndex) { }

        public Gltf(string name, CSkelMeshLod lod, List<CSkelMeshBone> bones, List<MaterialExporter2>? materialExports, ExporterOptions options, Action<NodeBuilder[]>? onSkeletonReady, FPackageIndex[]? morphTargets = null, int lodIndex = -1)
        {
            var mesh = new MeshBuilder<VERTEX, VertexColorXTextureX, VertexJoints4>(name);

            for (var i = 0; i < lod.Sections.Value.Length; i++)
            {
                ExportSkelMeshSections(i, lod, lod.Sections.Value[i], materialExports, mesh, options);
            }

            if (morphTargets != null)
            {
                var targetNames = "{\"targetNames\": [";
                for (var i = 0; i < morphTargets.Length; i++)
                {
                    var morphTarget = morphTargets[i].Load<UMorphTarget>();
                    if (morphTarget == null || morphTarget.MorphLODModels == null || morphTarget.MorphLODModels.Length < lodIndex || lodIndex == -1)
                        continue;
                    var morphBuilder = mesh.UseMorphTarget(i);
                    var morphModel = morphTarget.MorphLODModels[lodIndex];

                    targetNames += $"\"{morphTarget.Name}\"";
                    targetNames += i != morphTargets.Length-1 ? "," : "";

                    var verts = morphBuilder.Vertices.ToArray();
                    for (int j = 0; j < morphModel.Vertices.Length; j++) // morphModel.NumBaseMeshVerts can be different from verts.Length
                    {
                        var delta = morphModel.Vertices[j];
                        var vert = lod.Verts[delta.SourceIdx];
                        var srcVert = new VertexPositionNormalTangent(SwapYZ(vert.Position*0.01f),SwapYZAndNormalize((FVector)vert.Normal) , SwapYZAndNormalize((Vector4)vert.Tangent));
                        var index = FindVert(srcVert, verts);
                        if (index == -1)  continue;

                        morphBuilder.SetVertexDelta(morphBuilder.Vertices.ElementAt(index), new VertexGeometryDelta(SwapYZ(delta.PositionDelta*0.01f), Vector3.Zero, SwapYZAndNormalize(delta.TangentZDelta)));
                    }
                }

                targetNames += "]}";
                mesh.Extras = JsonNode.Parse(targetNames);
            }

            var sceneBuilder = new SceneBuilder();
            var armatureNodeBuilder = new NodeBuilder(name+".ao");

            var armature = CreateGltfSkeleton(bones, armatureNodeBuilder);
            onSkeletonReady?.Invoke(armature);
            sceneBuilder.AddSkinnedMesh(mesh, Matrix4x4.Identity, armature);

            Model = sceneBuilder.ToGltf2();
        }

        private static int FindVert(VertexPositionNormalTangent a, VertexPositionNormalTangent[] b)
        {
            for (int i = 0; i < b.Length; i++)
            {
                if (b[i].GetPosition() == a.GetPosition()) // not a good idea but i don't see any other way
                    return i;
            }
            return -1;
        }

        public ArraySegment<byte> SaveAsWavefront()
        {
            throw new NotImplementedException();
        }

        public void Save(EMeshFormat meshFormat, FArchiveWriter Ar)
        {
            switch (meshFormat)
            {
                case EMeshFormat.Gltf2:
                    Ar.Write(Model.WriteGLB());
                    break;
                case EMeshFormat.OBJ:
                    Ar.Write(SaveAsWavefront()); // this can be supported after new release of SharpGltf
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(meshFormat), meshFormat, null);
            }
        }

        public static NodeBuilder[] CreateGltfSkeleton(List<CSkelMeshBone> skeleton, NodeBuilder armatureNode) // TODO optimize
        {
            var result = new List<NodeBuilder>();

            for (var i = 0; i < skeleton.Count; i++)
            {
                var root = skeleton[i];
                if (root.ParentIndex != -1) continue;

                var rootCopy = (CSkelMeshBone)root.Clone(); // we don't want to modify the original skeleton
                // rootCopy.Orientation = FQuat.Conjugate(root.Orientation);
                result.AddRange(CreateBonesRecursive(rootCopy, armatureNode, skeleton, i));
            }

            return result.ToArray();
        }

        private static List<NodeBuilder> CreateBonesRecursive(CSkelMeshBone bone, NodeBuilder parent, List<CSkelMeshBone> skeleton, int index)
        {
            var res = new List<NodeBuilder>();

            var bonePos = SwapYZ(bone.Position*0.01f);
            var boneRot = SwapYZ(bone.Orientation);
            var node = parent.CreateNode(bone.Name.ToString())
                .WithLocalRotation(boneRot.ToQuaternion())
                .WithLocalTranslation(bonePos);

            res.Add(node);

            var numBones = skeleton.Count;
            for (int j = 0; j < numBones; j++)
            {
                if (index == j) continue;
                var bone2 = skeleton[j];
                if (bone2.ParentIndex == index)
                {
                    res.AddRange(CreateBonesRecursive(bone2, node, skeleton, j));
                }
            }
            return res;
        }

        public static void ExportSkelMeshSections(int index, CSkelMeshLod lod, CMeshSection sect, List<MaterialExporter2>? materialExports, MeshBuilder<VERTEX, VertexColorXTextureX, VertexJoints4> mesh, ExporterOptions options)
        {
            string materialName;
            if (sect.Material?.Load<UMaterialInterface>() is { } tex)
            {
                materialName = tex.Name;
                var materialExporter = new MaterialExporter2(tex, options);
                materialExports?.Add(materialExporter);
            }
            else materialName = sect.MaterialName ?? $"material_{index}";

            var mat = new MaterialBuilder().WithBaseColor(Vector4.One);
            mat.Name = materialName;

            var prim = mesh.UsePrimitive(mat);
            var indices = lod.Indices.Value;
            var verts = lod.Verts;
            var extraUvs = lod.ExtraUV.Value;
            var vertColors = lod.VertexColors;
            var firstIndex = sect.FirstIndex;
            var numTexCoords = lod.NumTexCoords > 0 ? lod.NumTexCoords : extraUvs.Length + 1;

            for (int j = 0; j < sect.NumFaces; j++)
            {
                var baseIdx = firstIndex + j * 3;
                var idx0 = indices[baseIdx];
                var idx1 = indices[baseIdx + 1];
                var idx2 = indices[baseIdx + 2];

                var vert1 = verts[idx0];
                var vert2 = verts[idx1];
                var vert3 = verts[idx2];

                var (v1, v2, v3) = PrepareTris(vert1, vert2, vert3);

                var col1 = vertColors is not null ? vertColors[idx0] : Vector4.One;
                var col2 = vertColors is not null ? vertColors[idx1] : Vector4.One;
                var col3 = vertColors is not null ? vertColors[idx2] : Vector4.One;

                var c1 = new VertexColorXTextureX(col1, (Vector2)vert1.UV, extraUvs, idx0, numTexCoords);
                var c2 = new VertexColorXTextureX(col2, (Vector2)vert2.UV, extraUvs, idx1, numTexCoords);
                var c3 = new VertexColorXTextureX(col3, (Vector2)vert3.UV, extraUvs, idx2, numTexCoords);

                var jv1 = PrepareVertexJoint(vert1);
                var jv2 = PrepareVertexJoint(vert2);
                var jv3 = PrepareVertexJoint(vert3);

                prim.AddTriangle((v1, c1, jv1), (v2, c2, jv2), (v3, c3, jv3));
            }
        }

        public static void ExportStaticMeshSections(int index, CStaticMeshLod lod, CMeshSection sect, List<MaterialExporter2>? materialExports, MeshBuilder<VERTEX, VertexColorXTextureX, VertexEmpty> mesh, ExporterOptions options)
        {
            string materialName;
            if (sect.Material?.Load<UMaterialInterface>() is { } tex)
            {
                materialName = tex.Name;
                var materialExporter = new MaterialExporter2(tex, options);
                materialExports?.Add(materialExporter);
            }
            else materialName = sect.MaterialName ?? $"material_{index}";

            var mat = new MaterialBuilder().WithBaseColor(Vector4.One);
            mat.Name = materialName;

            var prim = mesh.UsePrimitive(mat);
            var indices = lod.Indices.Value;
            var verts = lod.Verts;
            var extraUvs = lod.ExtraUV.Value;
            var vertColors = lod.VertexColors;
            var firstIndex = sect.FirstIndex;
            var numTexCoords = lod.NumTexCoords > 0 ? lod.NumTexCoords : extraUvs.Length + 1;

            for (int j = 0; j < sect.NumFaces; j++)
            {
                var baseIdx = firstIndex + j * 3;
                var idx0 = indices[baseIdx];
                var idx1 = indices[baseIdx + 1];
                var idx2 = indices[baseIdx + 2];

                var vert1 = verts[idx0];
                var vert2 = verts[idx1];
                var vert3 = verts[idx2];

                var (v1, v2, v3) = PrepareTris(vert1, vert2, vert3);

                var col1 = vertColors is not null ? vertColors[idx0] : Vector4.One;
                var col2 = vertColors is not null ? vertColors[idx1] : Vector4.One;
                var col3 = vertColors is not null ? vertColors[idx2] : Vector4.One;

                var c1 = new VertexColorXTextureX(col1, (Vector2)vert1.UV, extraUvs, idx0, numTexCoords);
                var c2 = new VertexColorXTextureX(col2, (Vector2)vert2.UV, extraUvs, idx1, numTexCoords);
                var c3 = new VertexColorXTextureX(col3, (Vector2)vert3.UV, extraUvs, idx2, numTexCoords);

                prim.AddTriangle((v1, c1), (v2, c2), (v3, c3));
            }
        }

        public static VertexJoints4 PrepareVertexJoint(CSkelMeshVertex vert)
        {
            var influences = vert.Influences;
            var bindings = new (int, float)[influences.Count];
            for (var i = 0; i < influences.Count; i++)
            {
                var inf = influences[i];
                bindings[i] = (inf.Bone, inf.Weight);
            }
            return new VertexJoints4(bindings);
        }

        public static (VertexColorXTextureX, VertexColorXTextureX, VertexColorXTextureX) PrepareUVsAndTexCoords(
            CBaseMeshLod lod, CMeshVertex vert1, CMeshVertex vert2, CMeshVertex vert3, uint[] indices)
        {
            return PrepareUVsAndTexCoords(lod.VertexColors, vert1, vert2, vert3, lod.ExtraUV.Value, indices);
        }

        public static (VertexColorXTextureX, VertexColorXTextureX, VertexColorXTextureX) PrepareUVsAndTexCoords(
            FColor[]? colors, CMeshVertex vert1, CMeshVertex vert2, CMeshVertex vert3, FMeshUVFloat[][] uvs, uint[] indices)
        {
            var (uvs1, uvs2, uvs3) = PrepareUVs(vert1, vert2, vert3, uvs, indices);
            var col1 = colors is not null ? colors[indices[0]] : Vector4.One;
            var col2 = colors is not null ? colors[indices[1]] : Vector4.One;
            var col3 = colors is not null ? colors[indices[2]] : Vector4.One;
            var numTexCoords = uvs.Length + 1;
            var c1 = new VertexColorXTextureX(col1, uvs1, numTexCoords);
            var c2 = new VertexColorXTextureX(col2, uvs2, numTexCoords);
            var c3 = new VertexColorXTextureX(col3, uvs3, numTexCoords);
            return (c1, c2, c3);
        }

        private static (List<Vector2>, List<Vector2>, List<Vector2>) PrepareUVs(CMeshVertex vert1, CMeshVertex vert2, CMeshVertex vert3, FMeshUVFloat[][] uvs, uint[] indices)
        {
            var uvs1 = new List<Vector2>() { (Vector2)vert1.UV };
            var uvs2 = new List<Vector2>() { (Vector2)vert2.UV };
            var uvs3 = new List<Vector2>() { (Vector2)vert3.UV };
            foreach (var uv in uvs)
            {
                uvs1.Add((Vector2)uv[indices[0]]);
                uvs2.Add((Vector2)uv[indices[1]]);
                uvs3.Add((Vector2)uv[indices[2]]);
            }

            return (uvs1, uvs2, uvs3);
        }

        private static (VERTEX, VERTEX, VERTEX) PrepareTris(CMeshVertex vert1, CMeshVertex vert2, CMeshVertex vert3)
        {
            var v1 = new VertexPositionNormalTangent(SwapYZ(vert1.Position*0.01f),SwapYZAndNormalize((FVector)vert1.Normal) , SwapYZAndNormalize((Vector4)vert1.Tangent));
            var v2 = new VertexPositionNormalTangent(SwapYZ(vert2.Position*0.01f), SwapYZAndNormalize((FVector)vert2.Normal), SwapYZAndNormalize((Vector4)vert2.Tangent));
            var v3 = new VertexPositionNormalTangent(SwapYZ(vert3.Position*0.01f), SwapYZAndNormalize((FVector)vert3.Normal), SwapYZAndNormalize((Vector4)vert3.Tangent));

            return (v1, v2, v3);
        }

        public static FVector SwapYZAndNormalize(FVector vec)
        {
            var res = SwapYZ(vec);
            res.Normalize();
            return res;
        }

        public static FVector SwapYZ(FVector vec)
        {
            var res = new FVector(vec.X, vec.Z, vec.Y);
            return res;
        }

        public static FQuat SwapYZ(FQuat quat) => new (quat.X, quat.Z, quat.Y, -quat.W);

        public static Vector4 SwapYZAndNormalize(Vector4 vec)
        {
            // Normalize XYZ only — W is the binormal sign (±1) and must not be included
            // in the normalization. The Y↔Z swap is a reflection (det=-1) that flips
            // handedness, so negate W to keep the tangent frame consistent.
            var xyz = Vector3.Normalize(new Vector3(vec.X, vec.Z, vec.Y));
            return new Vector4(xyz.X, xyz.Y, xyz.Z, -vec.W);
        }
    }
}
