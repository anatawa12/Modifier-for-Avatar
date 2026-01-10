#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        private readonly Dictionary<Transform, Transform> rollFixMap;

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
            rollFixMap = new Dictionary<Transform, Transform>();
        }

        [return:NotNullIfNotNull("t")]
        private Transform? GetFixed(Transform? t) => t == null ? null : rollFixMap.GetValueOrDefault(t, t);

        public void Process(ConstraintToReducePhysBone component)
        {
            var transform = component.transform;
            var children = transform.OfType<Transform>().Where(physBoneMap.ContainsKey).ToArray();

            if (component.rollFix)
            {
                DoRollFix(children);
                children = children.Select(GetFixed).ToArray();
            }

            Transform[] pbChains = component.pbChains.Where(x => x != null).Select(GetFixed).ToArray()!;

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

        private void DoRollFix(Transform[] children)
        {
            var fixedPhysBones = new HashSet<VRCPhysBoneBase>();
            foreach (var child in children)
            {
                var physBone = physBoneMap[child];
                if (fixedPhysBones.Contains(physBone)) continue;
                FixYawPitch(physBone);
                fixedPhysBones.Add(physBone);
            }
        }

        // based on https://github.com/anatawa12/AvatarOptimizer/blob/e6c31243afa0db51b05570c18a7d9f491c90f467/Editor/Processors/MergePhysBoneProcessor.cs#L307
        void FixYawPitch(VRCPhysBoneBase physBone)
        {
            // Already fixed; nothing to do!
            if (physBone.limitRotation.y.Equals(0.0f)) return;

            var originalBones = new List<Transform>();

            physBone.InitTransforms(true);
            var pbRoot = physBone.GetRootTransform();

            var ignoreTransforms = new HashSet<Transform>(physBone.ignoreTransforms);

            var newRoot = RotateRecursive(physBone, pbRoot, pbRoot.parent, 0, ignoreTransforms, originalBones);

            physBone.rootTransform = newRoot;
            physBone.ignoreTransforms.AddRange(originalBones);

            var chainLength = physBone.maxBoneChainIndex + (physBone.endpointPosition != Vector3.zero ? 1 : 0);
            var yaws = new float[chainLength];
            float fixedRollOfLastBone = 0;
            var pitches = new float[chainLength];

            for (var i = 0; i < chainLength; i++)
            {
                var rotationSpecified = physBone.CalcLimitRotation((float)i / (chainLength - 1));
                var rotation = ConvertRotation(rotationSpecified);
                pitches[i] = rotation.x;
                fixedRollOfLastBone = rotation.y;
                yaws[i] = rotation.z;
            }

            var maxPitch = pitches.Select(Mathf.Abs).Max();
            var maxYaw = yaws.Select(Mathf.Abs).Max();

            physBone.limitRotation = new Vector3(maxPitch, 0, maxYaw);

            if (maxPitch != 0 || maxYaw != 0)
            {
                // avoid NaN
                if (maxPitch == 0) maxPitch = 1;
                if (maxYaw == 0) maxYaw = 1;

                var pitchCurve = new AnimationCurve();
                var yawCurve = new AnimationCurve();

                pitchCurve.AddKey(0, pitches[0] / maxPitch);
                yawCurve.AddKey(0, yaws[0] / maxYaw);

                for (var i = 0; i < chainLength; i++)
                {
                    var time = (float)(i + 1) / chainLength;
                    pitchCurve.AddKey(time, pitches[i] / maxPitch);
                    yawCurve.AddKey(time, yaws[i] / maxYaw);
                }

                physBone.limitRotationXCurve = pitchCurve;
                physBone.limitRotationZCurve = yawCurve;
            }

            if (physBone.endpointPosition != Vector3.zero)
            {
                // TODO: this Endpoint Fix might not enough
                // Rotation fix will conflict with this fix
                physBone.endpointPosition = Quaternion.Euler(0, -fixedRollOfLastBone, 0) * physBone.endpointPosition;
            }
        }

        Transform RotateRecursive(VRCPhysBoneBase physBone,
            Transform transform,
            Transform parent,
            int depth,
            HashSet<Transform> ignoreTransforms,
            List<Transform> originalBones)
        {
            Vector3 targetLocation;

            var activeChildren = Enumerable.Range(0, transform.childCount)
                .Select(transform.GetChild)
                .Where(child => !ignoreTransforms.Contains(child))
                .ToArray();

            switch (activeChildren.Length)
            {
                case 0:
                    // end bone
                    if (physBone.endpointPosition != Vector3.zero)
                        targetLocation = physBone.endpointPosition;
                    else
                        targetLocation = Vector3.up;
                    break;
                case 1:
                    targetLocation = activeChildren[0].localPosition;
                    break;
                default:
                    switch (physBone.multiChildType)
                    {
                        case VRCPhysBoneBase.MultiChildType.Ignore:
                            targetLocation = Vector3.up;
                            break;
                        case VRCPhysBoneBase.MultiChildType.First:
                            targetLocation = activeChildren[0].localPosition;
                            break;
                        case VRCPhysBoneBase.MultiChildType.Average:
                            targetLocation =
                                activeChildren.Aggregate(Vector3.zero,
                                    (current, child) => current + child.localPosition) /
                                activeChildren.Length;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    break;
            }

            var specifiedRotation = physBone.CalcLimitRotation(physBone.CalcBoneRatio(depth));
            var rotation = ConvertRotation(specifiedRotation).y;

            // if the bone is at (0, -x, 0), we have infinite rotation for `FromToRotation` and
            // `Quaternion.FromToRotation`'s choice is not happy for logic below.
            // We need special handling for this case.
            var dot = Vector3.Dot(Vector3.up, math.normalizesafe(targetLocation));
            var critical = dot <= -1;

            //Debug.Log($"is critical: {critical}, dot: {dot}, transform: {transform.name}");
            var thisRotation = !critical ? rotation : -rotation;

            // create new (actual) bone
            var newBone = new GameObject($"{transform.name} (M4A C2ReducePB Proxy)");

            rollFixMap[transform] = newBone.transform;
            physBoneMap.Remove(transform);
            physBoneMap[newBone.transform] = physBone;

            // new bone should be at exactly same transform as the original bone
            newBone.transform.parent = transform;
            newBone.transform.localPosition = Vector3.zero;
            newBone.transform.localRotation = Quaternion.identity;
            newBone.transform.localScale = Vector3.one;

            // move to parent
            newBone.transform.SetParent(parent, true);

            // rotate newBone to fix roll
            newBone.transform.Rotate(Vector3.up, thisRotation, Space.Self);

            // move old bone to child of newBone
            transform.SetParent(newBone.transform, true);

            originalBones.Add(transform);

            //var rotationQuaternion = Quaternion.Euler(0, -thisRotation, 0);

            foreach (var child in activeChildren)
            {
                //child.localPosition = rotationQuaternion * child.localPosition;
                //child.localRotation = rotationQuaternion * child.localRotation;

                if (ignoreTransforms.Contains(child)) continue;
                RotateRecursive(physBone, child, newBone.transform, depth + 1, ignoreTransforms,
                    originalBones);
            }

            return newBone.transform;
        }

        static Vector3 ConvertRotation(Vector3 limitRotation)
        {
            // XYZ is the order used in VRCPhysBone
            var quat = quaternion.EulerXYZ(limitRotation * Mathf.Deg2Rad);
            return QuaternionToEulerXZY(quat) * Mathf.Rad2Deg;
        }

        static Vector3 QuaternionToEulerXZY(Quaternion q)
        {
            // Quaternion to Euler
            // https://qiita.com/aa_debdeb/items/abe90a9bd0b4809813da
            // YZX Order in the article. (XZY in Unity)
            // We use different perspective to represent same order of Euler order between Unity and the article.
            var sz = 2 * q.x * q.y + 2 * q.z * q.w;
            var unlocked = Mathf.Abs(sz) < 0.99999f;
            Debug.Log("unlocked: " + unlocked);
            return new Vector3(
                unlocked ? Mathf.Atan2(-(2 * q.y * q.z - 2 * q.x * q.w), 2 * q.w * q.w + 2 * q.y * q.y - 1) : 0,
                unlocked
                    ? Mathf.Atan2(-(2 * q.x * q.z - 2 * q.y * q.w), 2 * q.w * q.w + 2 * q.x * q.x - 1)
                    : Mathf.Atan2(2 * q.x * q.z + 2 * q.y * q.w, 2 * q.w * q.w + 2 * q.z * q.z - 1),
                Mathf.Asin(sz)
            );
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

            constraint.SolveInLocalSpace = solveInLocalSpace;
            constraint.Locked = false;
            constraint.IsActive = true;
            Reflections.TryBakeCurrentOffsetsRuntime(constraint, VRCConstraintBase.BakeOptions.BakeAll);
            constraint.Locked = true;

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
            public delegate void TryBakeCurrentOffsetsRuntimeType(VRCConstraintBase constraint,
                VRCConstraintBase.BakeOptions bakeOptions);

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