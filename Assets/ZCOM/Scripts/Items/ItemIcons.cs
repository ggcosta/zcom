using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Renders each item's 3D model once into a sprite for the inventory (off-screen camera, chroma-keyed background).
    /// </summary>
    public static class ItemIcons
    {
        const int Layer = 31;
        const int Size = 288;
        static readonly Vector3 StagePos = new Vector3(0f, -2000f, 0f);
        static readonly Color Key = new Color(1f, 0f, 1f, 1f);

        static Dictionary<ItemId, Sprite> cache = new Dictionary<ItemId, Sprite>();
        static Camera cam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cache = new Dictionary<ItemId, Sprite>(); cam = null; }

        public static Sprite Get(ItemId id)
        {
            if (id == ItemId.None) return null;
            if (cache.TryGetValue(id, out var s) && s) return s;
            var def = Items.Get(id);
            s = !string.IsNullOrEmpty(def.icon) ? Resources.Load<Sprite>("UI/" + def.icon) : null;
            if (!s) s = Render(id);
            cache[id] = s;
            return s;
        }

        static void EnsureStage()
        {
            if (cam) return;
            var go = new GameObject("ItemIconCamera");
            Object.DontDestroyOnLoad(go);
            cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Key;
            cam.cullingMask = 1 << Layer;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            go.transform.SetPositionAndRotation(StagePos + new Vector3(0f, 0f, -5f), Quaternion.identity);

            var key = new GameObject("IconKeyLight").AddComponent<Light>();
            Object.DontDestroyOnLoad(key.gameObject);
            key.type = LightType.Directional;
            key.cullingMask = 1 << Layer;
            key.intensity = 1.4f;
            key.color = new Color(0.9f, 0.95f, 1f);
            key.shadows = LightShadows.None;
            key.transform.rotation = Quaternion.Euler(35f, 25f, 0f);
            // cool rim from behind and a soft fill from below so the silhouettes read on dark slots
            foreach (var (rot, col, inten) in new[] { (Quaternion.Euler(-20f, 200f, 0f), new Color(0.45f, 0.8f, 1f), 1.1f), (Quaternion.Euler(-50f, -40f, 0f), new Color(1f, 0.9f, 0.8f), 0.45f) })
            {
                var l = new GameObject("IconFillLight").AddComponent<Light>();
                Object.DontDestroyOnLoad(l.gameObject);
                l.type = LightType.Directional;
                l.cullingMask = 1 << Layer;
                l.intensity = inten;
                l.color = col;
                l.shadows = LightShadows.None;
                l.transform.rotation = rot;
            }
        }

        static Sprite Render(ItemId id)
        {
            EnsureStage();
            var subject = new GameObject("IconSubject").transform;
            subject.position = StagePos;
            ItemVisuals.Build(id, subject);
            bool weapon = Items.IsWeapon(id);
            subject.rotation = weapon ? Quaternion.Euler(-8f, 90f, 6f) : Quaternion.Euler(-15f, 25f, 0f);

            var rends = subject.GetComponentsInChildren<Renderer>();
            Bounds b = new Bounds(StagePos, Vector3.zero);
            bool first = true;
            foreach (var r in rends)
            {
                r.gameObject.layer = Layer;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            cam.transform.position = new Vector3(b.center.x, b.center.y, StagePos.z - 5f);
            int w = weapon ? Size * 3 : Size, h = weapon ? Size : Size;
            float aspect = w / (float)h;
            cam.aspect = aspect;
            cam.orthographicSize = Mathf.Max(b.extents.y, b.extents.x / aspect) * 1.08f + 0.02f;

            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = $"Icon_{id}", filterMode = FilterMode.Bilinear };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);

            // Chroma-key the background out.
            var px = tex.GetPixels32();
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int i = 0; i < px.Length; i++)
            {
                var p = px[i];
                if (p.r > 200 && p.g < 60 && p.b > 200) { px[i] = new Color32(0, 0, 0, 0); continue; }
                int x = i % w, y = i / w;
                if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            // the sprite is the drawn object only (the model's bounds include invisible helpers), so it fills its slot
            Rect content = x1 >= x0 ? new Rect(Mathf.Max(0, x0 - 2), Mathf.Max(0, y0 - 2), Mathf.Min(w, x1 + 3) - Mathf.Max(0, x0 - 2), Mathf.Min(h, y1 + 3) - Mathf.Max(0, y0 - 2)) : new Rect(0, 0, w, h);

            subject.gameObject.SetActive(false);
            Object.Destroy(subject.gameObject);
            return Sprite.Create(tex, content, new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
