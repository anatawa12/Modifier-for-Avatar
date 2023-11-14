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
    
    internal class AnimatorControllerMapper
    {
        private readonly AnimationObjectMapper _mapping;
        private readonly Dictionary<Object, Object> _cache = new Dictionary<Object, Object>();
        private bool _mapped;

        public AnimatorControllerMapper(AnimationObjectMapper mapping)
        {
            _mapping = mapping;
        }

        public T MapObject<T>(T obj) where T : Object =>
            DeepClone(obj, CustomClone);

        // https://github.com/bdunderscore/modular-avatar/blob/db49e2e210bc070671af963ff89df853ae4514a5/Packages/nadena.dev.modular-avatar/Editor/AnimatorMerger.cs#L199-L241
        // Originally under MIT License
        // Copyright (c) 2022 bd_
        private Object CustomClone(Object o)
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
                    newClip.SetCurve(_mapping.MapPath(binding.path), binding.type, binding.propertyName,
                        AnimationUtility.GetEditorCurve(clip, binding));
                }

                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    var newBinding = binding;
                    newBinding.path = _mapping.MapPath(binding.path);
                    AnimationUtility.SetObjectReferenceCurve(newClip, newBinding,
                        AnimationUtility.GetObjectReferenceCurve(clip, binding));
                }

                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (newClip.length != clip.length)
                {
                    // if newClip has less properties than original clip (especially for no properties), 
                    // length of newClip can be changed which is bad.
                    newClip.SetCurve(
                        "$AvatarOptimizerClipLengthDummy$", typeof(GameObject), "m_IsActive",
                        AnimationCurve.Constant(clip.length, clip.length, 1f));
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
                    if (path != newPath) _mapped = true;
                }
                newMask.transformCount = dstI;

                return newMask;
            }
            else if (o is RuntimeAnimatorController controller)
            {
                using (new MappedScope(this))
                {
                    var newController = DefaultDeepClone(controller, CustomClone);
                    newController.name = controller.name + " (rebased)";
                    if (!_mapped) newController = controller;
                    _cache[controller] = newController;
                    return newController;
                }
            }
            else
            {
                return null;
            }
        }

        private readonly struct MappedScope : IDisposable
        {
            private readonly AnimatorControllerMapper _mapper;
            private readonly bool _previous;

            public MappedScope(AnimatorControllerMapper mapper)
            {
                _mapper = mapper;
                _previous = mapper._mapped;
                mapper._mapped = false;
            }

            public void Dispose()
            {
                _mapper._mapped |= _previous;
            }
        }

        // https://github.com/bdunderscore/modular-avatar/blob/db49e2e210bc070671af963ff89df853ae4514a5/Packages/nadena.dev.modular-avatar/Editor/AnimatorMerger.cs#LL242-L340C10
        // Originally under MIT License
        // Copyright (c) 2022 bd_
        private T DeepClone<T>(T original, Func<Object, Object> visitor) where T : Object
        {
            if (original == null) return null;

            // We want to avoid trying to copy assets not part of the animation system (eg - textures, meshes,
            // MonoScripts...), so check for the types we care about here
            switch (original)
            {
                // Any object referenced by an animator that we intend to mutate needs to be listed here.
                case Motion _:
                case AnimatorController _:
                case AnimatorOverrideController _:
                case AnimatorState _:
                case AnimatorStateMachine _:
                case AnimatorTransitionBase _:
                case StateMachineBehaviour _:
                case AvatarMask _:
                    break; // We want to clone these types

                // Leave textures, materials, and script definitions alone
                case Texture _:
                case MonoScript _:
                case Material _:
                case GameObject _:
                    return original;

                // Also avoid copying unknown scriptable objects.
                // This ensures compatibility with e.g. avatar remote, which stores state information in a state
                // behaviour referencing a custom ScriptableObject
                case ScriptableObject _:
                    return original;

                default:
                    throw new Exception($"Unknown type referenced from animator: {original.GetType()}");
            }

            if (_cache.TryGetValue(original, out var cached)) return (T)cached;

            var obj = visitor(original);
            if (obj != null)
            {
                _cache[original] = obj;
                _cache[obj] = obj;
                return (T)obj;
            }

            return DefaultDeepClone(original, visitor);
        }

        private T DefaultDeepClone<T>(T original, Func<Object, Object> visitor) where T : Object
        {
            Object obj;
            var ctor = original.GetType().GetConstructor(Type.EmptyTypes);
            if (ctor == null || original is ScriptableObject)
            {
                obj = Object.Instantiate(original);
            }
            else
            {
                obj = (T)ctor.Invoke(Array.Empty<object>());
                EditorUtility.CopySerialized(original, obj);
            }

            _cache[original] = obj;
            _cache[obj] = obj;

            using (var so = new SerializedObject(obj))
            {
                foreach (var prop in so.ObjectReferenceProperties())
                    prop.objectReferenceValue = DeepClone(prop.objectReferenceValue, visitor);

                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return (T)obj;
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