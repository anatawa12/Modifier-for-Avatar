using UnityEditor;
using UnityEngine;
using Debug = System.Diagnostics.Debug;

namespace Anatawa12.Modifier4Avatar.Editor
{
    [CustomEditor(typeof(ArrangeLilToonLightSettings))]
    [CanEditMultipleObjects]
    public class ArrangeLilToonLightSettingsInspector : UnityEditor.Editor
    {
        Material sourceMaterial;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            sourceMaterial = EditorGUILayout.ObjectField("Source Material",
                sourceMaterial, typeof(Material), true) as Material;
            using (new EditorGUI.DisabledScope(!sourceMaterial))
            {
                if (GUILayout.Button("Import from Source"))
                {
                    Debug.Assert(sourceMaterial != null, nameof(sourceMaterial) + " != null");

                    serializedObject.FindProperty(nameof(ArrangeLilToonLightSettings.minLimit)).floatValue =
                        sourceMaterial.GetFloat(LiltoonProps.LightMinLimit);
                    serializedObject.FindProperty(nameof(ArrangeLilToonLightSettings.maxLimit)).floatValue =
                        sourceMaterial.GetFloat(LiltoonProps.LightMaxLimit);
                    serializedObject.FindProperty(nameof(ArrangeLilToonLightSettings.monochromeLighting)).floatValue =
                        sourceMaterial.GetFloat(LiltoonProps.MonochromeLighting);
                    serializedObject.FindProperty(nameof(ArrangeLilToonLightSettings.environmentStrength)).floatValue =
                        sourceMaterial.GetFloat(LiltoonProps.ShadowEnvStrength);

                    serializedObject.FindProperty(nameof(ArrangeLilToonLightSettings.asUnlit)).floatValue =
                        sourceMaterial.GetFloat(LiltoonProps.AsUnlit);
                    serializedObject.FindProperty(nameof(ArrangeLilToonLightSettings.vertexLightStrength)).floatValue =
                        sourceMaterial.GetFloat(LiltoonProps.VertexLightStrength);
                    var lightStrengthOverride = sourceMaterial.GetVector(LiltoonProps.LightDirectionOverride);
                    serializedObject.FindProperty(nameof(ArrangeLilToonLightSettings.lightDirectionOverride)).vector3Value =
                        new Vector3(lightStrengthOverride.x, lightStrengthOverride.y, lightStrengthOverride.z);
                    serializedObject.FindProperty(nameof(ArrangeLilToonLightSettings.followObjectOrientation)).boolValue
                        = lightStrengthOverride.w != 0;
                    serializedObject.FindProperty(nameof(ArrangeLilToonLightSettings.blendOp)).intValue =
                        sourceMaterial.GetInt(LiltoonProps.BlendOpFa);

                    serializedObject.ApplyModifiedProperties();
                }
            }
        }
    }
}