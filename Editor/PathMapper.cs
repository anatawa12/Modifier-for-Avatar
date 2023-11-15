// https://github.com/anatawa12/AvatarOptimizer/tree/0f67c62b14dc747433e9d75d8134ee1029404bf4/Editor/ObjectMapping
// Originally under MIT License
// Copyright (c) 2022 anatawa12
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using nadena.dev.ndmf;
using nadena.dev.ndmf.runtime;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Anatawa12.Modifier4Avatar.Editor
{
    class PathMapperContext : IExtensionContext
    {
        private PathMapper _mapper;
        public void OnActivate(BuildContext context)
        {
            _mapper = new PathMapper(context.AvatarRootObject);
        }

        public void OnDeactivate(BuildContext context)
        {
            // replace all objects
            foreach (var component in context.AvatarRootObject.GetComponentsInChildren<Component>(true))
            {
                if (component is Transform) return;
                
                // apply special mapping
                var serialized = new SerializedObject(component);
                AnimatorControllerMapper mapper = null;

                foreach (var p in serialized.ObjectReferenceProperties())
                {
                    var objectReferenceValue = p.objectReferenceValue;
                    switch (objectReferenceValue)
                    {
                        case RuntimeAnimatorController _:
                            if (mapper == null)
                                mapper = new AnimatorControllerMapper(_mapper.CreateAnimationMapper(component.gameObject));

                            // ReSharper disable once AccessToModifiedClosure
                            var mapped = mapper.MapObject(objectReferenceValue);
                            if (mapped != objectReferenceValue)
                                p.objectReferenceValue = mapped;
                            break;
                    }
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    /// <summary>
    /// The class manages Object location mapping
    /// </summary>
    internal class PathMapper
    {
        private readonly IReadOnlyDictionary<GameObject, BeforeGameObjectTree> _beforeGameObjectInfos;

        public PathMapper([NotNull] GameObject rootObject)
        {
            if (!rootObject) throw new ArgumentNullException(nameof(rootObject));
            var transforms = rootObject.GetComponentsInChildren<Transform>(true);

            _beforeGameObjectInfos = transforms
                .ToDictionary(t => t.gameObject, t => new BeforeGameObjectTree(t.gameObject));

            foreach (var transform in transforms)
            {
                if (!transform.parent) continue;
                if (!_beforeGameObjectInfos.TryGetValue(transform.parent.gameObject, out var parentInfo)) continue;
                var selfInfo = _beforeGameObjectInfos[transform.gameObject];
                parentInfo.Children[transform.GetSiblingIndex()] = selfInfo;
            }

#if DEBUG
            // assertion
            foreach (var info in _beforeGameObjectInfos.Values)
                System.Diagnostics.Debug.Assert(info.Children.All(x => x != null), "info.Children.All(x => x != null)");
#endif
        }

        [CanBeNull]
        public AnimationObjectMapper CreateAnimationMapper(GameObject rootGameObject) =>
            _beforeGameObjectInfos.TryGetValue(rootGameObject, out var beforeTree)
                ? new AnimationObjectMapper(rootGameObject, beforeTree)
                : null;
    }

    class BeforeGameObjectTree
    {
        public readonly GameObject Instance;
        [NotNull] public readonly string Name;
        [NotNull] public readonly BeforeGameObjectTree[] Children;

        public BeforeGameObjectTree(GameObject gameObject)
        {
            Instance = gameObject;
            Name = gameObject.name;
            Children = new BeforeGameObjectTree[gameObject.transform.childCount];
        }
    }

    internal class AnimationObjectMapper
    {
        readonly GameObject _rootGameObject;
        readonly BeforeGameObjectTree _beforeGameObjectTree;

        private readonly Dictionary<string, string> _pathsCache =
            new Dictionary<string, string>();

        public AnimationObjectMapper(GameObject rootGameObject, BeforeGameObjectTree beforeGameObjectTree)
        {
            _rootGameObject = rootGameObject;
            _beforeGameObjectTree = beforeGameObjectTree;
        }

        // null means nothing to map
        [CanBeNull]
        public string MapPath(string path)
        {
            if (path == "") return "";
            if (_pathsCache.TryGetValue(path, out var info)) return info;

            var tree = _beforeGameObjectTree;

            foreach (var pathSegment in path.Split('/'))
            {
                tree = tree.Children.FirstOrDefault(x => x.Name == pathSegment);
                if (tree == null) break;
            }

            if (tree == null)
            {
                _pathsCache.Add(path, null);
                return null;
            }

            var foundGameObject = tree.Instance;
            var newPath = foundGameObject ? RuntimeUtil.RelativePath(_rootGameObject, foundGameObject) : null;

            info = newPath;
            _pathsCache.Add(path, info);
            return info;
        }
    }
    
    internal class AnimatorControllerMapper : DeepCloner
    {
        private readonly AnimationObjectMapper _mapping;

        public AnimatorControllerMapper(AnimationObjectMapper mapping)
        {
            _mapping = mapping;
        }

        // https://github.com/bdunderscore/modular-avatar/blob/db49e2e210bc070671af963ff89df853ae4514a5/Packages/nadena.dev.modular-avatar/Editor/AnimatorMerger.cs#L199-L241
        // Originally under MIT License
        // Copyright (c) 2022 bd_
        protected override Object CustomClone(Object o)
        {
            if (o is AnimationClip clip)
            {
                if (clip.IsProxy()) return clip;
                var newClip = new AnimationClip();
                newClip.name = "rebased " + clip.name;

                // copy m_UseHighQualityCurve with SerializedObject since m_UseHighQualityCurve doesn't have public API
                using (var serializedClip = new SerializedObject(clip))
                using (var serializedNewClip = new SerializedObject(newClip))
                {
                    serializedNewClip.FindProperty("m_UseHighQualityCurve")
                        .boolValue = serializedClip.FindProperty("m_UseHighQualityCurve").boolValue;
                    serializedNewClip.ApplyModifiedPropertiesWithoutUndo();
                }

                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var newPath = _mapping.MapPath(binding.path);
                    if (newPath != binding.path) MarkMapped();
                    newClip.SetCurve(newPath, binding.type, binding.propertyName,
                        AnimationUtility.GetEditorCurve(clip, binding));
                }

                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    var newBinding = binding;
                    newBinding.path = _mapping.MapPath(binding.path);
                    if (newBinding.path != binding.path) MarkMapped();
                    AnimationUtility.SetObjectReferenceCurve(newClip, newBinding,
                        AnimationUtility.GetObjectReferenceCurve(clip, binding));
                }

                newClip.wrapMode = clip.wrapMode;
                newClip.legacy = clip.legacy;
                newClip.frameRate = clip.frameRate;
                newClip.localBounds = clip.localBounds;
                AnimationUtility.SetAnimationClipSettings(newClip, AnimationUtility.GetAnimationClipSettings(clip)); 
                return newClip;
            }
            else if (o is AvatarMask mask)
            {
                var newMask = new AvatarMask();
                for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; ++part)
                    newMask.SetHumanoidBodyPartActive(part, mask.GetHumanoidBodyPartActive(part));
                newMask.name = "rebased " + mask.name;
                newMask.transformCount = mask.transformCount;
                var dstI = 0;
                for (var srcI = 0; srcI < mask.transformCount; srcI++)
                {
                    var path = mask.GetTransformPath(srcI);
                    var newPath = _mapping.MapPath(path);
                    if (newPath != null)
                    {
                        newMask.SetTransformPath(dstI, newPath);
                        newMask.SetTransformActive(dstI, mask.GetTransformActive(srcI));
                        dstI++;
                    }

                    if (path != newPath) MarkMapped();
                }
                newMask.transformCount = dstI;

                return newMask;
            }
            else
            {
                return null;
            }
        }
    }

    internal static class Utils
    {
        public static ObjectReferencePropertiesEnumerable ObjectReferenceProperties(this SerializedObject obj)
            => new ObjectReferencePropertiesEnumerable(obj);

        public readonly struct ObjectReferencePropertiesEnumerable : IEnumerable<SerializedProperty>
        {
            private readonly SerializedObject _obj;

            public ObjectReferencePropertiesEnumerable(SerializedObject obj) => _obj = obj;

            public Enumerator GetEnumerator() => new Enumerator(_obj);
            IEnumerator<SerializedProperty> IEnumerable<SerializedProperty>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public struct Enumerator : IEnumerator<SerializedProperty>
            {
                private readonly SerializedProperty _iterator;

                public Enumerator(SerializedObject obj) => _iterator = obj.GetIterator();

                public bool MoveNext()
                {
                    while (true)
                    {
                        bool enterChildren;
                        switch (_iterator.propertyType)
                        {
                            case SerializedPropertyType.String:
                            case SerializedPropertyType.Integer:
                            case SerializedPropertyType.Boolean:
                            case SerializedPropertyType.Float:
                            case SerializedPropertyType.Color:
                            case SerializedPropertyType.ObjectReference:
                            case SerializedPropertyType.LayerMask:
                            case SerializedPropertyType.Enum:
                            case SerializedPropertyType.Vector2:
                            case SerializedPropertyType.Vector3:
                            case SerializedPropertyType.Vector4:
                            case SerializedPropertyType.Rect:
                            case SerializedPropertyType.ArraySize:
                            case SerializedPropertyType.Character:
                            case SerializedPropertyType.AnimationCurve:
                            case SerializedPropertyType.Bounds:
                            case SerializedPropertyType.Gradient:
                            case SerializedPropertyType.Quaternion:
                            case SerializedPropertyType.FixedBufferSize:
                            case SerializedPropertyType.Vector2Int:
                            case SerializedPropertyType.Vector3Int:
                            case SerializedPropertyType.RectInt:
                            case SerializedPropertyType.BoundsInt:
                                enterChildren = false;
                                break;
                            case SerializedPropertyType.Generic:
                            case SerializedPropertyType.ExposedReference:
                            case SerializedPropertyType.ManagedReference:
                            default:
                                enterChildren = true;
                                break;
                        }

                        if (!_iterator.Next(enterChildren)) return false;
                        if (_iterator.propertyType == SerializedPropertyType.ObjectReference)
                            return true;
                    }
                }

                public void Reset()
                {
                    var obj = _iterator.serializedObject;
                    Dispose();
                    this = new Enumerator(obj);
                }

                public SerializedProperty Current => _iterator;
                object IEnumerator.Current => Current;

                public void Dispose()
                {
                }
            }
        }
        
        // https://creators.vrchat.com/avatars/#proxy-animations
        public static bool IsProxy(this AnimationClip clip) => clip.name.StartsWith("proxy_", StringComparison.Ordinal);
    }
}