using UnityEngine;
using UnityEngine.UI;

namespace Zcom
{
    public enum UiDesign { TacLink, FieldKit }

    /// <summary>Which UI design is active. F6 in game flips it (stored in PlayerPrefs) and restarts the raid.</summary>
    public static class UiStyle
    {
        const string Key = "zcom.uiDesign.v2";
        /// <summary>Set by tools (smoke test) to force a design without touching the saved preference.</summary>
        public static UiDesign? Override;
        public static UiDesign Current => Override ?? (UiDesign)PlayerPrefs.GetInt(Key, (int)UiDesign.TacLink);
        public static bool Kit => Current == UiDesign.FieldKit;

        public static void Toggle()
        {
            PlayerPrefs.SetInt(Key, Kit ? (int)UiDesign.TacLink : (int)UiDesign.FieldKit);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// "Field kit" look (target art: Art/Reference/Codex/hud_b_fieldkit_target.png): the colony's own issue gear rather
    /// than a hologram. Solid ink blocks, paper-white type and label tape, one signal-orange accent (the same paint as
    /// the outpost trim), hazard stripes for anything dangerous. Type does the work: Bebas Neue for names and numerals,
    /// Barlow Condensed for captions, IBM Plex Mono for small print. No glass, glow or scanlines.
    /// </summary>
    public static class FieldKit
    {
        public static readonly Color Ink = new Color(0.035f, 0.04f, 0.048f, 0.95f);
        public static readonly Color InkSolid = new Color(0.035f, 0.04f, 0.048f, 1f);
        public static readonly Color InkSoft = new Color(0.09f, 0.1f, 0.115f, 0.95f);
        public static readonly Color Paper = new Color(0.9f, 0.885f, 0.84f);
        public static readonly Color Signal = new Color(1f, 0.43f, 0.1f);
        public static readonly Color Ice = new Color(0.55f, 0.85f, 1f);
        public static readonly Color Danger = new Color(1f, 0.25f, 0.2f);
        public static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);
        public static Color Faint => A(Paper, 0.75f);

        public static Font Display => UiKit.F("BebasNeue-Regular");
        public static Font Caption => UiKit.F("BarlowCondensed-Bold");
        public static Font CaptionSemi => UiKit.F("BarlowCondensed-SemiBold");
        public static Font MonoFont => UiKit.F("IBMPlexMono-Medium");

        static Texture2D hazard, barcode, grain;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { hazard = barcode = grain = null; }

        static Texture2D Make(int w, int h, System.Func<int, int, float> f, FilterMode filter = FilterMode.Bilinear)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = filter };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(f(x, y)) * 255f));
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        /// <summary>Tileable hazard stripes: 45 degrees, half ink half gap.</summary>
        public static Texture2D Hazard => hazard ? hazard : hazard = Make(64, 64, (x, y) =>
        {
            float v = (x + y) % 32;
            float e = Mathf.Min(v, 16f - v) * 0.7071f;        // > 0 inside the stripe
            return Mathf.Clamp01(e + 0.5f);
        });

        /// <summary>A strip of barcode (random bar widths), stretched vertically.</summary>
        public static Texture2D Barcode
        {
            get
            {
                if (barcode) return barcode;
                var rng = new System.Random(883771);
                var bars = new bool[120];
                for (int i = 0; i < bars.Length;)
                {
                    int w = 1 + rng.Next(3);
                    bool on = (i == 0) || rng.NextDouble() < 0.55;
                    for (int k = 0; k < w && i < bars.Length; k++) bars[i++] = on;
                    if (i < bars.Length) bars[i++] = false;
                }
                barcode = Make(bars.Length, 2, (x, y) => bars[x] ? 1f : 0f, FilterMode.Point);
                barcode.wrapMode = TextureWrapMode.Clamp;
                return barcode;
            }
        }

        /// <summary>Print grain for paper surfaces (use as a dark, very transparent overlay).</summary>
        public static Texture2D Grain
        {
            get
            {
                if (grain) return grain;
                var rng = new System.Random(11);
                return grain = Make(128, 128, (x, y) => (float)(rng.NextDouble() * rng.NextDouble()));
            }
        }

        /// <summary>Solid block with a soft drop shadow (legible over bright ground) and a clipped bottom-right corner.</summary>
        public static RectTransform Block(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color, bool shadow = true)
        {
            var rt = UiKit.Node(name, parent, anchor, pivot, pos, size);
            if (shadow)
            {
                var sh = UiKit.Fill("Shadow", rt, UiKit.Glow, new Color(0, 0, 0, 0.45f));
                sh.rectTransform.offsetMin = new Vector2(-18, -22); sh.rectTransform.offsetMax = new Vector2(18, 14);
            }
            UiKit.Fill("Fill", rt, TacKit.Slot, color);
            return rt;
        }

        /// <summary>Paper surface: block + print grain.</summary>
        public static RectTransform PaperBlock(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = Block(name, parent, anchor, pivot, pos, size, Paper);
            var m = UiKit.Fill("GrainMask", rt, TacKit.Slot, Color.white);
            m.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var g = HoloKit.Tiled("Grain", m.transform, Grain, new Color(0.2f, 0.16f, 0.1f, 0.22f));
            g.uvRect = new Rect(0, 0, size.x / 128f, size.y / 128f);
            return rt;
        }

        /// <summary>Display type (Bebas Neue unless another font is given).</summary>
        public static Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor align, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box, Font font = null)
        {
            var t = UiKit.Label(name, parent, text, size, color, align, anchor, pivot, pos, box, true, false);
            t.font = font ? font : Display;
            return t;
        }

        /// <summary>Tracked caps caption (Barlow Condensed Bold).</summary>
        public static Text Micro(string name, Transform parent, string text, Vector2 anchor, Vector2 pivot, Vector2 pos, TextAnchor align = TextAnchor.MiddleLeft, int size = 16)
        {
            var t = UiKit.Label(name, parent, UiKit.Spaced(text), size, Faint, align, anchor, pivot, pos, new Vector2(300, size + 6), true, false);
            t.font = Caption;
            return t;
        }

        /// <summary>Small print (IBM Plex Mono).</summary>
        public static Text Mono(string name, Transform parent, string text, int size, Color color, Vector2 anchor, Vector2 pivot, Vector2 pos, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var t = UiKit.Label(name, parent, text, size, color, align, anchor, pivot, pos, new Vector2(320, size + 6), true, false);
            t.font = MonoFont;
            return t;
        }

        /// <summary>Hazard stripes (scroll uvRect.x to make them march).</summary>
        public static RawImage Stripes(string name, Transform parent, Color color, Vector2 size, float scale = 1f)
        {
            var ri = HoloKit.Tiled(name, parent, Hazard, color);
            ri.uvRect = new Rect(0, 0, size.x / (64f * scale), size.y / (64f * scale));
            return ri;
        }

        /// <summary>Label tape: a strip sized to its text, e.g. ink type on paper.</summary>
        public class Tape
        {
            public RectTransform rt;
            public Image bg;
            public Text text;
            float pad, height;
            string shown;

            public Tape(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, float height, int fontSize, Color bg, Color fg, float pad = 10f)
            {
                this.pad = pad; this.height = height;
                rt = UiKit.Node(name, parent, anchor, pivot, pos, new Vector2(60, height));
                this.bg = UiKit.Fill("Bg", rt, UiKit.White, bg);
                text = Label("Text", rt, "", fontSize, fg, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -1), new Vector2(600, height));
            }

            public void Set(string s, Color? bgColor = null, Color? fgColor = null)
            {
                if (bgColor.HasValue) bg.color = bgColor.Value;
                if (fgColor.HasValue) text.color = fgColor.Value;
                if (s == shown) return;
                shown = s;
                text.text = s;
                rt.gameObject.SetActive(!string.IsNullOrEmpty(s));
                rt.sizeDelta = new Vector2(text.preferredWidth + pad * 2f, height);
            }
        }
    }

    /// <summary>Mechanical counter: when the value changes the number drops into place like a flap display.</summary>
    [RequireComponent(typeof(Text))]
    public class TickText : MonoBehaviour
    {
        Text text;
        Vector2 home;
        float kick;
        bool homed;
        public float distance = 8f;

        public void Set(string s)
        {
            if (!text) text = GetComponent<Text>();
            if (!homed) { home = text.rectTransform.anchoredPosition; homed = true; }
            if (text.text == s) return;
            text.text = s;
            kick = 1f;
        }

        void Update()
        {
            if (!homed || kick <= 0f) return;
            kick = Mathf.MoveTowards(kick, 0f, Time.unscaledDeltaTime * 11f);
            text.rectTransform.anchoredPosition = home + new Vector2(0f, kick * kick * distance);
        }
    }
}
