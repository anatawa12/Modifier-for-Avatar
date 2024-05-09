using System;
using UnityEngine;

namespace Anatawa12.Modifier4Avatar
{
    [AddComponentMenu("Avatar Modifier/M4A Add Manga Expression Blend Shape")]
    public class AddMangaExpressionBlendShape : MonoBehaviour
    {
        public string newBlendShapeName = "newBlendShape";
        public bool normalBlendShape = false;
        public bool tangentBlendShape = false;
        public CombineBlendShape[] combineBlendShapes = Array.Empty<CombineBlendShape>();
        public AddMeshes[] addMeshes = Array.Empty<AddMeshes>();

        private void Reset()
        {
            combineBlendShapes = new CombineBlendShape[1];
            combineBlendShapes[0].name = "toCompbine";
            combineBlendShapes[0].weight = 1;
            addMeshes = new AddMeshes[1];
            addMeshes[0].visiblePosition.rotation = Quaternion.identity;
            addMeshes[0].visiblePosition.scale = Vector3.one;
            addMeshes[0].hiddenOffset.rotation = Quaternion.identity;
            addMeshes[0].hiddenOffset.scale = Vector3.one;
        }

        [Serializable]
        public struct CombineBlendShape
        {
            public string name;
            public float weight;
        }

        [Serializable]
        public struct AddMeshes
        {
            public Mesh mesh;
            public Transform bone;
            public int materialIndex;
            public TransRotateScale visiblePosition;
            public TransRotateScale hiddenOffset;
        }

        [Serializable]
        public struct TransRotateScale
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;

            public Matrix4x4 Matrix => Matrix4x4.TRS(position, rotation, scale);
        }
    }
}
