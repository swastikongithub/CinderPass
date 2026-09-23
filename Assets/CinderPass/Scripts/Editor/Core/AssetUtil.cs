using System.IO;
using UnityEditor;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>Idempotent asset helpers: every generator can be re-run without creating duplicates.</summary>
    public static class AssetUtil
    {
        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>
        /// Creates or overwrites an asset in place, preserving its GUID (so existing references stay valid).
        /// </summary>
        public static T SaveAsset<T>(T asset, string path) where T : Object
        {
            EnsureFolder(Path.GetDirectoryName(path));
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                return asset;
            }
            if (existing == asset) { EditorUtility.SetDirty(existing); return existing; }
            if (asset is Mesh srcMesh && existing is Mesh dstMesh)
            {
                dstMesh.Clear();
                EditorUtility.CopySerialized(srcMesh, dstMesh);
            }
            else
            {
                EditorUtility.CopySerialized(asset, existing);
            }
            existing.name = Path.GetFileNameWithoutExtension(path);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        public static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            EnsureFolder(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        /// <summary>Returns the material at path (creating it with the shader if missing) and resets its shader.</summary>
        public static Material Material(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                EnsureFolder(Path.GetDirectoryName(path));
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }
            m.name = Path.GetFileNameWithoutExtension(path);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Shader FindShader(string name)
        {
            var s = Shader.Find(name);
            if (s == null) throw new System.InvalidOperationException($"Shader '{name}' not found. Did it compile?");
            return s;
        }

        /// <summary>Writes a PNG into the project and returns the imported texture.</summary>
        public static Texture2D WritePng(Texture2D tex, string path, bool linear, bool normalMap = false, int maxSize = 2048, bool alphaIsTransparency = false, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            EnsureFolder(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !linear && !normalMap;
            imp.maxTextureSize = maxSize;
            imp.alphaIsTransparency = alphaIsTransparency;
            imp.wrapMode = wrap;
            imp.mipmapEnabled = true;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.anisoLevel = 4;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Loads an image file (jpg/png) directly into a readable linear/sRGB-agnostic texture.</summary>
        public static Texture2D LoadRaw(string path)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            t.LoadImage(File.ReadAllBytes(path));
            return t;
        }

        /// <summary>Reads any imported texture (including EXR) as float pixels via a temporary readable copy.</summary>
        public static Color[] ReadPixels(string assetPath, int width, int height)
        {
            var src = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
            if (src == null) return null;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var t = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            t.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels();
            Object.DestroyImmediate(t);
            return px;
        }

        /// <summary>
        /// Returns the component, adding it if missing. (Never use "GetComponent() ?? AddComponent()": in the
        /// editor a missing component is a fake-null object that the ?? operator treats as non-null.)
        /// </summary>
        public static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        public static void DestroyChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Object.DestroyImmediate(t.GetChild(i).gameObject);
        }

        public static GameObject Child(Transform parent, string name)
        {
            var existing = parent != null ? parent.Find(name) : null;
            if (existing != null) return existing.gameObject;
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }
    }
}
