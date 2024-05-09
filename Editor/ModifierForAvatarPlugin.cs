using System;
using System.Collections.Generic;
using Anatawa12.Modifier4Avatar.Editor;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(ModifierForAvatarPlugin))]

namespace Anatawa12.Modifier4Avatar.Editor
{
    class ModifierForAvatarPlugin : Plugin<ModifierForAvatarPlugin>
    {
        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("MakeSkinnedMesh", ctx =>
                {
                    foreach (var makeSkinnedMesh in ctx.AvatarRootObject.GetComponentsInChildren<MakeSkinnedMesh>())
                    {
                        var meshRenderer = makeSkinnedMesh.GetComponent<MeshRenderer>();
                        var meshFilter = makeSkinnedMesh.GetComponent<MeshFilter>();
                        if (!meshRenderer || !meshFilter)
                        {
                            Object.DestroyImmediate(makeSkinnedMesh);
                            ErrorReport.ReportError(Localizer, ErrorSeverity.Error,
                                "MakeSkinnedMesh: no MeshRenderer or MeshFilter", makeSkinnedMesh);
                            continue;
                        }

                        var skinnedMeshRenderer = makeSkinnedMesh.gameObject.AddComponent<SkinnedMeshRenderer>();
                        var mesh = skinnedMeshRenderer.sharedMesh;
                        var meshName = mesh.name;
                        mesh = Object.Instantiate(mesh);
                        skinnedMeshRenderer.sharedMesh = mesh;
                        mesh.name = meshName + " (Originally static)";

                        var transform = makeSkinnedMesh.transform;

                        skinnedMeshRenderer.rootBone = transform;
                        skinnedMeshRenderer.bones = new[] { transform };

                        mesh.bindposes = new[] { Matrix4x4.identity };

                        var vertexCount = mesh.vertexCount;
                        using (var bonesPerVertex1 = new NativeArray<byte>(vertexCount, Allocator.Temp))
                        using (var weights1 = new NativeArray<BoneWeight1>(vertexCount, Allocator.Temp))
                        {
                            var bonesPerVertex = bonesPerVertex1;
                            var weights = weights1;
                            for (var i = 0; i < vertexCount; i++)
                            {
                                bonesPerVertex[i] = 1;
                                weights[i] = new BoneWeight1 { boneIndex = 0, weight = 1 };
                            }

                            mesh.SetBoneWeights(bonesPerVertex, weights);
                        }

                        Object.DestroyImmediate(makeSkinnedMesh);
                        Object.DestroyImmediate(meshFilter);
                    }
                });

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

            // mesh editor
            InPhase(BuildPhase.Transforming)
                .Run("RemoveEyeBlendShapeGenerator", ctx =>
                {
                    foreach (var config in ctx.AvatarRootObject.GetComponentsInChildren<GenerateRemoveEyeBlendShape>())
                    {
                        var renderer = config.GetComponent<SkinnedMeshRenderer>();
                        var mesh = renderer.sharedMesh;
                        if (!mesh)
                        {
                            ErrorReport.ReportError(Localizer, ErrorSeverity.Error,
                                "RemoveEyeBlendShapeGenerator: no mesh", config);
                            continue;
                        }

                        if (!ctx.IsTemporaryAsset(mesh))
                        {
                            mesh = Object.Instantiate(mesh);
                            renderer.sharedMesh = mesh;
                        }

                        new RemoveEyeBlendShapeGenerator(
                            mesh,
                            config
                        ).Generate();
                        Object.DestroyImmediate(config);
                    }
                })
                .Then.Run("MangaExpressionBlendShapeGenerator", ctx =>
                {
                    foreach (var config in ctx.AvatarRootObject.GetComponentsInChildren<AddMangaExpressionBlendShape>())
                    {
                        var renderer = config.GetComponent<SkinnedMeshRenderer>();
                        var mesh = renderer.sharedMesh;
                        if (!mesh)
                        {
                            ErrorReport.ReportError(Localizer, ErrorSeverity.Error,
                                "MangaBlendShapeGeneartor: no mesh", config);
                            continue;
                        }

                        if (!ctx.IsTemporaryAsset(mesh))
                        {
                            mesh = Object.Instantiate(mesh);
                            renderer.sharedMesh = mesh;
                        }

                        AddMangaExpressionBlendShapeGenerator.Generate(mesh, renderer, config);
                        Object.DestroyImmediate(config);
                    }
                })
                ;
        }

        public static Localizer Localizer { get; } = new Localizer("en-us",
            () => new List<(string, Func<string, string>)> { ("en-us", key => key) });
    }
}