using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Anatawa12.Modifier4Avatar.Editor
{
    internal abstract class DeepCloner
    {
        private readonly Dictionary<Object, Object> _cache = new Dictionary<Object, Object>();
        private bool _mapped;

        public T MapObject<T>(T obj) where T : Object =>
            DeepClone(obj);

        protected abstract Object CustomClone(Object o);
        
        protected void MarkMapped() => _mapped = true;

        private readonly struct MappedScope : IDisposable
        {
            private readonly DeepCloner _mapper;
            private readonly bool _previous;

            public MappedScope(DeepCloner mapper)
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
        public T DeepClone<T>(T original) where T : Object
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

            T obj;
            
            using (new MappedScope(this))
            {
                obj = (T)CustomClone(original);
                if (obj != null && !_mapped) obj = original;
            }

            if (obj == null)
            {
                using (new MappedScope(this))
                {
                    obj = DefaultDeepClone(original);
                    if (!_mapped) obj = original;
                }
            }

            _cache[original] = obj;
            _cache[obj] = obj;
            return obj;
        }

        protected T DefaultDeepClone<T>(T original) where T : Object
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
                    prop.objectReferenceValue = DeepClone(prop.objectReferenceValue);

                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return (T)obj;
        }
    }
}