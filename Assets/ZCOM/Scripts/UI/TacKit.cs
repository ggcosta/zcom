using UnityEngine;
using UnityEngine.UI;

namespace Zcom
{
    /// <summary>
    /// "Tac-link" visor look shared by the HUD and the inventory: notched glass plates, cut-corner slots, hatching,
    /// tick rulers, thin corner brackets, plus the dynamic bits - text decode, visor curvature (mesh warp) and a
    /// one-shot scan sweep.
    /// </summary>
    public static class TacKit
    {
        public static readonly Color Cyan = new Color(0.36f, 0.9f, 1f);
        public static readonly Color Ice = new Color(0.86f, 0.95f, 1f);
        public static readonly Color Glass = new Color(0.012f, 0.03f, 0.05f, 0.72f);
        public static readonly Color GlassHi = new Color(0.03f, 0.08f, 0.11f, 0.85f);
        public static readonly Color Micro = new Color(0.55f, 0.75f, 0.85f, 0.45f);
        public static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>Wide display face for names, heavy face for numerals, semi-bold for captions.</summary>
        public static Font Display => UiKit.F("Michroma-Regular");
        public static Font Num => UiKit.F("ChakraPetch-Bold");
        public static Font Cap => UiKit.F("ChakraPetch-SemiBold");

        static Sprite plateGlow, thinArc, plate, plateFrame, slot, slotFrame, slotGlow, hatchSpr, corner, chevron, tab, gradUp, tickBar, pip;
        static Texture2D hatch, ruler, noise;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            plateGlow = thinArc = plate = plateFrame = slot = slotFrame = slotGlow = hatchSpr = corner = chevron = tab = gradUp = tickBar = pip = null;
            hatch = ruler = noise = null;
        }

        delegate float AlphaFn(float x, float y);

        static Texture2D Tex(int w, int h, AlphaFn f, TextureWrapMode wrap = TextureWrapMode.Clamp, bool mips = false)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mips) { wrapMode = wrap, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(f(x + 0.5f, y + 0.5f)) * 255f));
            t.SetPixels32(px);
            t.Apply(mips, false);
            return t;
        }

        static Sprite Spr(Texture2D t, float border) =>
            Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));

        /// <summary>Distance (+ inside) to a rect with a big top-left cut and a small bottom-right cut.</summary>
        static float NotchDist(float x, float y, float w, float h, float big, float small)
        {
            float edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(w - x, h - y));
            float tl = (x + (h - y) - big) * 0.7071f;
            float br = ((w - x) + y - small) * 0.7071f;
            return Mathf.Min(edge, Mathf.Min(tl, br));
        }

        /// <summary>Distance (+ inside) to a rect with only the bottom-right corner cut.</summary>
        static float CutDist(float x, float y, float w, float h, float cut)
        {
            float edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(w - x, h - y));
            float br = ((w - x) + y - cut) * 0.7071f;
            return Mathf.Min(edge, br);
        }

        const int PS = 96;

        /// <summary>Glass plate: big top-left notch, small bottom-right notch, 9-sliced.</summary>
        public static Sprite Plate => plate ? plate : plate = Spr(Tex(PS, PS, (x, y) => NotchDist(x, y, PS, PS, 22f, 9f) + 0.5f), 26f);
        public static Sprite PlateFrame => plateFrame ? plateFrame : plateFrame = Spr(Tex(PS, PS, (x, y) =>
        {
            float d = NotchDist(x, y, PS, PS, 22f, 9f);
            return Mathf.Clamp01(d + 0.5f) * Mathf.Clamp01(1.9f - d);
        }), 26f);

        /// <summary>Inner edge glow of the plate shape (bright at the rim, fading inwards).</summary>
        public static Sprite PlateGlow => plateGlow ? plateGlow : plateGlow = Spr(Tex(PS, PS, (x, y) =>
        {
            float d = NotchDist(x, y, PS, PS, 22f, 9f);
            if (d < 0f) return 0f;
            float k = Mathf.Clamp01(1f - d / 22f);
            return k * k;
        }), 26f);

        const int SS = 64;

        /// <summary>Inventory slot: bottom-right cut corner.</summary>
        public static Sprite Slot => slot ? slot : slot = Spr(Tex(SS, SS, (x, y) => CutDist(x, y, SS, SS, 14f) + 0.5f), 18f);
        public static Sprite SlotFrame => slotFrame ? slotFrame : slotFrame = Spr(Tex(SS, SS, (x, y) =>
        {
            float d = CutDist(x, y, SS, SS, 14f);
            return Mathf.Clamp01(d + 0.5f) * Mathf.Clamp01(1.8f - d);
        }), 18f);

        /// <summary>Inner glow of a slot (bright at the edge, fading inwards) - rarity underlight.</summary>
        public static Sprite SlotGlow => slotGlow ? slotGlow : slotGlow = Spr(Tex(SS, SS, (x, y) =>
        {
            float d = CutDist(x, y, SS, SS, 14f);
            if (d < 0f) return 0f;
            float k = Mathf.Clamp01(1f - d / 16f);
            return k * k * 0.9f;
        }), 18f);

        /// <summary>Thin corner bracket (top-left L, 2px). Rotate/flip for the others.</summary>
        public static Sprite Corner => corner ? corner : corner = Spr(Tex(32, 32, (x, y) =>
            (x < 2.5f && y > 14f) || (y > 29.5f && x < 18f) ? 1f : 0f), 0);

        /// <summary>Arrow head pointing up (edge waypoints).</summary>
        public static Sprite Chevron => chevron ? chevron : chevron = Spr(Tex(64, 64, (x, y) =>
        {
            float u = Mathf.Abs(x - 32f), v = y - 12f;
            float outer = (40f - v) - u * 1.1f;
            float inner = (22f - v) - u * 1.1f;
            return Mathf.Clamp01(Mathf.Min(outer, -inner, v) + 0.5f);
        }), 0);

        /// <summary>Header tab: parallelogram with a slanted right edge.</summary>
        public static Sprite Tab => tab ? tab : tab = Spr(Tex(64, 32, (x, y) =>
            Mathf.Clamp01(Mathf.Min(x, (64f - 16f + y * 0.5f) - x, Mathf.Min(y, 32f - y)) + 0.5f)), 0);

        /// <summary>Vertical gradient, opaque at the bottom.</summary>
        public static Sprite GradUp => gradUp ? gradUp : gradUp = Spr(Tex(4, 64, (x, y) => Mathf.Pow(1f - y / 64f, 1.6f)), 0);

        /// <summary>Thin ring (radius 60, ~3px of a 128 texture) for radial fills on the ground ring.</summary>
        public static Sprite ThinArc => thinArc ? thinArc : thinArc = Spr(Tex(128, 128, (x, y) =>
        {
            float r = Mathf.Sqrt((x - 64) * (x - 64) + (y - 64) * (y - 64));
            return Mathf.Clamp01(2f - Mathf.Abs(r - 60f));
        }, TextureWrapMode.Clamp, true), 0);

        /// <summary>Single slanted tick for ammo strips.</summary>
        public static Sprite TickBar => tickBar ? tickBar : tickBar = Spr(Tex(16, 48, (x, y) =>
        {
            float s = y / 48f * 5f;
            return Mathf.Clamp01(Mathf.Min(x - s, (11f + s) - x) + 0.5f);
        }), 0);

        /// <summary>Small diamond pip.</summary>
        public static Sprite Pip => pip ? pip : pip = Spr(Tex(24, 24, (x, y) =>
            Mathf.Clamp01(10.5f - (Mathf.Abs(x - 12) + Mathf.Abs(y - 12)) + 0.5f)), 0);

        /// <summary>Tileable 45 degree hatching (warning stripes, empty slots).</summary>
        public static Texture2D Hatch => hatch ? hatch : hatch = Tex(16, 16, (x, y) => ((x + y) % 16f) < 5f ? 1f : 0f, TextureWrapMode.Repeat);

        /// <summary>Tileable ruler: one tick per tile (major tick handled by a second layer).</summary>
        public static Texture2D Ruler => ruler ? ruler : ruler = Tex(8, 8, (x, y) => x < 1.5f ? 1f : 0f, TextureWrapMode.Repeat);

        /// <summary>Tileable static noise for holo shimmer.</summary>
        public static Texture2D Noise
        {
            get
            {
                if (noise) return noise;
                var rng = new System.Random(7);
                noise = Tex(64, 64, (x, y) => (float)rng.NextDouble(), TextureWrapMode.Repeat);
                noise.filterMode = FilterMode.Point;
                return noise;
            }
        }

        // ------------------------------------------------------------------ builders

        /// <summary>Glass plate: drop shadow, translucent fill, top sheen, hairline frame and two thin corner brackets.</summary>
        public static RectTransform GlassPlate(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color accent, float frameA = 0.38f)
        {
            var p = UiKit.Node(name, parent, anchor, pivot, pos, size);
            var sh = UiKit.Fill("Shadow", p, UiKit.Glow, new Color(0, 0, 0, 0.5f));
            sh.rectTransform.offsetMin = new Vector2(-26, -30); sh.rectTransform.offsetMax = new Vector2(26, 20);
            UiKit.Fill("Fill", p, Plate, Glass);
            var sheen = UiKit.Fill("Sheen", p, GradUp, A(accent, 0.07f));
            sheen.rectTransform.localScale = new Vector3(1, -1, 1);
            var n = Tiled("Noise", p, Noise, new Color(1, 1, 1, 0.025f));
            n.uvRect = new Rect(0, 0, size.x / 64f, size.y / 64f);
            UiKit.Fill("Inner", p, PlateGlow, A(accent, 0.13f));
            UiKit.Fill("Shade", p, GradUp, new Color(0f, 0f, 0f, 0.22f));
            var hi = UiKit.Img("TopLine", p, UiKit.FadeLine, new Color(1f, 1f, 1f, 0.22f), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(30, -2.5f), new Vector2(Mathf.Min(260f, size.x - 60f), 1.5f));
            UiKit.Fill("Frame", p, PlateFrame, A(accent, frameA));
            Brackets(p, A(accent, 0.9f), 14f, 4f, true);
            return p;
        }

        /// <summary>Thin brackets on two (top-right + bottom-left) or all four corners.</summary>
        public static Image[] Brackets(RectTransform p, Color c, float size, float inset, bool twoOnly = false)
        {
            var res = new Image[twoOnly ? 2 : 4];
            int k = 0;
            for (int i = 0; i < 4; i++)
            {
                bool right = i == 1 || i == 3, top = i < 2;
                if (twoOnly && !((right && top) || (!right && !top))) continue;
                var a = new Vector2(right ? 1 : 0, top ? 1 : 0);
                var img = UiKit.Img("Bracket" + i, p, Corner, c, a, a, new Vector2(right ? inset : -inset, top ? inset : -inset), new Vector2(size, size));
                img.rectTransform.localScale = new Vector3(right ? -1 : 1, top ? 1 : -1, 1);
                res[k++] = img;
            }
            return res;
        }

        public static RawImage Tiled(string name, Transform parent, Texture2D tex, Color color)
        {
            var ri = HoloKit.Tiled(name, parent, tex, color);
            return ri;
        }

        /// <summary>Tiny technical caption (serials, units).</summary>
        public static Text MicroText(string name, Transform parent, string text, Vector2 anchor, Vector2 pivot, Vector2 pos, TextAnchor align = TextAnchor.MiddleLeft, int size = 11)
        {
            var t = UiKit.Label(name, parent, text, size, Micro, align, anchor, pivot, pos, new Vector2(300, 14), false, false);
            return t;
        }

        /// <summary>Header: slanted accent tab with the title, a fading rule and a micro caption on the right.</summary>
        public static Text Header(RectTransform p, string title, string micro, Color accent, float width)
        {
            UiKit.Img("TabGlow", p, HoloKit.SoftDot, A(accent, 0.12f), new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(90, -30), new Vector2(260, 70));
            UiKit.Img("Tab", p, Tab, A(accent, 0.95f), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(18, -30), new Vector2(8, 22));
            var t = UiKit.Label("Title", p, title, 32, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(36, -28), new Vector2(width - 60, 36), true);
            t.font = Num;
            for (int i = 0; i < 4; i++)
                UiKit.Img("TitleTick" + i, p, UiKit.Skew, A(accent, 0.8f - i * 0.17f), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(36 + t.preferredWidth + 14 + i * 10, -28), new Vector2(10, 15));
            UiKit.Img("Rule", p, UiKit.FadeLine, A(accent, 0.35f), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(18, -54), new Vector2(width - 36, 1.5f));
            UiKit.Img("RuleHi", p, UiKit.White, A(accent, 0.9f), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(18, -54), new Vector2(46, 2.5f));
            if (!string.IsNullOrEmpty(micro))
                MicroText("Micro", p, micro, new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-22, -38), TextAnchor.MiddleRight);
            return t;
        }

        /// <summary>Applies visor curvature to every graphic below `space` (call after building the cluster).</summary>
        public static void Warp(RectTransform space, Vector2 hingeAnchor, Vector2 hingeOffset, float angleDeg)
        {
            foreach (var g in space.GetComponentsInChildren<Graphic>(true))
            {
                var w = g.GetComponent<VisorWarp>() ?? g.gameObject.AddComponent<VisorWarp>();
                w.space = space;
                w.hingeAnchor = hingeAnchor;
                w.hingeOffset = hingeOffset;
                w.angle = angleDeg;
            }
        }
    }

    /// <summary>
    /// Bends a UI graphic as if it were printed on the curved glass of a visor: the plane is rotated about a vertical
    /// hinge (the edge of the cluster nearest the screen centre) and projected with perspective, so the outer edge
    /// grows towards the eye. Runs after Shadow/Outline (added later), so their copies are bent too.
    /// </summary>
    [DisallowMultipleComponent]
    public class VisorWarp : BaseMeshEffect
    {
        public RectTransform space;
        public Vector2 hingeAnchor;     // normalised position in `space`
        public Vector2 hingeOffset;     // px from that point
        public float angle;             // degrees; sign picks which side comes forward
        public float focal = 1500f;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || !space || Mathf.Approximately(angle, 0f)) return;
            Matrix4x4 toSpace = space.worldToLocalMatrix * transform.localToWorldMatrix;
            Matrix4x4 back = toSpace.inverse;
            Rect r = space.rect;
            Vector2 hinge = r.min + Vector2.Scale(r.size, hingeAnchor) + hingeOffset;
            float s = Mathf.Sin(angle * Mathf.Deg2Rad), c = Mathf.Cos(angle * Mathf.Deg2Rad);
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                Vector3 p = toSpace.MultiplyPoint3x4(v.position);
                float x = p.x - hinge.x, y = p.y - hinge.y;
                float k = focal / Mathf.Max(200f, focal - x * s);
                p.x = hinge.x + x * c * k;
                p.y = hinge.y + y * k;
                v.position = back.MultiplyPoint3x4(p);
                vh.SetUIVertex(v, i);
            }
        }
    }

    /// <summary>Sci-fi text "decode": new strings resolve left to right out of scrambled glyphs.</summary>
    [RequireComponent(typeof(Text))]
    public class DecodeText : MonoBehaviour
    {
        const string Glyphs = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789#%/<>[]=+";
        Text text;
        string target = "";
        float t = 1f, duration = 0.4f;
        System.Random rng = new System.Random();

        public string Target => target;

        void Awake() { text = GetComponent<Text>(); }

        public void Set(string s, float dur = 0.4f, bool instant = false)
        {
            if (!text) text = GetComponent<Text>();
            if (s == target && !instant) return;
            target = s ?? "";
            duration = Mathf.Max(0.05f, dur);
            t = instant ? 1f : 0f;
            if (instant) text.text = target;
        }

        public void Replay() { t = 0f; }

        void Update()
        {
            if (t >= 1f) return;
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / duration);
            int n = target.Length;
            int shown = Mathf.FloorToInt(t * (n + 3)) - 3;
            var sb = new System.Text.StringBuilder(n);
            for (int i = 0; i < n; i++)
            {
                char ch = target[i];
                sb.Append(i <= shown || ch == ' ' ? ch : Glyphs[rng.Next(Glyphs.Length)]);
            }
            text.text = t >= 1f ? target : sb.ToString();
        }
    }
}
