using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anatawa12.Modifier4Avatar.Editor
{
    public static class AddMangaExpressionBlendShapeGenerator
    {
        public static void Generate(
            Mesh mesh,
            SkinnedMeshRenderer renderer,
            AddMangaExpressionBlendShape config
        )
        {
            var originalData = new MeshData(mesh);
            var additionalMeshes = new MeshData[config.addMeshes.Length];
            var mergeMeshes = new List<MeshData>[mesh.subMeshCount];
            for (var i = 0; i < mergeMeshes.Length; i++)
                mergeMeshes[i] = new List<MeshData>();

            var vertexIndexOffset = originalData.vertices.Length;
            for (var i = 0; i < config.addMeshes.Length; i++)
            {
                var addMesh = config.addMeshes[i];
                var data = GenerateAdditionalMeshData(originalData, renderer, addMesh, vertexIndexOffset);
                additionalMeshes[i] = data;
                mergeMeshes[addMesh.materialIndex].Add(data);
                vertexIndexOffset += data.vertices.Length;
            }

            var totalCount = originalData.vertices.Length + additionalMeshes.Sum(x => x.vertices.Length);

            // merge meshes
            mesh.vertices = ExtendAttribute(originalData, totalCount, additionalMeshes, x => x.vertices);
            mesh.normals = ExtendAttribute(originalData, totalCount, additionalMeshes, x => x.normals);
            mesh.tangents = ExtendAttribute(originalData, totalCount, additionalMeshes, x => x.tangents);

            #region uv

            for (var index = 0; index < 7; index++)
            {
                if (originalData.uvStatus[index] == MeshData.TexCoordStatus.NotDefined) continue;
                // ReSharper disable once AccessToModifiedClosure
                var extended = ExtendAttribute(originalData, totalCount, additionalMeshes, x => x.uvs[index]);
                switch (originalData.uvStatus[index])
                {
                    case MeshData.TexCoordStatus.NotDefined:
                        break;
                    case MeshData.TexCoordStatus.Vector2:
                        mesh.SetUVs(index, extended.Select(x => (Vector2)x).ToList());
                        break;
                    case MeshData.TexCoordStatus.Vector3:
                        mesh.SetUVs(index, extended.Select(x => (Vector3)x).ToList());
                        break;
                    case MeshData.TexCoordStatus.Vector4:
                        mesh.SetUVs(index, extended.ToList());
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            #endregion

            mesh.colors32 = ExtendAttribute(originalData, totalCount, additionalMeshes, x => x.colors);
            var boneWeightsPerVertex =
                ExtendAttribute(originalData, totalCount, additionalMeshes, x => x.boneWeightsPerVertex);
            var boneWeights = originalData.boneWeights.Concat(additionalMeshes.SelectMany(x => x.boneWeights))
                .ToArray();
            using (var boneWeightsPerVertexNative = new NativeArray<byte>(boneWeightsPerVertex, Allocator.Temp))
            using (var boneWeightsNative = new NativeArray<BoneWeight1>(boneWeights, Allocator.Temp))
                mesh.SetBoneWeights(boneWeightsPerVertexNative, boneWeightsNative);

            #region BlendShape

            // nothing to do with original blendShapes

            // TODO: add additional blend shapes
            var addingShapeVertices = new Vector3[totalCount];
            var addingShapeNormals = new Vector3[totalCount];
            var addingShapeTangents = new Vector3[totalCount];

            foreach (var configCombineBlendShape in config.combineBlendShapes)
            {
                var shapeIndex = -1;
                for (var i = 0; i < originalData.blendShapes.Length; i++)
                {
                    if (originalData.blendShapes[i].name == configCombineBlendShape.name &&
                        originalData.blendShapes[i].weight == 100)
                    {
                        shapeIndex = i;
                        break;
                    }
                }
                if (shapeIndex == -1) throw new ArgumentException($"original blend shape {configCombineBlendShape.name} not found", nameof(config));

                var weight = configCombineBlendShape.weight / 100;
                var shapeDeltaPosition = originalData.blendShapeVertices[shapeIndex];
                var shapeDeltaNormal = originalData.blendShapeNormals[shapeIndex];
                var shapeDeltaTangent = originalData.blendShapeTangents[shapeIndex];

                for (var i = 0; i < shapeDeltaPosition.Length; i++)
                {
                    addingShapeVertices[i] += shapeDeltaPosition[i] * weight;
                    addingShapeNormals[i] += shapeDeltaNormal[i] * weight;
                    addingShapeTangents[i] += shapeDeltaTangent[i] * weight;
                }
            }

            var offset = originalData.vertices.Length;
            foreach (var meshData in additionalMeshes)
            {
                var vertices = meshData.blendShapeVertices[0];
                var normals = meshData.blendShapeNormals[0];
                var tangents = meshData.blendShapeTangents[0];
                vertices.AsSpan().CopyTo(addingShapeVertices.AsSpan().Slice(offset, vertices.Length));
                normals.AsSpan().CopyTo(addingShapeNormals.AsSpan().Slice(offset, normals.Length));
                tangents.AsSpan().CopyTo(addingShapeTangents.AsSpan().Slice(offset, tangents.Length));
                offset += vertices.Length;
            }

            mesh.AddBlendShapeFrame(
                config.newBlendShapeName,
                100,
                addingShapeVertices,
                addingShapeNormals,
                addingShapeTangents);

            #endregion

            #region SubMeshes

            // first, clear all submeshes
            mesh.subMeshCount = 0;
            mesh.triangles = Array.Empty<int>();
            mesh.subMeshCount = originalData.subMeshes.Length;

            // then, add all submeshes
            for (var i = 0; i < originalData.subMeshes.Length; i++)
            {
                var subMesh = originalData.subMeshes[i];
                var toMerge = mergeMeshes[i];
                if (toMerge.Count == 0)
                {
                    // just copy them
                    mesh.SetIndices(
                        originalData.triangles.Skip(subMesh.indexStart).Take(subMesh.indexCount).ToArray(),
                        subMesh.topology,
                        i,
                        true,
                        subMesh.baseVertex);
                }
                else
                {
                    // join and merge them
                    var triangles = new List<int>(subMesh.indexCount + toMerge.Sum(x => x.triangles.Length));
                    triangles.AddRange(originalData.triangles.Skip(subMesh.indexStart).Take(subMesh.indexCount)
                        .Select(x => x + subMesh.baseVertex));
                    foreach (var mergeMesh in toMerge)
                        triangles.AddRange(mergeMesh.triangles);
                    mesh.SetIndices(triangles.ToArray(), subMesh.topology, i, true, subMesh.baseVertex);
                }
            }

            #endregion
        }

        private static T[] ExtendAttribute<T>(MeshData original, int totalCount, MeshData[] meshes,
            Func<MeshData, T[]> selector)
        {
            var originalData = selector(original);
            if (originalData.Length == 0) return Array.Empty<T>();
            var result = new T[totalCount];

            originalData.CopyTo(result, 0);
            var offset = original.vertices.Length;
            foreach (ref var mesh in meshes.AsSpan())
            {
                var data = selector(mesh);
                data.CopyTo(result, offset);
                offset += mesh.vertices.Length;
            }

            return result;
        }

        private static MeshData GenerateAdditionalMeshData(
            MeshData originalMesh,
            SkinnedMeshRenderer renderer,
            AddMangaExpressionBlendShape.AddMeshes addMesh,
            int vertexIndexOffset
        )
        {
            var boneIndex = Array.IndexOf(renderer.bones, addMesh.bone);
            if (boneIndex == -1)
                throw new ArgumentException("bone not found in renderer", nameof(addMesh));
            var addMeshData = new MeshData(addMesh.mesh);

            var boneBindPose = originalMesh.bindposes[boneIndex];
            var boneBindPoseInverse = boneBindPose.inverse;

            var visibleTransform = boneBindPoseInverse * addMesh.visiblePosition.Matrix;
            var hiddenTransform = visibleTransform * addMesh.hiddenOffset.Matrix;

            // compute visible position
            var visiblePosition = new Vector3[addMeshData.vertices.Length];
            var visibleNormal = new Vector3[addMeshData.normals.Length];
            var visibleTangent = new Vector4[addMeshData.tangents.Length];
            for (var i = 0; i < addMeshData.vertices.Length; i++)
            {
                visiblePosition[i] = visibleTransform.MultiplyPoint3x4(addMeshData.vertices[i]);
                visibleNormal[i] = visibleTransform.MultiplyVector(addMeshData.normals[i]);
                visibleTangent[i] = visibleTransform.MultiplyVector(addMeshData.tangents[i]);
            }

            // set hidden position as default position
            for (var i = 0; i < addMeshData.vertices.Length; i++)
            {
                addMeshData.vertices[i] = hiddenTransform.MultiplyPoint3x4(addMeshData.vertices[i]);
                addMeshData.normals[i] = hiddenTransform.MultiplyVector(addMeshData.normals[i]);
                var tangent = addMeshData.tangents[i];
                Vector4 tangentNew = hiddenTransform.MultiplyVector(tangent);
                tangentNew.w = tangent.w;
                addMeshData.tangents[i] = tangentNew;
            }

            // generate blend shape to transform to visible position
            var blendShapeVertices = new Vector3[addMeshData.vertices.Length];
            var blendShapeNormals = new Vector3[addMeshData.normals.Length];
            var blendShapeTangents = new Vector3[addMeshData.tangents.Length];
            for (var i = 0; i < addMeshData.vertices.Length; i++)
            {
                blendShapeVertices[i] = visiblePosition[i] - addMeshData.vertices[i];
                blendShapeNormals[i] = visibleNormal[i] - addMeshData.normals[i];
                blendShapeTangents[i] = visibleTangent[i] - addMeshData.tangents[i];
            }

            // save data
            addMeshData.blendShapes = new[] { ("", 100.0f) };
            addMeshData.blendShapeVertices = new[] { blendShapeVertices };
            addMeshData.blendShapeNormals = new[] { blendShapeNormals };
            addMeshData.blendShapeTangents = new[] { blendShapeTangents };

            // save bone weight
            var boneWeightPerVertex = new byte[addMeshData.vertices.Length];
            var boneWeights = new BoneWeight1[addMeshData.vertices.Length];
            boneWeightPerVertex.AsSpan().Fill(1);
            boneWeights.AsSpan().Fill(new BoneWeight1 { boneIndex = boneIndex, weight = 1 });
            addMeshData.boneWeightsPerVertex = boneWeightPerVertex;
            addMeshData.boneWeights = boneWeights;

            // merge submeshes
            // note: requires Triangles

            var trianglesMerged = new List<int>(addMeshData.triangles.Length);
            foreach (var subMeshDescriptor in addMeshData.subMeshes)
            {
                trianglesMerged.AddRange(addMeshData.triangles
                    .Skip(subMeshDescriptor.indexStart)
                    .Take(subMeshDescriptor.indexCount)
                    .Select(x => x + subMeshDescriptor.baseVertex + vertexIndexOffset));
            }

            addMeshData.triangles = trianglesMerged.ToArray();
            addMeshData.subMeshes = new[] { new SubMeshDescriptor(0, trianglesMerged.Count) };

            return addMeshData;
        }
    }

    class MeshData
    {
        // ReSharper disable InconsistentNaming
        public Vector3[] vertices;
        public Vector3[] normals;
        public Vector4[] tangents;
        public TexCoordStatus[] uvStatus;
        public Vector4[][] uvs;
        public Color32[] colors;

        public byte[] boneWeightsPerVertex;
        public BoneWeight1[] boneWeights;
        public (string name, float weight)[] blendShapes;
        public Vector3[][] blendShapeVertices;
        public Vector3[][] blendShapeNormals;
        public Vector3[][] blendShapeTangents;

        public int[] triangles;
        public SubMeshDescriptor[] subMeshes;
        public Matrix4x4[] bindposes;
        // ReSharper restore InconsistentNaming

        public MeshData(Mesh mesh)
        {
            vertices = mesh.vertices;
            normals = mesh.normals;
            tangents = mesh.tangents;

            #region uv

            uvStatus = new TexCoordStatus[7];
            uvs = new Vector4[7][];
            var uv2 = new List<Vector2>(0);
            var uv3 = new List<Vector3>(0);
            var uv4 = new List<Vector4>(0);
            for (var index = 0; index < 7; index++)
            {
                switch (mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + index))
                {
                    case 2:
                        uvStatus[index] = TexCoordStatus.Vector2;
                        mesh.GetUVs(index, uv2);
                        uvs[index] = new Vector4[mesh.vertexCount];
                        for (int i = 0; i < mesh.vertexCount; i++)
                            uvs[index][i] = uv2[i];
                        break;
                    case 3:
                        uvStatus[index] = TexCoordStatus.Vector3;
                        mesh.GetUVs(index, uv3);
                        uvs[index] = new Vector4[mesh.vertexCount];
                        for (int i = 0; i < mesh.vertexCount; i++)
                            uvs[index][i] = uv3[i];
                        break;
                    case 4:
                        uvStatus[index] = TexCoordStatus.Vector4;
                        mesh.GetUVs(index, uv4);
                        uvs[index] = uv4.ToArray();
                        break;
                }
            }

            #endregion uv

            colors = mesh.colors32;
            boneWeightsPerVertex = mesh.GetBonesPerVertex().ToArray();
            boneWeights = mesh.GetAllBoneWeights().ToArray();
            // ReSharper disable LocalVariableHidesMember
            var blendShapes = new List<(string name, float weight)>();
            var blendShapeVertices = new List<Vector3[]>();
            var blendShapeNormals = new List<Vector3[]>();
            var blendShapeTangents = new List<Vector3[]>();
            // ReSharper restore LocalVariableHidesMember

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                var count = mesh.GetBlendShapeFrameCount(i);
                for (int j = 0; j < count; j++)
                {
                    var verticesBuffer = new Vector3[mesh.vertexCount];
                    var normalsBuffer = new Vector3[mesh.vertexCount];
                    var tangentsBuffer = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(i, j, verticesBuffer, normalsBuffer, tangentsBuffer);
                    blendShapes.Add((name, mesh.GetBlendShapeFrameWeight(i, j)));
                    blendShapeVertices.Add(verticesBuffer);
                    blendShapeNormals.Add(normalsBuffer);
                    blendShapeTangents.Add(tangentsBuffer);
                }
            }

            this.blendShapes = blendShapes.ToArray();
            this.blendShapeVertices = blendShapeVertices.ToArray();
            this.blendShapeNormals = blendShapeNormals.ToArray();
            this.blendShapeTangents = blendShapeTangents.ToArray();

            triangles = mesh.triangles;
            subMeshes = new SubMeshDescriptor[mesh.subMeshCount];
            for (int i = 0; i < mesh.subMeshCount; i++)
                subMeshes[i] = mesh.GetSubMesh(i);
            bindposes = mesh.bindposes;
        }

        public enum TexCoordStatus
        {
            NotDefined = 0,
            Vector2 = 1,
            Vector3 = 2,
            Vector4 = 3,
        }
    }
}