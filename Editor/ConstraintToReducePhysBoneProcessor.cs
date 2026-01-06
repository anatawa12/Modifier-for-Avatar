#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using Unity.Mathematics;
using UnityEngine;
using VRC.Dynamics;
using VRC.Dynamics.ManagedTypes;
using VRC.SDK3.Dynamics.Constraint.Components;
using static Anatawa12.Modifier4Avatar.Editor.ModifierForAvatarPlugin;

namespace Anatawa12.Modifier4Avatar.Editor
{
    public readonly struct ConstraintToReducePhysBoneProcessor
    {
        private readonly Dictionary<Transform, VRCPhysBoneBase> physBoneMap;

        public ConstraintToReducePhysBoneProcessor(GameObject avatar)
        {
            // create map from transform to physBones
            // We do not support multi physbone per one transform
            var physBoneMap = new Dictionary<Transform, VRCPhysBoneBase>();
            foreach (var physBone in avatar.GetComponentsInChildren<VRCPhysBoneBase>(true))
            {
                var rootTransform = physBone.GetRootTransform();
                var ignores = physBone.ignoreTransforms;
                AddToMap(rootTransform);

                void AddToMap(Transform t)
                {
                    if (ignores.Contains(t)) return;
                    physBoneMap.TryAdd(t, physBone);
                    foreach (Transform child in t) AddToMap(child);
                }
            }
            this.physBoneMap = physBoneMap;
        }

        public void Process(ConstraintToReducePhysBone component)
        {
            var transform = component.transform;
            var children = transform.OfType<Transform>().Where(physBoneMap.ContainsKey).ToArray();

            Transform[] pbChains = component.pbChains.Where(x => x != null).ToArray()!;

            if (pbChains.Any(x => !children.Contains(x)))
            {
                ErrorReport.ReportError(Localizer, ErrorSeverity.Error,
                    "ConstraintToReducePhysBone: pbChains contains transform that is not child of the component, or not affected by PhysBone",
                    component);
                return;
            }

            if (pbChains.Length < 1)
            {
                ErrorReport.ReportError(Localizer, ErrorSeverity.Error,
                    "ConstraintToReducePhysBone: pbChains is empty",
                    component);
                return;
            }

            if (children.Length <= 1)
            {
                // nothing to do
                return;
            }

            var constraintChains = children.Where(x => !pbChains.Contains(x)).ToArray();

            if (constraintChains.Length == 0) return;

            var constraintSources = BuildConstraintSources(
                constraintChains,
                pbChains
            );

            foreach (var (target, sources) in constraintSources)
                CreateConstraintChain(target, sources, component.solveInLocalSpace);

            foreach (var (target, _) in constraintSources)
            {
                var pb = physBoneMap[target];
                if (pb.GetRootTransform() == target)
                {
                    // remove entire physbone if the target is root of physbone
                    UnityEngine.Object.DestroyImmediate(pb);
                }
                else
                {
                    // otherwise, ignore the target transform
                    pb.ignoreTransforms.Add(target);
                }
            }
        }

        private void CreateConstraintChain(Transform target, List<(Transform source, float weight)> sources,
            bool solveInLocalSpace)
        {
            var constraint = target.gameObject.AddComponent<VRCRotationConstraint>();
            foreach (var source in sources)
            {
                constraint.Sources.Add(new VRCConstraintSource
                {
                    SourceTransform = source.source,
                    Weight = source.weight,
                });
            }
            
            constraint.Locked = false;
            constraint.IsActive = true;
            Reflections.TryBakeCurrentOffsetsRuntime(constraint, VRCConstraintBase.BakeOptions.BakeAll);
            constraint.Locked = true;
            constraint.SolveInLocalSpace = solveInLocalSpace;

            var targetChildren = target.OfType<Transform>().Where(physBoneMap.ContainsKey).ToArray();
            if (targetChildren.Length == 0) return;
            if (targetChildren.Length > 1)
            {
                ErrorReport.ReportError(Localizer, ErrorSeverity.NonFatal,
                    "ConstraintToReducePhysBone: Created constraint target has multiple PhysBone-affected children. Only first child is constrained.",
                    target);
            }
            var childSources = new List<(Transform source, float weight)>();
            foreach (var source in sources)
            {
                var sourceChildren = source.source.OfType<Transform>().Where(physBoneMap.ContainsKey).ToArray();
                if (sourceChildren.Length == 0) continue;
                if (sourceChildren.Length > 1)
                {
                    ErrorReport.ReportError(Localizer, ErrorSeverity.NonFatal,
                        "ConstraintToReducePhysBone: Created constraint source has multiple PhysBone-affected children. Only first child is used as source.",
                        source.source);
                }
                childSources.Add((sourceChildren[0], source.weight));
            }
            if (childSources.Count == 0) return;

            CreateConstraintChain(targetChildren[0], childSources, solveInLocalSpace);
        }

        static List<(Transform target, List<(Transform source, float weight)>)> BuildConstraintSources(
            Transform[] targets,
            Transform[] sources
        )
        {
            if (targets.Length == 0) return new List<(Transform target, List<(Transform source, float weight)>)>();
            if (sources.Length == 1)
            {
                // simple; all to one
                return targets.Select(t => (t, new List<(Transform source, float weight)> { (sources[0], 1f) }))
                    .ToList();
            }

            var transforms = targets.Select(x => (isTraget: true, transform: x))
                .Concat(sources.Select(x => (isTraget: false, transform: x)))
                .ToArray();

            // we distribute sources based on the position of targets and sources.
            // We expect to use this component on skirt bones, so we assume that the targets and sources
            // are similarly on plane and on a circle-like layout.
            // Each target should be influenced by the nearest two sources on the circle.
            
            // Therefore, we first fit a plane to the points.
            // Second, create one representative axis on the plane, and sort transforms based on the angle on the axis.
            // Finally, assign each target to the nearest two sources on the sorted list.

            var points = transforms.Select(t => (float3)t.transform.position).ToArray();
            var angles = CalculateAnglesFromCentroid(points);

            Array.Sort(angles, transforms);

            var result = new List<(Transform target, List<(Transform source, float weight)>)>();

            var leftSrcIndex = 0;
            for (; transforms[leftSrcIndex].isTraget; leftSrcIndex++) ;
            // assert: transforms[leftSrcIndex].isTarget == false
            while (true)
            {
                var rightSrcIndex = (leftSrcIndex + 1) % transforms.Length;
                for (; transforms[rightSrcIndex].isTraget; rightSrcIndex = (rightSrcIndex + 1) % transforms.Length) ;
                // assert: transforms[rightSrcIndex].isTarget == false

                // Then, all targets between leftSrcIndex and rightSrcIndex are influenced by these two sources.
                var leftSource = transforms[leftSrcIndex].transform;
                var rightSource = transforms[rightSrcIndex].transform;
                if (leftSrcIndex < rightSrcIndex)
                {
                    // simple case: left < right, no wrap
                    var numberOfTargets = rightSrcIndex - leftSrcIndex - 1;

                    for (int i = 1; i <= numberOfTargets; i++)
                    {
                        var target = transforms[leftSrcIndex + i].transform;
                        var weightRight = (float)i / (numberOfTargets + 1);
                        var weightLeft = 1f - weightRight;
                        result.Add((target, new List<(Transform source, float weight)>
                        {
                            (leftSource, weightLeft),
                            (rightSource, weightRight),
                        }));
                    }
                    leftSrcIndex = rightSrcIndex;
                    if (leftSrcIndex == 0) break; // Lucky break: completed full circle
                }
                else
                {
                    // wrap case: left > right
                    // This indicates this is the last segment

                    var numberOfTargets = transforms.Length - leftSrcIndex - 1 + rightSrcIndex;

                    for (int i = 1; i <= numberOfTargets; i++)
                    {
                        var targetIndex = (leftSrcIndex + i) % transforms.Length;
                        var target = transforms[targetIndex].transform;
                        var weightRight = (float)i / (numberOfTargets + 1);
                        var weightLeft = 1f - weightRight;
                        result.Add((target, new List<(Transform source, float weight)>
                        {
                            (leftSource, weightLeft),
                            (rightSource, weightRight),
                        }));
                    }

                    break;
                }
            }

            return result;
        }

        private static float[] CalculateAnglesFromCentroid(float3[] points)
        {
            if (points == null || points.Length < 2)
                throw new ArgumentException("Point cloud must contain at least two points.");

            var centroid = float3.zero;
            foreach (var point in points) centroid += point;
            centroid /= points.Length;

            var referenceVector = math.normalize(points[0] - centroid);
            var angles = new float[points.Length];

            for (var i = 0; i < points.Length; i++)
            {
                var currentVector = math.normalize(points[i] - centroid);
                var dotProduct = math.dot(referenceVector, currentVector);
                var angle = math.acos(math.clamp(dotProduct, -1f, 1f));

                var cross = math.cross(referenceVector, currentVector);
                if (math.abs(math.cmax(cross)) < math.abs(math.cmin(cross))) angle = -angle;

                angles[i] = math.degrees(angle);
            }

            return angles;
        }

        private static class Reflections
        {
            public delegate void TryBakeCurrentOffsetsRuntimeType(VRCConstraintBase constraint, VRCConstraintBase.BakeOptions bakeOptions);
            public static readonly TryBakeCurrentOffsetsRuntimeType TryBakeCurrentOffsetsRuntime =
                (TryBakeCurrentOffsetsRuntimeType)Delegate.CreateDelegate(
                    typeof(TryBakeCurrentOffsetsRuntimeType),
                    typeof(VRCConstraintBase).GetMethod(
                        "TryBakeCurrentOffsetsRuntime",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                        null,
                        new Type[] { typeof(VRCConstraintBase.BakeOptions) },
                        null
                    )!
                );
        }
    }
}
