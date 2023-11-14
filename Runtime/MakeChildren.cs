using UnityEngine;
using UnityEngine.Animations;
using VRC.SDKBase;

namespace Anatawa12.Modifier4Avatar
{
    [AddComponentMenu("Avatar Modifier/M4A Make Children")]
    class MakeChildren : MonoBehaviour, IEditorOnly
    {
        [NotKeyable]
        public Transform[] children;
    }
}
