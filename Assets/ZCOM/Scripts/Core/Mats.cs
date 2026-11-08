using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>Material factory + cache. Works in edit mode (scene builder) and at runtime.</summary>
    public static class Mats
    {
        static Dictionary<string, Material> cache = new Dictionary<string, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cache = new Dictionary<string, Material>(); }

        static Shader Pick(string name, System.Func<ShaderBank, Shader> fromBank)
        {
            var bank = ShaderBank.I;
            if (bank != null)
            {
                var s = fromBank(bank);
                if (s != null) return s;
            }
            return Shader.Find(name);
        }

        public static Shader LitShader => Pick("Universal Render Pipeline/Lit", b => b.lit);
        public static Shader AdditiveShader => Pick("ZCOM/FxAdditive", b => b.fxAdditive);
        public static Shader AlphaShader => Pick("ZCOM/FxAlpha", b => b.fxAlpha);

        public static Color HDR(Color c, float intensity) => new Color(c.r * intensity, c.g * intensity, c.b * intensity, c.a);

        public static Material Lit(Color baseColor, float metallic = 0.1f, float smoothness = 0.45f, Color emission = default)
        {
            var m = new Material(LitShader);
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            // Emission is always enabled so hit-flashes (via property blocks) can light any part up.
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            // URP keeps _EMISSION only when an emissive GI flag is set (it re-derives keywords on import).
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        public static Material Additive(Texture tex, Color hdrColor, bool vertexColor = true)
        {
            var m = new Material(AdditiveShader);
            if (tex != null) m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", hdrColor);
            m.SetFloat("_VertexColor", vertexColor ? 1f : 0f);
            return m;
        }

        public static Material Alpha(Texture tex, Color color, bool vertexColor = true)
        {
            var m = new Material(AlphaShader);
            if (tex != null) m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", color);
            m.SetFloat("_VertexColor", vertexColor ? 1f : 0f);
            return m;
        }

        public static Material Cached(string key, System.Func<Material> make)
        {
            if (!cache.TryGetValue(key, out var m) || m == null)
            {
                m = make();
                m.name = key;
                cache[key] = m;
            }
            return m;
        }
    }

    /// <summary>Builds visual parts from Unity's built-in primitive meshes without colliders.</summary>
    public static class Prims
    {
        static readonly Dictionary<PrimitiveType, Mesh> meshes = new Dictionary<PrimitiveType, Mesh>();

        public static Mesh Mesh(PrimitiveType type)
        {
            if (meshes.TryGetValue(type, out var m) && m != null) return m;
            var go = GameObject.CreatePrimitive(type);
            m = go.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
            meshes[type] = m;
            return m;
        }

        public static GameObject Part(string name, PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale,
            Material mat, Vector3 euler = default, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = Mesh(type);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A capsule stretched between two local points (arms, legs, struts).</summary>
        public static GameObject Limb(string name, Transform parent, Vector3 a, Vector3 b, float thickness, Material mat)
        {
            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = b - a;
            float len = dir.magnitude;
            var go = Part(name, PrimitiveType.Capsule, parent, mid, new Vector3(thickness, len * 0.5f + thickness * 0.25f, thickness), mat);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            return go;
        }
    }

    public static class Spring
    {
        public static void Step(ref float x, ref float v, float target, float stiffness, float damping, float dt)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 120f)));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                v += (-(x - target) * stiffness - v * damping) * h;
                x += v * h;
            }
        }

        public static void Step(ref Vector3 x, ref Vector3 v, Vector3 target, float stiffness, float damping, float dt)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 120f)));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                v += (-(x - target) * stiffness - v * damping) * h;
                x += v * h;
            }
        }
    }

    public static class Ease
    {
        public static float OutCubic(float t) { t = Mathf.Clamp01(t); float u = 1f - t; return 1f - u * u * u; }
        public static float OutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
