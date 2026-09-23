using UnityEditor;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Sets serialized (including private [SerializeField]) fields from tooling, exactly as the Inspector
    /// would, so runtime components keep private fields and explicit references instead of Find() calls.
    /// </summary>
    public static class Wiring
    {
        public static void Set(Object target, string path, object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(path);
            if (p == null)
            {
                Debug.LogError($"[Wiring] {target.GetType().Name} has no serialized field '{path}'.", target);
                return;
            }
            Assign(p, value);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void Assign(SerializedProperty p, object value)
        {
            switch (value)
            {
                case null: p.objectReferenceValue = null; break;
                case Object o: p.objectReferenceValue = o; break;
                case float f: p.floatValue = f; break;
                case int i when p.propertyType == SerializedPropertyType.Enum: p.enumValueIndex = i; break;
                case int i when p.propertyType == SerializedPropertyType.LayerMask: p.intValue = i; break;
                case int i: p.intValue = i; break;
                case bool b: p.boolValue = b; break;
                case string s: p.stringValue = s; break;
                case Vector2 v2: p.vector2Value = v2; break;
                case Vector3 v3: p.vector3Value = v3; break;
                case Color c: p.colorValue = c; break;
                case LayerMask m: p.intValue = m.value; break;
                case System.Enum e: p.enumValueIndex = System.Convert.ToInt32(e); break;
                default: Debug.LogError($"[Wiring] Unsupported value type {value.GetType()} for {p.propertyPath}"); break;
            }
        }

        /// <summary>Sets an array of object references.</summary>
        public static void SetArray<T>(Object target, string path, T[] values) where T : Object
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(path);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
