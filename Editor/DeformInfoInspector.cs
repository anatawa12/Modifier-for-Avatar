using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Anatawa12.Modifier4Avatar.Editor
{
    [CustomEditor(typeof(DeformInfo))]
    class DeformInfoInspector : UnityEditor.Editor
    {
        public GameObject importFrom;
        private SerializedProperty _transformInfos;
        private SerializedProperty _eyePosition;

        private void OnEnable()
        {
            _transformInfos = serializedObject.FindProperty(nameof(DeformInfo.transformInfos));
            _eyePosition = serializedObject.FindProperty(nameof(DeformInfo.eyePosition));
        }

        public override void OnInspectorGUI()
        {
            importFrom = EditorGUILayout.ObjectField("Import", importFrom, typeof(GameObject), true) as GameObject;
            if (importFrom && GUILayout.Button("Import From GameObject"))
            {
                ImportFromGameObject(importFrom);
                var avatarDescriptor = importFrom.GetComponent<VRCAvatarDescriptor>();
                if (avatarDescriptor)
                    _eyePosition.vector3Value = avatarDescriptor.ViewPosition;
            }

            EditorGUILayout.PropertyField(_eyePosition);
            GUILayout.Label("Transforms", EditorStyles.boldLabel);
            if (_transformInfos.arraySize != 0)
            {
                var rootProperty = _transformInfos.GetArrayElementAtIndex(0);
                var indices = rootProperty.FindPropertyRelative(nameof(TransformInfo.childIndices));
                for (var i = 0; i < indices.arraySize; i++)
                    DrawTransformTree(_transformInfos, indices.GetArrayElementAtIndex(i).intValue);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTransformTree(SerializedProperty array, int index)
        {
            var element = array.GetArrayElementAtIndex(index);
            var name = element.FindPropertyRelative(nameof(TransformInfo.name)).stringValue;
            element.isExpanded = EditorGUILayout.Foldout(element.isExpanded, name);
            if (element.isExpanded)
            {
                EditorGUI.indentLevel++;
                var enabledProp = element.FindPropertyRelative(nameof(TransformInfo.enabled));
                enabledProp.boolValue = EditorGUILayout.ToggleLeft("enabled", enabledProp.boolValue);
                if (enabledProp.boolValue)
                {
                    var indices = element.FindPropertyRelative(nameof(TransformInfo.childIndices));
                    for (var i = 0; i < indices.arraySize; i++)
                        DrawTransformTree(array, indices.GetArrayElementAtIndex(i).intValue);
                }
                EditorGUI.indentLevel--;
            }
        }

        private void ImportFromGameObject(GameObject gameObject)
        {
            if (_transformInfos.arraySize != 0)
            {
                if (!EditorUtility.DisplayDialog("Modifier for Avatar", 
                        "Importing from GameObject will clear current deform information for transform.\n" +
                        "Do you actually want to import from GameObject?",
                        "Import", "Cancel"))
                    return;
            }

            int cursor = 0;
            _transformInfos.arraySize = 1;
            ProcessRecursive(gameObject.transform, _transformInfos, ref cursor);
        }

        private void ProcessRecursive(Transform transform, SerializedProperty array, ref int cursor)
        {
            var property = array.GetArrayElementAtIndex(cursor++);
            property.FindPropertyRelative(nameof(TransformInfo.name)).stringValue = transform.name;   
            property.FindPropertyRelative(nameof(TransformInfo.enabled)).boolValue = true;
            property.FindPropertyRelative(nameof(TransformInfo.position)).vector3Value = transform.localPosition;
            property.FindPropertyRelative(nameof(TransformInfo.rotation)).quaternionValue = transform.localRotation;
            property.FindPropertyRelative(nameof(TransformInfo.scale)).vector3Value = transform.localScale;

            var indices = property.FindPropertyRelative(nameof(TransformInfo.childIndices));

            var childCount = transform.childCount;
            array.arraySize += childCount;
            indices.arraySize = childCount;
            for (var i = 0; i < childCount; i++)
            {
                indices.GetArrayElementAtIndex(i).intValue = cursor;
                ProcessRecursive(transform.GetChild(i), array, ref cursor);
            }
        }
    }
}