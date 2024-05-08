using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Debug = System.Diagnostics.Debug;

namespace Anatawa12.Modifier4Avatar.Editor
{
    // Note: this is in-place
    public class RemoveEyeBlendShapeGenerator
    {
        private readonly Mesh _mesh;
        private readonly GenerateRemoveEyeBlendShape _config;

        public RemoveEyeBlendShapeGenerator(Mesh mesh, GenerateRemoveEyeBlendShape config)
        {
            _mesh = mesh;
            _config = config;
        }

        public void Generate()
        {
            var index = FindBlendShape();
            if (index == -1) throw new Exception("base shape not found");

            Assert.IsTrue(_mesh.GetBlendShapeFrameCount(index) == 1);
            var weight = _mesh.GetBlendShapeFrameWeight(index, 0);

            var deltas = new Vector3[_mesh.vertexCount];
            var normals = new Vector3[_mesh.vertexCount];
            var tangents = new Vector3[_mesh.vertexCount];
            _mesh.GetBlendShapeFrameVertices(index, 0, deltas, normals, tangents);

            var islands = ComputeIslands();
            var isAllMoving = new bool[islands.Count];
            for (var i = 0; i < islands.Count; i++)
            {
                var allMoving = true;
                foreach (var vertexIndex in islands[i])
                {
                    var value = deltas[vertexIndex.value][(int)_config.checkAxis % 3];
                    var plus = (int)_config.checkAxis < 3;

                    if (!(plus ? value > _config.threshold : value < -_config.threshold))
                    {
                        allMoving = false;
                        break;
                    }
                }

                isAllMoving[i] = allMoving;
            }

            var keep = new bool[_mesh.vertexCount];
            for (var i = 0; i < islands.Count; i++)
            {
                if (isAllMoving[i]) continue;
                foreach (var vertexIndex in islands[i])
                    keep[vertexIndex.value] = true;
            }

            void ApplyKeep(Vector3[] array)
            {
                for (var i = 0; i < array.Length; i++)
                {
                    if (!keep[i])
                        array[i] = Vector3.zero;
                    else
                        array[i] *= _config.shapeMultiplier;
                }
            }

            ApplyKeep(deltas);
            ApplyKeep(normals);
            ApplyKeep(tangents);

            _mesh.AddBlendShapeFrame(_config.removeEyeBlendShapeName, weight, deltas, normals, tangents);
        }

        private int FindBlendShape()
        {
            for (var i = 0; i < _mesh.blendShapeCount; i++)
            {
                if (_mesh.GetBlendShapeName(i) == _config.baseShapeName)
                    return i;
            }

            return -1;
        }

        readonly struct VertexIndex : IEquatable<VertexIndex>
        {
            public readonly int value;

            public bool Equals(VertexIndex other) => value == other.value;
            public override bool Equals(object obj) => obj is VertexIndex other && Equals(other);
            public override int GetHashCode() => value;
            public static bool operator ==(VertexIndex left, VertexIndex right) => left.Equals(right);
            public static bool operator !=(VertexIndex left, VertexIndex right) => !left.Equals(right);
        }

        readonly struct TriangleIndex : IEquatable<TriangleIndex>
        {
            public readonly VertexIndex zero;
            public readonly VertexIndex one;
            public readonly VertexIndex two;

            public TriangleIndex(VertexIndex zero, VertexIndex one, VertexIndex two)
            {
                this.zero = zero;
                this.one = one;
                this.two = two;
            }

            public bool Equals(TriangleIndex other) =>
                zero.Equals(other.zero) && one.Equals(other.one) && two.Equals(other.two);

            public override bool Equals(object obj) => obj is TriangleIndex other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(zero, one, two);
            public static bool operator ==(TriangleIndex left, TriangleIndex right) => left.Equals(right);
            public static bool operator !=(TriangleIndex left, TriangleIndex right) => !left.Equals(right);
        }

        private List<List<VertexIndex>> ComputeIslands()
        {
            Debug.Assert(_mesh.triangles.Length % 3 == 0);
            var trianglesArray = MemoryMarshal
                .CreateSpan(ref UnsafeUtility.As<int, TriangleIndex>(ref _mesh.triangles[0]),
                    _mesh.triangles.Length / 3).ToArray();
            var triangles = new HashSet<TriangleIndex>(trianglesArray);
            var trianglesByVertex = new HashSet<TriangleIndex>[_mesh.vertexCount];

            // initialize dictionary for less jumps
            for (var i = 0; i < trianglesByVertex.Length; i++)
                trianglesByVertex[i] = new HashSet<TriangleIndex>();

            // collect all triangles by each triangle
            foreach (var triangle in triangles)
            {
                trianglesByVertex[triangle.zero.value].Add(triangle);
                trianglesByVertex[triangle.one.value].Add(triangle);
                trianglesByVertex[triangle.two.value].Add(triangle);
            }

            var islands = new List<List<VertexIndex>>();

            while (triangles.Count != 0)
            {
                var entryPoint = triangles.First();
                triangles.Remove(entryPoint);
                var verticesOfIsland = new List<VertexIndex> { };

                var proceedVertexes = new HashSet<VertexIndex>();
                var processVertexQueue = new Queue<VertexIndex>();

                if (proceedVertexes.Add(entryPoint.zero)) processVertexQueue.Enqueue(entryPoint.zero);
                if (proceedVertexes.Add(entryPoint.one)) processVertexQueue.Enqueue(entryPoint.one);
                if (proceedVertexes.Add(entryPoint.two)) processVertexQueue.Enqueue(entryPoint.two);

                while (processVertexQueue.Count != 0)
                {
                    var vertex = processVertexQueue.Dequeue();
                    var trianglesCandidate = trianglesByVertex[vertex.value];
                    verticesOfIsland.Add(vertex);

                    foreach (var triangle in trianglesCandidate)
                    {
                        // already the triangle is proceed
                        if (!triangles.Remove(triangle)) continue;

                        if (proceedVertexes.Add(triangle.zero)) processVertexQueue.Enqueue(triangle.zero);
                        if (proceedVertexes.Add(triangle.one)) processVertexQueue.Enqueue(triangle.one);
                        if (proceedVertexes.Add(triangle.two)) processVertexQueue.Enqueue(triangle.two);
                    }
                }

                islands.Add(verticesOfIsland);
            }

            return islands;
        }
    }
}