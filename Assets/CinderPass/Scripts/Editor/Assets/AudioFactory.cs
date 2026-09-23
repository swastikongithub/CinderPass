using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CinderPass.EditorTools
{
    /// <summary>
    /// Synthesises the ambience and one-shot sounds the game needs (no external audio dependencies).
    /// Loops are crossfaded at the seam so they repeat without clicks.
    /// </summary>
    public static class AudioFactory
    {
        const int Rate = 44100;

        public static string ClipPath(string name) => $"{CinderPaths.Audio}/{name}.wav";

        public static void BuildAll()
        {
            var rng = new System.Random(5);
            Write("SFX_TyreGravel_Loop", Loop(2.5f, t => Gravel(t, rng)), loop: true);
            Write("SFX_Impact", Impact(0.7f, rng));
            Write("AMB_LavaRumble_Loop", Loop(7f, t => Rumble(t, rng)), loop: true);
            Write("AMB_Wind_Loop", Loop(9f, t => Wind(t, rng)), loop: true);
            Write("SFX_EruptionBoom", Boom(4.5f, rng));
            Write("SFX_LavaSizzle", Sizzle(1.8f, rng));
            CopyEngineClip();
        }

        static void CopyEngineClip()
        {
            // The only third-party audio used: the engine loop from the bundled Prometeo pack (copied so the
            // game has no dependency on that package).
            const string src = "Assets/PROMETEO - Car Controller/Sounds/CarEngine.wav";
            string dst = ClipPath("SFX_Engine_Loop");
            if (File.Exists(src) && !File.Exists(dst))
            {
                AssetUtil.EnsureFolder(CinderPaths.Audio);
                AssetDatabase.CopyAsset(src, dst);
            }
        }

        // ---------------------------------------------------------------- generators

        static float[] Loop(float seconds, Func<float, float> sample)
        {
            int n = (int)(seconds * Rate);
            int fade = Rate / 2;
            var buf = new float[n + fade];
            for (int i = 0; i < buf.Length; i++) buf[i] = sample((float)i / Rate);
            // Crossfade the tail into the head for a seamless loop.
            for (int i = 0; i < fade; i++)
            {
                float w = (float)i / fade;
                buf[i] = buf[i] * w + buf[n + i] * (1f - w);
            }
            var outBuf = new float[n];
            Array.Copy(buf, outBuf, n);
            Normalize(outBuf, 0.8f);
            return outBuf;
        }

        static float brown, lp1, lp2, hp;

        static float White(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

        static float Gravel(float t, System.Random r)
        {
            // Dense granular crunch: filtered noise with random grain envelopes.
            lp1 += (White(r) - lp1) * 0.35f;
            float grain = r.NextDouble() < 0.004 ? 1f : 0f;
            lp2 = lp2 * 0.992f + grain * (float)r.NextDouble();
            return lp1 * (0.35f + lp2 * 3f);
        }

        static float Rumble(float t, System.Random r)
        {
            brown = Mathf.Clamp(brown + White(r) * 0.02f, -1f, 1f) * 0.999f;
            lp1 += (brown - lp1) * 0.02f;
            float pop = r.NextDouble() < 0.0004 ? (float)r.NextDouble() : 0f;
            lp2 = lp2 * 0.9985f + pop;
            float bubble = Mathf.Sin(t * 2f * Mathf.PI * (60f + lp2 * 80f)) * lp2 * 0.6f;
            return lp1 * 3f + bubble + Mathf.Sin(t * 2f * Mathf.PI * 31f) * 0.08f;
        }

        static float Wind(float t, System.Random r)
        {
            lp1 += (White(r) - lp1) * 0.05f;
            hp = lp1 - lp2;
            lp2 += (lp1 - lp2) * 0.002f;
            float gust = 0.5f + 0.35f * Mathf.Sin(t * 0.7f) + 0.15f * Mathf.Sin(t * 1.9f + 1.3f);
            return hp * gust * 2.5f;
        }

        static float[] Impact(float seconds, System.Random r)
        {
            int n = (int)(seconds * Rate);
            var b = new float[n];
            float f = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                f += (White(r) - f) * 0.2f;
                float env = Mathf.Exp(-t * 14f);
                b[i] = (Mathf.Sin(t * 2f * Mathf.PI * (70f - t * 40f)) * 0.9f + f * 0.6f) * env;
            }
            Normalize(b, 0.9f);
            return b;
        }

        static float[] Boom(float seconds, System.Random r)
        {
            int n = (int)(seconds * Rate);
            var b = new float[n];
            float br = 0f, l = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                br = Mathf.Clamp(br + White(r) * 0.03f, -1f, 1f) * 0.9995f;
                l += (br - l) * 0.015f;
                float env = Mathf.Min(1f, t * 8f) * Mathf.Exp(-t * 0.9f);
                b[i] = (l * 4f + Mathf.Sin(t * 2f * Mathf.PI * 38f) * 0.4f * Mathf.Exp(-t * 2f)) * env;
            }
            Normalize(b, 0.95f);
            return b;
        }

        static float[] Sizzle(float seconds, System.Random r)
        {
            int n = (int)(seconds * Rate);
            var b = new float[n];
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float w = White(r);
                float high = w - prev;
                prev = w;
                float env = Mathf.Min(1f, t * 30f) * Mathf.Exp(-t * 1.8f);
                float crackle = r.NextDouble() < 0.01 ? White(r) * 2f : 0f;
                b[i] = (high * 0.5f + crackle) * env;
            }
            Normalize(b, 0.8f);
            return b;
        }

        static void Normalize(float[] b, float peak)
        {
            float max = 1e-5f;
            foreach (var s in b) max = Mathf.Max(max, Mathf.Abs(s));
            float k = peak / max;
            for (int i = 0; i < b.Length; i++) b[i] *= k;
        }

        // ---------------------------------------------------------------- WAV writer

        static void Write(string name, float[] samples, bool loop = false)
        {
            string path = ClipPath(name);
            AssetUtil.EnsureFolder(CinderPaths.Audio);
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                int dataBytes = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(Rate);
                w.Write(Rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(dataBytes);
                foreach (var s in samples) w.Write((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = imp.defaultSampleSettings;
            settings.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.6f;
            imp.defaultSampleSettings = settings;
            imp.forceToMono = true;
            imp.SaveAndReimport();
        }
    }
}
