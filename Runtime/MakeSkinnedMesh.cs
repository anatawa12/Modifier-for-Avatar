using UnityEngine;
using VRC.SDKBase;

namespace Anatawa12.Modifier4Avatar
{
    /// <summary>
    /// This component will make the Mesh Renderer to Skinned Mesh Renderer
    ///
    /// The skinned mesh renderer will be deformed with bone.
    ///
    /// This component will be applied before Modular Avatar so Mesh Settings will
    /// (also) be applied to Skinned Mesh Renderer created by this component.
    ///
    /// This component can be used to make the mesh merged by Automatic Merge Skinned Mesh in Trace And Optimize in AAO.
    /// </summary>
    [AddComponentMenu("Avatar Modifier/M4A Make Skinned Mesh")]
    internal class MakeSkinnedMesh : MonoBehaviour, IEditorOnly
    {
    }
}

