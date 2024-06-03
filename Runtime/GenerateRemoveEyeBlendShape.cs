using UnityEngine;
using VRC.SDKBase;

namespace Anatawa12.Modifier4Avatar
{
    [AddComponentMenu("Avatar Modifier/M4A Generate Remove Eye Blend Shape")]
    public class GenerateRemoveEyeBlendShape : MonoBehaviour, IEditorOnly
    {
        public string baseShapeName;
        public string removeEyeBlendShapeName;
        public Axis checkAxis = Axis.ZPlus;
        public float threshold = 0.05f;
        public float shapeMultiplier = 1.10f;

        public enum Axis
        {
            XPlus,
            YPlus,
            ZPlus,
            XMinus,
            YMinus,
            ZMinus,
        }
    }
}
