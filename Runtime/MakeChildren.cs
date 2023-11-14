using UnityEngine;
using UnityEngine.Animations;
using VRC.SDKBase;

namespace Anatawa12.Modifier4Avatar
{
    class MakeChildren : MonoBehaviour, IEditorOnly
    {
        [NotKeyable]
        public Transform[] children;
    }
}
