using UnityEngine;
using UnityEngine.UI;

namespace Zcom
{
    /// <summary>
    /// Shared look for all UI: Rajdhani font, colour palette and procedurally generated sci-fi sprites
    /// (chamfered panels, frames, brackets, slanted bar segments, glows, key caps, icons) plus small builders.
    /// </summary>
    public static class UiKit
    {
        // ------------------------------------------------------------------ palette
        public static readonly Color Accent = new Color(0.38f, 0.9f, 1f);
        public static readonly Color AccentDim = new Color(0.38f, 0.9f, 1f, 0.35f);
        public static readonly Color Amber = new Color(1f, 0.68f, 0.22f);
        public static readonly Color Danger = new Color(1f, 0.3f, 0.25f);
        public static readonly Color TextColor = new Color(0.93f, 0.96f, 1f);
        public static readonly Color TextDim = new Color(0.7f, 0.8f, 0.9f, 0.9f);
        public static readonly Color PanelBg = new Color(0.015f, 0.035f, 0.06f, 0.82f);
        public static readonly Color SlotBg = new Color(0.04f, 0.075f, 0.11f, 0.95f);

        // ------------------------------------------------------------------ fonts
        static Font bold, semi;
        public static Font Bold => bold ? bold : bold = Resources.Load<Font>("Fonts/Rajdhani-Bold") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        public static Font Semi => semi ? semi : semi = Resources.Load<Font>("Fonts/Rajdhani-SemiBold") ?? Bold;

        static readonly System.Collections.Generic.Dictionary<string, Font> fonts = new System.Collections.Generic.Dictionary<string, Font>();
        /// <summary>Any font in Resources/Fonts by file name (falls back to Rajdhani Bold).</summary>
        public static Font F(string name)
        {
            if (!fonts.TryGetValue(name, out var f) || !f) fonts[name] = f = Resources.Load<Font>("Fonts/" + name) ?? Bold;
            return f;
        }

        // ------------------------------------------------------------------ sprites
        static Sprite panelFill, panelFrame, bracket, skew, glow, circle, ring, diamond, roundFill, roundFrame, line, grenadeIcon, dashIcon, white;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            panelFill = panelFrame = bracket = skew = glow = circle = ring = diamond = roundFill = roundFrame = line = grenadeIcon = dashIcon = white = null;
            bold = semi = null;
            fonts.Clear();
        }

        delegate float AlphaFn(float x, float y);

        static Texture2D Tex(int w, int h, AlphaFn f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(f(x + 0.5f, y + 0.5f)) * 255f));
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        static Sprite Spr(Texture2D t, float border) =>
            Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));

        const int P = 64;      // panel texture size
        const float Cut = 13f; // chamfer size (px)

        /// <summary>Signed distance (px, positive inside) to a rect with the top-left and bottom-right corners cut.</summary>
        static float ChamferDist(float x, float y, float w, float h, float cut)
        {
            float edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(w - x, h - y));
            float tl = (x + (h - y) - cut) * 0.7071f;
            float br = ((w - x) + y - cut) * 0.7071f;
            return Mathf.Min(edge, Mathf.Min(tl, br));
        }

        static float RoundDist(float x, float y, float w, float h, float r)
        {
            float dx = Mathf.Max(r - x, x - (w - r), 0f);
            float dy = Mathf.Max(r - y, y - (h - r), 0f);
            float outside = Mathf.Sqrt(dx * dx + dy * dy);
            float inside = Mathf.Min(Mathf.Min(x, y), Mathf.Min(w - x, h - y));
            return dx > 0f || dy > 0f ? r - outside : inside;
        }

        /// <summary>Solid chamfered panel (tint with Image.color).</summary>
        public static Sprite PanelFill => panelFill ? panelFill : panelFill = Spr(Tex(P, P, (x, y) => ChamferDist(x, y, P, P, Cut) + 0.5f), Cut + 3f);

        /// <summary>1.5px outline of the chamfered panel.</summary>
        public static Sprite PanelFrame => panelFrame ? panelFrame : panelFrame = Spr(Tex(P, P, (x, y) =>
        {
            float d = ChamferDist(x, y, P, P, Cut);
            return Mathf.Clamp01(d + 0.5f) * Mathf.Clamp01(2.2f - d);
        }), Cut + 3f);

        /// <summary>Corner bracket (top-left L). Rotate for the other corners.</summary>
        public static Sprite Bracket => bracket ? bracket : bracket = Spr(Tex(24, 24, (x, y) =>
            (x < 2.5f && y > 8f) || (y > 21.5f && x < 16f) ? 1f : 0f), 0);

        /// <summary>Slanted bar segment (parallelogram).</summary>
        public static Sprite Skew => skew ? skew : skew = Spr(Tex(40, 16, (x, y) =>
        {
            float s = y / 16f * 8f;
            return Mathf.Clamp01(Mathf.Min(x - s, (40f - 8f + s) - x) + 0.5f);
        }), 0);

        /// <summary>Soft rectangular glow (9-sliced) for backdrops and shadows.</summary>
        public static Sprite Glow => glow ? glow : glow = Spr(Tex(64, 64, (x, y) =>
        {
            float d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(64 - x, 64 - y));
            float k = Mathf.Clamp01(d / 28f);
            return k * k * (3f - 2f * k);
        }), 30f);

        public static Sprite Circle => circle ? circle : circle = Spr(Tex(64, 64, (x, y) =>
            Mathf.Clamp01(31.5f - Mathf.Sqrt((x - 32) * (x - 32) + (y - 32) * (y - 32)) + 0.5f)), 0);

        public static Sprite Ring => ring ? ring : ring = Spr(Tex(128, 128, (x, y) =>
        {
            float r = Mathf.Sqrt((x - 64) * (x - 64) + (y - 64) * (y - 64));
            return Mathf.Clamp01(2.5f - Mathf.Abs(r - 58f));
        }), 0);

        public static Sprite Diamond => diamond ? diamond : diamond = Spr(Tex(32, 32, (x, y) =>
            Mathf.Clamp01(15.5f - (Mathf.Abs(x - 16) + Mathf.Abs(y - 16)) + 0.5f)), 0);

        public static Sprite RoundFill => roundFill ? roundFill : roundFill = Spr(Tex(32, 32, (x, y) => RoundDist(x, y, 32, 32, 7f) + 0.5f), 10f);

        public static Sprite RoundFrame => roundFrame ? roundFrame : roundFrame = Spr(Tex(32, 32, (x, y) =>
        {
            float d = RoundDist(x, y, 32, 32, 7f);
            return Mathf.Clamp01(d + 0.5f) * Mathf.Clamp01(2.4f - d);
        }), 10f);

        /// <summary>Horizontal line that fades out to the right (flip with negative scale for the left side).</summary>
        public static Sprite FadeLine => line ? line : line = Spr(Tex(128, 4, (x, y) => Mathf.Pow(1f - x / 128f, 1.5f)), 0);

        public static Sprite White => white ? white : white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);

        /// <summary>Plasma grenade glyph.</summary>
        public static Sprite GrenadeIcon => grenadeIcon ? grenadeIcon : grenadeIcon = Spr(Tex(64, 64, (x, y) =>
        {
            float body = 20f - Mathf.Sqrt((x - 32) * (x - 32) + (y - 28) * (y - 28));
            float cap = Mathf.Min(Mathf.Min(x - 26f, 38f - x), Mathf.Min(y - 46f, 54f - y));
            float band = Mathf.Abs(y - 28f) < 3f && body > 0f ? -1f : 0f;
            float pin = 4f - Mathf.Abs(Mathf.Sqrt((x - 44) * (x - 44) + (y - 52) * (y - 52)) - 6f);
            float a = Mathf.Max(body + band * 3f, cap, pin * 0.6f);
            return Mathf.Clamp01(a + 0.5f);
        }), 0);

        /// <summary>Dash glyph (double chevron).</summary>
        public static Sprite DashIcon => dashIcon ? dashIcon : dashIcon = Spr(Tex(64, 64, (x, y) =>
        {
            float Chevron(float ox)
            {
                float dx = x - ox, dy = Mathf.Abs(y - 32f);
                return 4.5f - Mathf.Abs(dx + dy * 0.9f - 6f) * 0.75f - Mathf.Max(0f, dy - 18f);
            }
            return Mathf.Clamp01(Mathf.Max(Chevron(16f), Chevron(32f)) + 0.5f);
        }), 0);

        // ------------------------------------------------------------------ builders

        public static RectTransform Node(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(string name, Transform parent, float inset = 0f)
        {
            var rt = Node(name, parent, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static Image Img(string name, Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = Node(name, parent, anchor, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sprite && sprite.border.sqrMagnitude > 0f ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Image Fill(string name, Transform parent, Sprite sprite, Color color, float inset = 0f)
        {
            var rt = Stretch(name, parent, inset);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sprite && sprite.border.sqrMagnitude > 0f ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor align,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box, bool bold = true, bool shadow = true)
        {
            var rt = Node(name, parent, anchor, pivot, pos, box);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = bold ? Bold : Semi;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            if (shadow)
            {
                var s = rt.gameObject.AddComponent<Shadow>();
                s.effectColor = new Color(0f, 0f, 0f, 0.65f);
                s.effectDistance = new Vector2(1.5f, -1.5f);
            }
            return t;
        }

        /// <summary>Chamfered panel = soft shadow + dark fill + thin accent frame + two corner brackets.</summary>
        public static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color? frame = null, bool brackets = true)
        {
            var rt = Node(name, parent, anchor, pivot, pos, size);
            var sh = Img("Shadow", rt, Glow, new Color(0f, 0f, 0f, 0.45f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -4), size + new Vector2(40, 40));
            sh.rectTransform.anchorMin = Vector2.zero; sh.rectTransform.anchorMax = Vector2.one;
            sh.rectTransform.offsetMin = new Vector2(-22, -26); sh.rectTransform.offsetMax = new Vector2(22, 18);
            Fill("Fill", rt, PanelFill, PanelBg);
            Fill("Frame", rt, PanelFrame, frame ?? new Color(Accent.r, Accent.g, Accent.b, 0.28f));
            if (brackets)
            {
                Color bc = frame.HasValue ? new Color(frame.Value.r, frame.Value.g, frame.Value.b, 0.9f) : new Color(Accent.r, Accent.g, Accent.b, 0.85f);
                var a = Img("BracketTR", rt, Bracket, bc, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-3, -3), new Vector2(16, 16));
                a.rectTransform.localScale = new Vector3(-1, 1, 1);
                var b = Img("BracketBL", rt, Bracket, bc, new Vector2(0, 0), new Vector2(0, 0), new Vector2(3, 3), new Vector2(16, 16));
                b.rectTransform.localScale = new Vector3(1, -1, 1);
            }
            return rt;
        }

        /// <summary>Key cap with a letter, e.g. [E].</summary>
        public static RectTransform KeyCap(string name, Transform parent, string key, Vector2 anchor, Vector2 pivot, Vector2 pos, int size = 16)
        {
            float w = Mathf.Max(26f, key.Length * size * 0.62f + 14f);
            var rt = Node(name, parent, anchor, pivot, pos, new Vector2(w, 26f));
            Fill("Fill", rt, RoundFill, new Color(0.9f, 0.95f, 1f, 0.12f));
            Fill("Frame", rt, RoundFrame, new Color(0.9f, 0.95f, 1f, 0.7f));
            Label("Key", rt, key, size, TextColor, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(w, 26f), true, false);
            return rt;
        }

        /// <summary>Adds wide letter spacing (UI title style) to a plain string.</summary>
        public static string Spaced(string s, int spaces = 1)
        {
            var sb = new System.Text.StringBuilder(s.Length * (spaces + 1));
            string gap = new string(' ', spaces);
            for (int i = 0; i < s.Length; i++)
            {
                sb.Append(s[i]);
                if (i < s.Length - 1) sb.Append(s[i] == ' ' ? "  " : gap);
            }
            return sb.ToString();
        }

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        /// <summary>Codex-generated UI art from Resources/UI (icons, portraits).</summary>
        public static Sprite Art(string name) => Resources.Load<Sprite>("UI/" + name);
    }
}
