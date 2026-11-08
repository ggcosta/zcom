using UnityEngine;
using UnityEngine.UI;

namespace Zcom
{
    /// <summary>Procedural sprites for the holographic visor HUD (hexes, arcs, radar sweep, scanlines, visor frame).</summary>
    public static class HoloKit
    {
        static Sprite skewFrame, hex, hexFrame, arc, sweep, crescent, visor, warn, ringTicks, softDot;
        static Texture2D scan, hexTile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { skewFrame = hex = hexFrame = arc = sweep = crescent = visor = warn = ringTicks = softDot = null; scan = hexTile = null; }

        delegate float AlphaFn(float x, float y);

        static Texture2D Tex(int w, int h, AlphaFn f, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = wrap, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(f(x + 0.5f, y + 0.5f)) * 255f));
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        }

        static Sprite Spr(Texture2D t, float border = 0f) =>
            Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));

        /// <summary>Signed distance (px, + inside) to a flat-topped hexagon of circumradius r centred in the texture.</summary>
        static float HexDist(float x, float y, float c, float r)
        {
            float px = Mathf.Abs(x - c), py = Mathf.Abs(y - c);
            float a = r * 0.8660254f;                       // apothem
            float d1 = a - py;
            float d2 = a - (px * 0.8660254f + py * 0.5f);
            return Mathf.Min(d1, d2);
        }

        /// <summary>Outline of the UiKit.Skew parallelogram (same slant), for framing slanted panels.</summary>
        public static Sprite SkewFrame => skewFrame ? skewFrame : skewFrame = Spr(Tex(190, 210, (x, y) =>
        {
            float s = y / 210f * 38f;                           // UiKit.Skew slant: 8 px over 40 px width
            float d = Mathf.Min(Mathf.Min(x - s, (190f - 38f + s) - x) * 0.98f, Mathf.Min(y, 210f - y));
            return Mathf.Clamp01(d + 0.5f) * Mathf.Clamp01(2.6f - d);
        }));

        public static Sprite Hex => hex ? hex : hex = Spr(Tex(128, 128, (x, y) => HexDist(x, y, 64, 62) + 0.5f));
        public static Sprite HexFrame => hexFrame ? hexFrame : hexFrame = Spr(Tex(128, 128, (x, y) =>
        {
            float d = HexDist(x, y, 64, 62);
            return Mathf.Clamp01(d + 0.5f) * Mathf.Clamp01(3.2f - d);
        }));

        /// <summary>Thick ring (radius 58, width 8 of a 128 texture) - use with Image.Type.Filled / Radial360 for arc gauges.</summary>
        public static Sprite Arc => arc ? arc : arc = Spr(Tex(128, 128, (x, y) =>
        {
            float r = Mathf.Sqrt((x - 64) * (x - 64) + (y - 64) * (y - 64));
            return Mathf.Clamp01(4.5f - Mathf.Abs(r - 58f));
        }));

        /// <summary>Thin ring with tick marks every 30 degrees (radar range rings).</summary>
        public static Sprite RingTicks => ringTicks ? ringTicks : ringTicks = Spr(Tex(256, 256, (x, y) =>
        {
            float dx = x - 128, dy = y - 128;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float ring = Mathf.Clamp01(1.3f - Mathf.Abs(r - 124f));
            float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            float m = Mathf.Abs(Mathf.DeltaAngle(ang, Mathf.Round(ang / 30f) * 30f));
            float tick = r > 112f && r < 125f && m < 0.9f ? 1f : 0f;
            return Mathf.Max(ring, tick);
        }));

        /// <summary>Radar sweep: a 70 degree conical gradient trailing behind +Y (rotate the image to sweep).</summary>
        public static Sprite Sweep => sweep ? sweep : sweep = Spr(Tex(256, 256, (x, y) =>
        {
            float dx = x - 128, dy = y - 128;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            if (r > 124f) return 0f;
            float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;      // 0 = up, + clockwise
            if (ang > 0f || ang < -70f) return ang > 0f && ang < 1.5f ? 0.9f : 0f;
            float k = 1f + ang / 70f;
            return k * k * 0.55f * Mathf.Clamp01(r / 20f);
        }));

        /// <summary>Soft crescent (damage direction indicator), points up.</summary>
        public static Sprite Crescent => crescent ? crescent : crescent = Spr(Tex(256, 96, (x, y) =>
        {
            float dx = (x - 128f) / 128f, dy = (y + 260f) / 96f;
            float r = Mathf.Sqrt(dx * dx * 9f + dy * dy);
            float band = 1f - Mathf.Abs(r - 3.35f) / 0.45f;
            float side = 1f - Mathf.Abs(dx);
            return Mathf.Clamp01(band) * Mathf.Clamp01(side * 1.6f);
        }));

        /// <summary>Visor frame: thin rounded border with heavier corner arcs (9-sliced, stretch over the screen).</summary>
        public static Sprite Visor => visor ? visor : visor = Spr(Tex(256, 256, (x, y) =>
        {
            const float R = 90f, inset = 10f;
            float cx = Mathf.Clamp(x, inset + R, 256 - inset - R), cy = Mathf.Clamp(y, inset + R, 256 - inset - R);
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            bool corner = (x < inset + R || x > 256 - inset - R) && (y < inset + R || y > 256 - inset - R);
            float line = Mathf.Clamp01(1.2f - Mathf.Abs(d - R));
            float heavy = corner ? Mathf.Clamp01(3.2f - Mathf.Abs(d - R + 5f)) * 0.9f : 0f;
            float outside = d > R ? Mathf.Clamp01((d - R) / 10f) * 0.55f : 0f;   // darken beyond the visor edge
            return Mathf.Max(Mathf.Max(line, heavy), outside);
        }), 120f);

        /// <summary>Warning triangle with an exclamation mark.</summary>
        public static Sprite Warn => warn ? warn : warn = Spr(Tex(64, 64, (x, y) =>
        {
            float u = x - 32f, v = y - 6f;
            float edge = Mathf.Min(v, (52f - v) * 0.5f - Mathf.Abs(u) * 0.87f);
            float tri = Mathf.Clamp01(edge + 0.5f) * Mathf.Clamp01(5f - edge);
            float bar = Mathf.Abs(u) < 3f && v > 18f && v < 38f ? 1f : 0f;
            float dot = (u * u + (v - 12f) * (v - 12f)) < 10f ? 1f : 0f;
            return Mathf.Max(tri, Mathf.Max(bar, dot));
        }));

        public static Sprite SoftDot => softDot ? softDot : softDot = Spr(Tex(64, 64, (x, y) =>
        {
            float r = Mathf.Sqrt((x - 32) * (x - 32) + (y - 32) * (y - 32)) / 32f;
            return Mathf.Pow(Mathf.Clamp01(1f - r), 2f);
        }));

        /// <summary>Tileable scanlines (2 bright rows in 6).</summary>
        public static Texture2D Scanlines => scan ? scan : scan = Tex(4, 6, (x, y) => y < 2f ? 1f : 0f, TextureWrapMode.Repeat);

        /// <summary>Tileable hexagon grid outline.</summary>
        public static Texture2D HexTile => hexTile ? hexTile : hexTile = Tex(52, 90, (x, y) =>
        {
            float best = 1e9f;
            foreach (var c in new[] { new Vector2(0, 0), new Vector2(52, 0), new Vector2(26, 45), new Vector2(0, 90), new Vector2(52, 90) })
            {
                float px = Mathf.Abs(x - c.x), py = Mathf.Abs(y - c.y);
                float d = Mathf.Max(px * 0.8660254f + py * 0.5f, py) ;
                best = Mathf.Min(best, Mathf.Abs(d - 26f));
            }
            return Mathf.Clamp01(1.2f - best);
        }, TextureWrapMode.Repeat);

        // ------------------------------------------------------------------ builders

        /// <summary>An arc gauge: a ring Image filled radially, rotated so the fill starts at startDeg (0 = up, clockwise).</summary>
        public static Image ArcImage(string name, Transform parent, float diameter, Color color, float startDeg, bool clockwise = true)
        {
            var img = UiKit.Img(name, parent, Arc, color, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(diameter, diameter));
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillClockwise = clockwise;
            img.rectTransform.localRotation = Quaternion.Euler(0, 0, -startDeg);
            return img;
        }

        public static RawImage Tiled(string name, Transform parent, Texture2D tex, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var ri = go.AddComponent<RawImage>();
            ri.texture = tex;
            ri.color = color;
            ri.raycastTarget = false;
            return ri;
        }
    }
}
