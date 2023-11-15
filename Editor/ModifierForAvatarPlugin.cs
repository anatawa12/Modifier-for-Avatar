using Anatawa12.Modifier4Avatar.Editor;
using nadena.dev.ndmf;
using UnityEngine;

[assembly:ExportsPlugin(typeof(ModifierForAvatarPlugin))]

namespace Anatawa12.Modifier4Avatar.Editor
{
    class ModifierForAvatarPlugin : Plugin<ModifierForAvatarPlugin>
    {
        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .BeforePlugin("net.narazaka.vrchat.floor_adjuster")
                .AfterPlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(PathMapperContext), seq =>
                {
                    seq
                        .Run("MakeChildren", ctx =>
                        {
                            foreach (var makeChildren in ctx.AvatarRootObject.GetComponentsInChildren<MakeChildren>())
                            {
                                foreach (var child in makeChildren.children)
                                    child.parent = makeChildren.transform;
                                Object.DestroyImmediate(makeChildren);
                            }
                        })
                        ;
                });

            InPhase(BuildPhase.Transforming)
                .BeforePlugin("net.narazaka.vrchat.floor_adjuster")
                .AfterPlugin("nadena.dev.modular-avatar")
                .Run("Defromer", ctx =>
                {
                    var deformer = ctx.AvatarRootObject.GetComponent<Deformer>();
                    if (!deformer) return;
                    deformer.info.Apply(deformer.transform);
                    ctx.AvatarDescriptor.ViewPosition = deformer.info.eyePosition;
                    Object.DestroyImmediate(deformer);
                });
        }
    }
}