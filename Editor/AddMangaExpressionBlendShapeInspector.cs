using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Anatawa12.Modifier4Avatar.Editor
{
    [CustomEditor(typeof(AddMangaExpressionBlendShape))]
    public class AddMangaExpressionBlendShapeInspector : UnityEditor.Editor
    {
        private void OnEnable()
        {
            EditorApplication.update += Update;
            dirtyCount = -1;
        }

        float weight = 1;

        public override void OnInspectorGUI()
        {
            weight = EditorGUILayout.Slider("Weight", weight, 0, 1);
            base.OnInspectorGUI();
        }

        private List<PreviewRenderer> previewRenderers = new();

        float previewWeight = 1;
        int dirtyCount = -1;
        private void Update()
        {
            if (EditorUtility.GetDirtyCount(target) == dirtyCount && previewWeight == weight)
                return;
            previewWeight = weight;
            dirtyCount = EditorUtility.GetDirtyCount(target);

            var config = (AddMangaExpressionBlendShape)target;
            var renderer = config.GetComponent<SkinnedMeshRenderer>();
            if (!renderer) return;
            while (previewRenderers.Count < config.addMeshes.Length)
                previewRenderers.Add(CreatePreviewRenderer());
            while (previewRenderers.Count > config.addMeshes.Length)
            {
                DestroyImmediate(previewRenderers[^1].container);
                previewRenderers.RemoveAt(previewRenderers.Count - 1);
            }

            for (var i = 0; i < config.addMeshes.Length; i++)
            {
                var addMesh = config.addMeshes[i];
                if (addMesh.mesh == null || addMesh.bone == null) continue;

                var previewRenderer = previewRenderers[i];
                var previewMeshFilter = previewRenderer.filter;
                var containerTransform = previewRenderer.container.transform;
                containerTransform.SetParent(addMesh.bone);
                containerTransform.localPosition = addMesh.visiblePosition.position;
                containerTransform.localRotation = Quaternion.Normalize(addMesh.visiblePosition.rotation);
                containerTransform.localScale = addMesh.visiblePosition.scale;
                if (addMesh.materialIndex < renderer.sharedMaterials.Length)
                    previewRenderer.renderer.sharedMaterial = renderer.sharedMaterials[addMesh.materialIndex];
                else
                    previewRenderer.renderer.sharedMaterial = null;
                previewMeshFilter.sharedMesh = addMesh.mesh;

                var previewRendererTransform = previewRenderer.renderer.transform;
                previewRendererTransform.localPosition = Vector3.Lerp(addMesh.hiddenOffset.position, Vector3.zero, weight);
                previewRendererTransform.localRotation = Quaternion.Slerp(addMesh.hiddenOffset.rotation, Quaternion.identity, weight);
                previewRendererTransform.localScale = Vector3.Lerp(addMesh.hiddenOffset.scale, Vector3.one, weight);
            }
        }

        private void OnSceneGUI()
        {
            var config = (AddMangaExpressionBlendShape)target;

            for (var i = 0; i < config.addMeshes.Length; i++)
            {
                EditorGUI.BeginChangeCheck();
                var addMesh = config.addMeshes[i];
                if (addMesh.bone == null) continue;
                TRSHandle(addMesh.bone, ref addMesh.visiblePosition.position, ref addMesh.visiblePosition.rotation, ref addMesh.visiblePosition.scale);
                
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(config, "Move Mesh with Handle");
                    config.addMeshes[i] = addMesh;
                    EditorUtility.SetDirty(config);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(config);
                    serializedObject.Update();
                }
            }
        }

        private void TRSHandle(
            Transform baseTransform,
            ref Vector3 position,
            ref Quaternion rotation,
            ref Vector3 scale)
        {
            var transformRotation = baseTransform.rotation;
            var transformLossyScale = baseTransform.lossyScale;

            EditorGUI.BeginChangeCheck();

            var globalRotation = transformRotation * rotation;
            var globalRotationNew = globalRotation;

            var globalPosition = baseTransform.TransformPoint(position);
            var globalPositionNew = globalPosition;

            Handles.TransformHandle(ref globalPositionNew, ref globalRotationNew, ref scale);

            if (EditorGUI.EndChangeCheck())
            {
                if (globalPosition != globalPositionNew)
                {
                    var localDelta = baseTransform.InverseTransformPoint(globalPositionNew) - position;
                    SetPositionWithLocalDelta(ref position, localDelta);
                }

                var deltaRotation = Quaternion.Inverse(globalRotation) * globalRotationNew;
                deltaRotation.ToAngleAxis(out var angle, out _);
                if (!Mathf.Approximately(angle, 0))
                {
                    rotation = Quaternion.Normalize(Quaternion.Inverse(transformRotation) * globalRotationNew);
                }
            }

            return;

            float RoundBasedOnMinimumDifference(float valueToRound, float minDifference)
            {
                return minDifference == 0f
                    ? DiscardLeastSignificantDecimal(valueToRound)
                    : (float)Math.Round(valueToRound, GetNumberOfDecimalsForMinimumDifference(minDifference),
                        MidpointRounding.AwayFromZero);
            }

            float DiscardLeastSignificantDecimal(float v)
            {
                int digits = Mathf.Clamp((int)(5.0 - Mathf.Log10(Mathf.Abs(v))), 0, 15);
                return (float)Math.Round(v, digits, MidpointRounding.AwayFromZero);
            }

            int GetNumberOfDecimalsForMinimumDifference(float minDifference)
            {
                return Mathf.Clamp(-Mathf.FloorToInt(Mathf.Log10(Mathf.Abs(minDifference))), 0, 15);
            }

            void SetPositionWithLocalDelta(ref Vector3 position, Vector3 localDelta, bool withMinDifference = true)
            {
                var minDragDifference = Vector3.one * (HandleUtility.GetHandleSize(globalPosition) / 80f);
                minDragDifference.x /= transformLossyScale.x;
                minDragDifference.y /= transformLossyScale.y;
                minDragDifference.z /= transformLossyScale.z;

                var oldLocalPosition = position;
                var newLocalPosition = oldLocalPosition + localDelta;

                if (withMinDifference)
                {
                    newLocalPosition.x = RoundBasedOnMinimumDifference(newLocalPosition.x, minDragDifference.x);
                    newLocalPosition.y = RoundBasedOnMinimumDifference(newLocalPosition.y, minDragDifference.y);
                    newLocalPosition.z = RoundBasedOnMinimumDifference(newLocalPosition.z, minDragDifference.z);
                }

                newLocalPosition.x = Mathf.Approximately(localDelta.x, 0) ? oldLocalPosition.x : newLocalPosition.x;
                newLocalPosition.y = Mathf.Approximately(localDelta.y, 0) ? oldLocalPosition.y : newLocalPosition.y;
                newLocalPosition.z = Mathf.Approximately(localDelta.z, 0) ? oldLocalPosition.z : newLocalPosition.z;

                position = newLocalPosition;
            }
        }

        private PreviewRenderer CreatePreviewRenderer()
        {
            var container = new GameObject("Preview Container");
            container.hideFlags = HideFlags.HideAndDontSave;
            var gameObject = new GameObject("Preview");
            gameObject.transform.SetParent(container.transform);
            var renderer = gameObject.AddComponent<MeshRenderer>();
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            return new PreviewRenderer(
                renderer, 
                meshFilter,
                container);
        }

        private void OnDisable()
        {
            EditorApplication.update -= Update;
            foreach (var previewRenderer in previewRenderers)
                DestroyImmediate(previewRenderer.container);
            previewRenderers.Clear();

        }

        readonly struct PreviewRenderer
        {
            public readonly MeshRenderer renderer;
            public readonly MeshFilter filter;
            public readonly GameObject container;

            public PreviewRenderer(MeshRenderer renderer, MeshFilter filter, GameObject container)
            {
                this.renderer = renderer;
                this.filter = filter;
                this.container = container;
            }
        }
    }
}
