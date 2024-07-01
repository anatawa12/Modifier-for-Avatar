#if LILTOON
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using VRC.SDKBase;

namespace Anatawa12.Modifier4Avatar
{
    [AddComponentMenu("Avatar Modifier/M4A Arrange lilToon Light Settings")]
    public class ArrangeLilToonLightSettings : MonoBehaviour, IEditorOnly
    {
        [Range(0,1)]
        public float minLimit = 0.05f;
        [Range(0,10)]
        public float maxLimit = 1f;
        [Range(0, 1)]
        public float monochromeLighting = 0f;
        [Range(0, 1)]
        public float environmentStrength = 0f;

        [Range(0, 1)]
        public float asUnlit = 0f;
        [Range(0, 1)]
        public float vertexLightStrength = 0f;
        public Vector3 lightDirectionOverride = new (0f, 0.001f, 0f);
        public bool followObjectOrientation = false;
        public BlendOp blendOp = BlendOp.Max;

        public Vector4 LightStrengthOverrideVector4 => new(lightDirectionOverride.x, lightDirectionOverride.y,
            lightDirectionOverride.z, followObjectOrientation ? 1 : 0);
    }
}
#endif
