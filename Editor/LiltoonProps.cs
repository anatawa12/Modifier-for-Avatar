using UnityEngine;

namespace Anatawa12.Modifier4Avatar.Editor
{
    public static class LiltoonProps
    {
        public static readonly int LightMinLimit = Shader.PropertyToID("_LightMinLimit");
        public static readonly int LightMaxLimit = Shader.PropertyToID("_LightMaxLimit");
        public static readonly int MonochromeLighting = Shader.PropertyToID("_MonochromeLighting");
        public static readonly int ShadowEnvStrength = Shader.PropertyToID("_ShadowEnvStrength");
        public static readonly int AsUnlit = Shader.PropertyToID("_AsUnlit");
        public static readonly int VertexLightStrength = Shader.PropertyToID("_VertexLightStrength");
        public static readonly int LightDirectionOverride = Shader.PropertyToID("_LightDirectionOverride");
        public static readonly int BlendOpFa = Shader.PropertyToID("_BlendOpFA");
    }
}