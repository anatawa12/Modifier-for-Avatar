using System;
using UnityEngine;

namespace Anatawa12.Modifier4Avatar
{
    [CreateAssetMenu(menuName = "M4A Deform Info")]
    class DeformInfo : ScriptableObject
    {
        public TransformInfo[] transformInfos;
        public Vector3 eyePosition;

        public void Apply(Transform transform)
        {
            if (transformInfos.Length == 0) return;

            transformInfos[0].Apply(transform, transformInfos);
        }
    }

    [Serializable]
    internal struct TransformInfo
    {
        public string name;
        public bool enabled;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public int[] childIndices;

        public void Apply(Transform transform, TransformInfo[] transformInfos)
        {
            transform.localPosition = position;
            transform.localRotation = rotation;
            transform.localScale = scale;

            foreach (var childIndex in childIndices)
            {
                var childInfo = transformInfos[childIndex];
                var child = transform.Find(childInfo.name);
                if (child) childInfo.Apply(child, transformInfos);
                // MA (FirstPersonVisible) support
                var firstPersonVisible = transform.Find(childInfo.name + " (FirstPersonVisible)");
                if (firstPersonVisible) childInfo.Apply(firstPersonVisible, transformInfos);
            }
        }
    }
}