using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Zcom
{
    /// <summary>
    /// Clean, minimal sci-fi HUD (Art/Reference/Codex/hud_v3_mockup.png), built at runtime:
    ///  bottom-left   live 3D helmet portrait (PreviewStage), name, shield + health bars, ability slots
    ///  bottom-right  live 3D weapon render, name, ammo counter, cell bar, loadout slots
    ///  top-right     objective tracker;  top-centre  area name + hostiles alert
    ///  world         reticle with ammo arc, slim enemy bars, damage numbers, scan markers, damage-direction arcs, prompts
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public static Hud I { get; private set; }
        public Canvas Canvas { get; private set; }

        static readonly Color Cyan = new Color(0.36f, 0.9f, 1f);
        static readonly Color HealthCol = new Color(1f, 0.72f, 0.26f);
        static readonly Color Glass = new Color(0.02f, 0.045f, 0.07f, 0.62f);
        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);
        static Color Dim => UiKit.TextDim;

        RectTransform root, worldLayer, dmgLayer;
        CanvasGroup bottomL, bottomR, topR;
        float startT;

        // vitals
        RawImage portrait;
        Image portraitFrame;
        Text hpValue, shValue, shieldAlert;
        Image[] hpSegs, shSegs;
        float chip = 1f, chipDelay, shieldFlash, shieldAlertT;

        class SkillUi { public RectTransform rt; public Image icon, ring, ringBg, fill; public Image[] pips; public float punch; }
        SkillUi grenade, dash, scanSkill;

        // weapon
        RawImage weaponView;
        Text weaponName, ammoText, ammoMax, weaponState;
        Image cellFill, cellChip;
        class Slot { public Image bg, frame; public Text num; }
        readonly Slot[] slots = new Slot[3];
        int lastInvVersion = -1, lastAmmo = -1;
        float ammoPunch, cellChipV = 1f;
        WeaponKind? shownWeapon;

        // top
        Text areaText, threatText, objectiveText;
        CanvasGroup areaGroup;
        RectTransform threatBox;
        Image[] objTicks;
        string lastArea;
        float areaT = -1f;

        // world
        class Bar { public RectTransform rt; public Image fill, chip; public bool used; public float shown = 1f; }
        readonly List<Bar> bars = new List<Bar>();
        readonly Dictionary<Alien, Bar> barOf = new Dictionary<Alien, Bar>();
        class Marker { public RectTransform rt; public Image icon; public Text text; public bool used; }
        readonly List<Marker> markers = new List<Marker>();
        class DmgArc { public Image img; public Vector3 dir; public float t; }
        readonly List<DmgArc> dmgArcs = new List<DmgArc>();

        // reticle
        RectTransform crosshair;
        readonly RectTransform[] chLines = new RectTransform[4];
        readonly Image[] chImages = new Image[4];
        readonly RectTransform[] hmLines = new RectTransform[4];
        readonly Image[] hmImages = new Image[4];
        Image chDot, reloadRing, ammoArc, ammoArcBg;
        Text reticleAmmo;
        float hitT, hitDur = 0.15f, chPunch;
        bool hitKill;

        // banner / prompt / overlays
        CanvasGroup bannerGroup;
        Text bannerKicker, bannerTitle, bannerSub;
        RectTransform bannerLineL, bannerLineR;
        float bannerT = -1f;
        RectTransform promptRect;
        Text promptText;
        Image promptFrame;
        Image vignette;
        float damageFlash;
        GameObject gameOver, controls;
        CanvasGroup gameOverGroup, controlsGroup;
        Text gameOverStats;
        float controlsShownUntil;

        class Floater { public RectTransform rt; public Text text; public Vector3 world, vel; public float t, life, scale; public bool active; }
        readonly List<Floater> floaters = new List<Floater>();
        int floaterCursor;
        LootChest[] chests;

        void Awake()
        {
            I = this;
            PreviewStage.Ensure();
            Build();
            Cursor.visible = false;
        }

        void Start() { chests = FindObjectsByType<LootChest>(FindObjectsSortMode.None); }
        void OnDestroy() { Cursor.visible = true; }

        // ================================================================== construction

        CanvasGroup Group(string name)
        {
            var rt = UiKit.Stretch(name, root);
            var g = rt.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            return g;
        }

        /// <summary>Frosted glass panel: dark translucent fill, soft top highlight, hairline frame.</summary>
        RectTransform GlassPanel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, float frameAlpha = 0.35f)
        {
            var p = UiKit.Node(name, parent, anchor, pivot, pos, size);
            UiKit.Img("Shadow", p, UiKit.Glow, new Color(0, 0, 0, 0.45f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -6), size + new Vector2(50, 50));
            UiKit.Fill("Fill", p, UiKit.PanelFill, Glass);
            UiKit.Img("Highlight", p, UiKit.Glow, new Color(0.6f, 0.9f, 1f, 0.05f), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(size.x - 10, size.y * 0.5f));
            UiKit.Fill("Frame", p, UiKit.PanelFrame, A(Cyan, frameAlpha));
            return p;
        }

        void Build()
        {
            var cgo = new GameObject("HUD Canvas", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            Canvas = cgo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root = (RectTransform)cgo.transform;

            UiKit.Fill("EdgeShade", root, ProcTex.VignetteSprite, new Color(0f, 0.01f, 0.03f, 0.35f));
            vignette = UiKit.Fill("Vignette", root, ProcTex.VignetteSprite, new Color(1f, 0.08f, 0.04f, 0f));
            worldLayer = UiKit.Stretch("WorldLayer", root);
            dmgLayer = UiKit.Node("DamageArcs", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            bottomL = Group("Vitals");
            bottomR = Group("Weapon");
            topR = Group("Objective");
            BuildVitals((RectTransform)bottomL.transform);
            BuildWeapon((RectTransform)bottomR.transform);
            BuildTop((RectTransform)topR.transform);
            BuildCrosshair();
            BuildBanner();
            BuildPrompt();
            BuildControls();
            gameObject.AddComponent<InventoryUI>().Init(root, UiKit.Bold);
            BuildGameOver();
            gameObject.AddComponent<RaidUI>().Init(root);
        }

        // ------------------------------------------------------------------ bottom-left

        void BuildVitals(RectTransform g)
        {
            // live helmet portrait in a slanted frame
            var pf = UiKit.Node("Portrait", g, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 40), new Vector2(190, 210));
            UiKit.Img("Glow", pf, HoloKit.SoftDot, A(Cyan, 0.16f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(300, 300));
            var mask = UiKit.Img("Mask", pf, UiKit.Skew, Color.white, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(190, 210));
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            UiKit.Img("Bg", mask.transform, UiKit.White, new Color(0.03f, 0.07f, 0.1f, 0.85f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200, 220));
            UiKit.Img("BgGlow", mask.transform, HoloKit.SoftDot, A(Cyan, 0.22f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(10, 10), new Vector2(260, 260));
            var go = new GameObject("Live", typeof(RectTransform));
            go.transform.SetParent(mask.transform, false);
            portrait = go.AddComponent<RawImage>();
            portrait.raycastTarget = false;
            var prt = portrait.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(230, 230);
            prt.anchoredPosition = new Vector2(4, -6);
            if (PreviewStage.I) portrait.texture = PreviewStage.I.Portrait;
            UiKit.Img("Shade", mask.transform, UiKit.Glow, new Color(0, 0.02f, 0.04f, 0.65f), new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(240, 90));
            portraitFrame = UiKit.Img("Frame", pf, HoloKit.SkewFrame, A(Cyan, 0.8f), new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(190, 210));
            UiKit.Img("Corner", pf, UiKit.White, Cyan, new Vector2(0, 0), new Vector2(0, 0), new Vector2(-2, -2), new Vector2(60, 3));

            const float x0 = 250f;
            UiKit.Label("Name", g, UiKit.Spaced("ORBITAL MARSHAL"), 24, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(x0, 226), new Vector2(360, 30), false);
            UiKit.Label("Class", g, UiKit.Spaced("VANGUARD"), 13, A(Cyan, 0.75f), TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(x0 + 1, 203), new Vector2(200, 18), false);

            UiKit.Img("ShieldIcon", g, UiKit.Art("icon_shield"), Cyan, new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(x0 + 9, 172), new Vector2(20, 20)).preserveAspect = true;
            UiKit.Label("ShieldLbl", g, UiKit.Spaced("SHIELD"), 16, Cyan, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(x0 + 28, 172), new Vector2(110, 20), false);
            shSegs = Bars(g, "Shield", new Vector2(x0 + 130, 172), 220f, 12f, 10);
            shValue = UiKit.Label("ShieldVal", g, "50", 24, Cyan, TextAnchor.MiddleRight, new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(x0 + 420, 173), new Vector2(70, 28), false);

            UiKit.Label("HealthGlyph", g, "+", 24, HealthCol, TextAnchor.MiddleCenter, new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(x0 + 9, 136), new Vector2(22, 24), false);
            UiKit.Label("HealthLbl", g, UiKit.Spaced("HEALTH"), 16, HealthCol, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(x0 + 28, 136), new Vector2(110, 20), false);
            hpSegs = Bars(g, "Health", new Vector2(x0 + 130, 136), 220f, 12f, 10);
            hpValue = UiKit.Label("HP", g, "100", 24, HealthCol, TextAnchor.MiddleRight, new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(x0 + 420, 137), new Vector2(70, 28), false);
            UiKit.Img("Divider", g, UiKit.FadeLine, A(Cyan, 0.4f), new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(x0, 112), new Vector2(380, 1.5f));
            shieldAlert = UiKit.Label("ShieldAlert", g, UiKit.Spaced("SHIELD DOWN"), 14, UiKit.Danger, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(x0 + 140, 203), new Vector2(200, 18), false);
            shieldAlert.color = A(UiKit.Danger, 0f);

            grenade = MakeSkill(g, "Grenade", new Vector2(x0 + 36, 66), "icon_grenade", "G", 2);
            dash = MakeSkill(g, "Dash", new Vector2(x0 + 132, 66), "icon_dash", "SPACE", 0);
            scanSkill = MakeSkill(g, "Scan", new Vector2(x0 + 228, 66), "icon_scan", "Q", 0);
        }

        Image[] Bars(Transform parent, string name, Vector2 pos, float width, float h, int count)
        {
            var arr = new Image[count];
            float gap = 3f, w = (width - gap * (count - 1)) / count;
            for (int i = 0; i < count; i++)
                arr[i] = UiKit.Img($"{name}{i}", parent, UiKit.Skew, Color.white, new Vector2(0, 0), new Vector2(0, 0.5f), pos + new Vector2(i * (w + gap), 0), new Vector2(w + h * 0.2f, h));
            return arr;
        }

        SkillUi MakeSkill(Transform parent, string name, Vector2 pos, string icon, string key, int pips)
        {
            var s = new SkillUi();
            s.rt = UiKit.Node(name, parent, new Vector2(0, 0), new Vector2(0.5f, 0.5f), pos, new Vector2(66, 66));
            s.fill = UiKit.Img("Fill", s.rt, UiKit.Circle, new Color(0.02f, 0.05f, 0.08f, 0.7f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60, 60));
            s.ringBg = UiKit.Img("RingBg", s.rt, HoloKit.Arc, new Color(1, 1, 1, 0.1f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70, 70));
            s.ring = HoloKit.ArcImage("Ring", s.rt, 70, Cyan, 0f);
            s.icon = UiKit.Img("Icon", s.rt, UiKit.Art(icon), UiKit.TextColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
            s.icon.preserveAspect = true;
            UiKit.Label("Key", s.rt, $"[{key}]", 15, Dim, TextAnchor.UpperCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(100, 20), false);
            s.pips = new Image[pips];
            for (int i = 0; i < pips; i++)
                s.pips[i] = UiKit.Img($"Pip{i}", s.rt, UiKit.Circle, Cyan, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-2 - i * 10, -2), new Vector2(7, 7));
            return s;
        }

        // ------------------------------------------------------------------ bottom-right

        void BuildWeapon(RectTransform g)
        {
            var p = GlassPanel("WeaponPanel", g, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 40), new Vector2(520, 156));
            UiKit.Img("ViewGlow", p, HoloKit.SoftDot, A(Cyan, 0.1f), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(150, 0), new Vector2(320, 200));
            var go = new GameObject("WeaponView", typeof(RectTransform));
            go.transform.SetParent(p, false);
            weaponView = go.AddComponent<RawImage>();
            weaponView.raycastTarget = false;
            var wr = weaponView.rectTransform;
            wr.anchorMin = wr.anchorMax = new Vector2(0, 0.5f);
            wr.pivot = new Vector2(0.5f, 0.5f);
            wr.anchoredPosition = new Vector2(150, 2);
            wr.sizeDelta = new Vector2(288, 120);
            if (PreviewStage.I) weaponView.texture = PreviewStage.I.WeaponTex;
            UiKit.Img("Divider", p, UiKit.White, A(Cyan, 0.25f), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(304, 0), new Vector2(1.5f, 110));

            weaponName = UiKit.Label("Name", p, "", 20, UiKit.TextColor, TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -16), new Vector2(200, 26), false);
            ammoText = UiKit.Label("Ammo", p, "", 58, Cyan, TextAnchor.LowerRight, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-84, 34), new Vector2(150, 70), false);
            ammoMax = UiKit.Label("AmmoMax", p, "", 26, A(Cyan, 0.6f), TextAnchor.LowerLeft, new Vector2(1, 0), new Vector2(0, 0), new Vector2(-80, 43), new Vector2(80, 32), false);
            weaponState = UiKit.Label("State", p, "", 13, UiKit.Amber, TextAnchor.MiddleRight, new Vector2(1, 0), new Vector2(1, 0.5f), new Vector2(-22, 108), new Vector2(200, 16), false);
            var bar = UiKit.Node("Cell", p, new Vector2(1, 0), new Vector2(1, 0.5f), new Vector2(-22, 24), new Vector2(190, 5));
            UiKit.Fill("Bg", bar, UiKit.White, new Color(1, 1, 1, 0.08f));
            cellChip = UiKit.Fill("Chip", bar, UiKit.White, new Color(1f, 1f, 1f, 0.4f));
            cellChip.type = Image.Type.Filled; cellChip.fillMethod = Image.FillMethod.Horizontal;
            cellFill = UiKit.Fill("Fill", bar, UiKit.White, Cyan);
            cellFill.type = Image.Type.Filled; cellFill.fillMethod = Image.FillMethod.Horizontal;

            for (int i = 0; i < 3; i++)
            {
                var s = new Slot();
                var rt = UiKit.Node($"Slot{i}", g, new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-532 + i * 46, 216), new Vector2(40, 26));
                s.bg = UiKit.Img("Bg", rt, UiKit.Skew, new Color(0.02f, 0.05f, 0.08f, 0.7f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 26));
                s.frame = UiKit.Img("Line", rt, UiKit.White, A(Cyan, 0.4f), new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 2));
                s.num = UiKit.Label("Num", rt, (i + 1).ToString(), 16, UiKit.TextColor, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(40, 24), false);
                slots[i] = s;
            }
        }

        // ------------------------------------------------------------------ top

        void BuildTop(RectTransform g)
        {
            var p = GlassPanel("Objective", g, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -34), new Vector2(330, 74), 0.3f);
            UiKit.Label("Kicker", p, UiKit.Spaced("OBJECTIVE"), 12, A(Cyan, 0.75f), TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -11), new Vector2(200, 16), false);
            objectiveText = UiKit.Label("Text", p, "", 20, UiKit.TextColor, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -28), new Vector2(300, 24), false);
            objTicks = new Image[6];
            for (int i = 0; i < objTicks.Length; i++)
            {
                objTicks[i] = UiKit.Img($"Tick{i}", p, UiKit.Skew, A(HealthCol, 0.2f), new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(22 + i * 38, 13), new Vector2(34, 5));
                objTicks[i].gameObject.SetActive(false);
            }

            var area = UiKit.Node("Area", root, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(560, 40));
            areaGroup = area.gameObject.AddComponent<CanvasGroup>();
            areaGroup.alpha = 0f;
            areaText = UiKit.Label("Text", area, "", 26, UiKit.TextColor, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 36), false);
            for (int s = -1; s <= 1; s += 2)
                UiKit.Img("Wing", area, UiKit.FadeLine, A(Cyan, 0.6f), new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(s * 190, 0), new Vector2(90, 1.5f)).rectTransform.localScale = new Vector3(s, 1, 1);

            threatBox = UiKit.Node("Threat", root, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -78), new Vector2(330, 34));
            UiKit.Img("Glow", threatBox, HoloKit.SoftDot, A(UiKit.Danger, 0.22f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(480, 80));
            UiKit.Img("Fill", threatBox, UiKit.Skew, new Color(0.25f, 0.02f, 0.02f, 0.7f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(330, 34));
            UiKit.Img("Line", threatBox, UiKit.White, A(UiKit.Danger, 0.9f), new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280, 1.5f));
            UiKit.Img("Warn", threatBox, HoloKit.Warn, UiKit.Danger, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(54, 1), new Vector2(22, 22));
            threatText = UiKit.Label("Text", threatBox, "", 18, UiKit.Danger, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(12, 1), new Vector2(280, 28), false);

            UiKit.KeyCap("HKey", root, "H", new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -34), 14);
            UiKit.Label("ControlsHint", root, UiKit.Spaced("CONTROLS"), 14, A(UiKit.TextColor, 0.55f), TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(74, -34), new Vector2(200, 26), false);
        }

        // ------------------------------------------------------------------ reticle / banner / prompt / overlays

        void BuildCrosshair()
        {
            crosshair = UiKit.Node("Crosshair", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
            ammoArcBg = HoloKit.ArcImage("AmmoBg", crosshair, 66, A(Cyan, 0.14f), 45f);
            ammoArcBg.fillAmount = 0.25f;
            ammoArc = HoloKit.ArcImage("Ammo", crosshair, 66, A(Cyan, 0.85f), 45f);
            reticleAmmo = UiKit.Label("Count", crosshair, "", 14, A(Cyan, 0.85f), TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(36, -24), new Vector2(80, 18), false);
            for (int i = 0; i < 4; i++)
            {
                bool vertical = i % 2 == 0;
                var img = UiKit.Img($"CH{i}", crosshair, UiKit.White, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, vertical ? new Vector2(2f, 11) : new Vector2(11, 2f));
                var o = img.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0, 0, 0, 0.5f);
                o.effectDistance = new Vector2(1f, -1f);
                chLines[i] = img.rectTransform;
                chImages[i] = img;
            }
            chDot = UiKit.Img("Dot", crosshair, UiKit.Circle, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3.5f, 3.5f));
            for (int i = 0; i < 4; i++)
            {
                var hm = UiKit.Img($"HM{i}", crosshair, UiKit.White, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3, 14));
                hmLines[i] = hm.rectTransform;
                hmImages[i] = hm;
                hm.enabled = false;
            }
            reloadRing = HoloKit.ArcImage("ReloadRing", crosshair, 84, UiKit.Amber, 0f);
            reloadRing.enabled = false;
        }

        void BuildBanner()
        {
            // sits in the upper quarter, under the area / threat / extraction readouts, so it never covers the character or the fight
            var b = UiKit.Node("Banner", root, new Vector2(0.5f, 0.765f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200, 120));
            bannerGroup = b.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            UiKit.Img("Backdrop", b, UiKit.Glow, new Color(0f, 0.01f, 0.03f, 0.45f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -4), new Vector2(760, 124));
            bannerKicker = UiKit.Label("Kicker", b, "", 13, Cyan, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 34), new Vector2(800, 18), false);
            bannerTitle = UiKit.Label("Title", b, "", 40, UiKit.TextColor, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 3), new Vector2(1200, 50), false);
            bannerLineL = UiKit.Img("LineL", b, UiKit.FadeLine, Cyan, new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(-6, -24), new Vector2(300, 1.5f)).rectTransform;
            bannerLineL.localScale = new Vector3(-1, 1, 1);
            bannerLineR = UiKit.Img("LineR", b, UiKit.FadeLine, Cyan, new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(6, -24), new Vector2(300, 1.5f)).rectTransform;
            bannerSub = UiKit.Label("Sub", b, "", 17, Dim, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -42), new Vector2(1000, 22), false);
        }

        void BuildPrompt()
        {
            promptRect = UiKit.Node("Prompt", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 40));
            UiKit.Fill("Fill", promptRect, UiKit.PanelFill, Glass);
            promptFrame = UiKit.Fill("Frame", promptRect, UiKit.PanelFrame, A(Cyan, 0.5f));
            UiKit.KeyCap("Key", promptRect, "E", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), 16);
            promptText = UiKit.Label("Text", promptRect, "", 18, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(44, 0), new Vector2(400, 30), false);
            promptRect.gameObject.SetActive(false);
        }

        void BuildControls()
        {
            var p = GlassPanel("Controls", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -66), new Vector2(390, 362));
            controls = p.gameObject;
            controlsGroup = controls.AddComponent<CanvasGroup>();
            UiKit.Label("Title", p, UiKit.Spaced("CONTROLS"), 16, Cyan, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -16), new Vector2(300, 20), false);
            var rows = new[]
            {
                ("WASD", "Move"), ("MOUSE", "Aim"), ("LMB", "Fire"), ("R", "Recharge cell"), ("SPACE", "Dash"),
                ("G", "Grenade (hold, release)"), ("Q", "Scan pulse"), ("E", "Interact / open crate"), ("TAB", "Inventory"), ("1 2 3", "Switch weapon"),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                UiKit.KeyCap("Key", p, rows[i].Item1, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -48 - i * 30), 15);
                UiKit.Label("Action", p, rows[i].Item2, 18, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(120, -48 - i * 30 - 1), new Vector2(250, 26), false);
            }
            controlsGroup.alpha = 0f;
            controlsShownUntil = 6.5f;
        }

        void BuildGameOver()
        {
            gameOver = UiKit.Stretch("GameOver", root).gameObject;
            gameOverGroup = gameOver.AddComponent<CanvasGroup>();
            UiKit.Fill("Dim", gameOver.transform, UiKit.White, new Color(0.02f, 0f, 0f, 0.7f));
            var p = GlassPanel("Card", gameOver.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 260), 0.5f);
            UiKit.Label("Kicker", p, UiKit.Spaced("OPERATIVE DOWN"), 15, UiKit.Danger, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(600, 20), false);
            UiKit.Label("Title", p, UiKit.Spaced("SIGNAL LOST"), 62, UiKit.TextColor, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(640, 70), false);
            gameOverStats = UiKit.Label("Stats", p, "", 18, Dim, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(600, 24), false);
            UiKit.KeyCap("Key", p, "R", new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(-46, 30), 16);
            UiKit.Label("Redeploy", p, UiKit.Spaced("REDEPLOY"), 18, Cyan, TextAnchor.MiddleLeft, new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(-34, 30), new Vector2(200, 26), false);
            gameOver.SetActive(false);
        }

        // ================================================================== public API

        public void Banner(string title, string sub) => Banner(title, sub, null);

        public void Banner(string title, string sub, string kicker)
        {
            bannerKicker.text = UiKit.Spaced(kicker ?? "AREA UPDATE");
            bannerTitle.text = UiKit.Spaced(title);
            bannerSub.text = sub;
            bannerT = 0f;
        }

        public void Hitmarker(bool kill, bool crit)
        {
            hitKill = kill;
            hitT = hitDur = kill ? 0.3f : 0.12f;
            chPunch = kill ? 1f : 0.5f;
            foreach (var img in hmImages)
            {
                img.enabled = true;
                img.color = kill ? UiKit.Danger : crit ? UiKit.Amber : Color.white;
            }
        }

        public void OnPlayerDamaged(float amount) => OnPlayerDamaged(amount, Vector3.zero);

        /// <summary>hitDir: direction the hit travelled (source -> player); shows a red arc toward the source.</summary>
        public void OnPlayerDamaged(float amount, Vector3 hitDir)
        {
            damageFlash = Mathf.Clamp01(damageFlash + 0.25f + amount / 80f);
            chipDelay = 0.45f;
            shieldFlash = 1f;
            hitDir.y = 0f;
            if (hitDir.sqrMagnitude < 0.001f) return;
            DmgArc a = null;
            foreach (var d in dmgArcs) if (d.t <= 0f) { a = d; break; }
            if (a == null)
            {
                a = new DmgArc { img = UiKit.Img("DmgArc", dmgLayer, HoloKit.Crescent, UiKit.Danger, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(380, 130)) };
                dmgArcs.Add(a);
            }
            a.dir = -hitDir.normalized;
            a.t = 1.1f;
        }

        public void ShieldBroken() { shieldAlertT = 1.8f; shieldFlash = 1f; }

        public void DamageNumber(Vector3 world, float amount, bool crit)
        {
            var f = Spawn(world, Mathf.RoundToInt(amount).ToString(), crit ? new Color(1f, 0.5f, 0.3f) : HealthCol, crit ? 38 : 28);
            f.scale = crit ? 1.8f : 1.35f;
        }

        public void WorldText(Vector3 world, string text, Color color, int size)
        {
            var f = Spawn(world, text, color, size);
            f.vel = Vector3.up * 1.5f;
            f.life = 0.9f;
        }

        public void ShowGameOver(int kills, int score)
        {
            gameOver.SetActive(true);
            gameOverGroup.alpha = 0f;
            gameOverStats.text = $"ELIMINATIONS  <color=#{UiKit.Hex(UiKit.TextColor)}>{kills}</color>          BACKPACK  <color=#{UiKit.Hex(UiKit.Danger)}>LOST</color>";
            gameOver.transform.SetAsLastSibling();
            Cursor.visible = true;
        }

        Floater Spawn(Vector3 world, string text, Color color, int size)
        {
            Floater f;
            if (floaters.Count < 40)
            {
                f = new Floater();
                f.text = UiKit.Label("Floater", worldLayer, "", size, color, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240, 50));
                var o = f.text.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0, 0, 0, 0.55f);
                o.effectDistance = new Vector2(1.2f, -1.2f);
                f.rt = f.text.rectTransform;
                floaters.Add(f);
            }
            else
            {
                f = floaters[floaterCursor];
                floaterCursor = (floaterCursor + 1) % floaters.Count;
            }
            f.text.text = text;
            f.text.color = color;
            f.text.fontSize = size;
            f.world = world + Random.insideUnitSphere * 0.25f;
            f.vel = new Vector3(Random.Range(-1.2f, 1.2f), 3.2f, Random.Range(-1.2f, 1.2f));
            f.t = 0f;
            f.life = 0.75f;
            f.scale = 1.3f;
            f.active = true;
            f.rt.gameObject.SetActive(true);
            f.rt.SetAsLastSibling();
            return f;
        }

        // ================================================================== update

        Camera UiCam => Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Canvas.worldCamera;

        bool ToLocal(Camera cam, Vector3 world, out Vector2 local)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            local = Vector2.zero;
            if (sp.z < 0f) return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, UiCam, out local);
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            startT += dt;
            var player = PlayerController.I;
            var cam = CameraRig.I ? CameraRig.I.Cam : Camera.main;
            bool uiOpen = player && player.inventory && player.inventory.PanelOpen;

            float intro = Mathf.Clamp01((startT - 0.2f) / 0.6f);
            foreach (var g in new[] { bottomL, bottomR, topR })
                g.alpha = Mathf.MoveTowards(g.alpha, uiOpen ? 0f : intro, dt * 6f);

            if (player)
            {
                UpdateVitals(player, dt);
                UpdateSkills(player, dt);
                UpdateWeapon(player, dt);
                UpdateCrosshair(player, cam, dt, uiOpen);
                UpdatePrompt(player, cam, uiOpen);
            }
            UpdateTop(dt, uiOpen);
            if (cam) UpdateWorldMarkers(player, cam, uiOpen, dt);
            UpdateDamageArcs(cam, dt, uiOpen);
            UpdateBanner(dt, uiOpen);
            UpdateControls(dt);

            damageFlash = Mathf.MoveTowards(damageFlash, 0f, dt * 1.8f);
            float low = 0f;
            if (player && player.health && player.health.IsAlive && player.health.Health01 < 0.35f)
                low = (0.35f - player.health.Health01) / 0.35f * (0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f));
            vignette.color = new Color(1f, 0.08f, 0.04f, Mathf.Clamp01(damageFlash * 0.55f + low * 0.8f));

            if (gameOver.activeSelf) gameOverGroup.alpha = Mathf.MoveTowards(gameOverGroup.alpha, 1f, dt * 1.5f);
            UpdateFloaters(cam, dt);
        }

        void UpdateVitals(PlayerController player, float dt)
        {
            var h = player.health;
            if (!h) return;
            float v = h.Health01;
            chipDelay -= dt;
            if (chip < v) chip = v;
            else if (chipDelay <= 0f) chip = Mathf.MoveTowards(chip, v, dt * 0.7f);

            Color full = v < 0.25f ? UiKit.Danger : HealthCol;
            if (v < 0.25f) full = Color.Lerp(full, Color.white, Mathf.PingPong(Time.unscaledTime * 3f, 0.4f));
            FillSegments(hpSegs, v, chip, full, -1f);
            hpValue.text = Mathf.CeilToInt(h.Health).ToString();
            hpValue.color = full;

            float s = h.Shield01;
            shieldFlash = Mathf.MoveTowards(shieldFlash, 0f, dt * 3f);
            Color sc = Color.Lerp(Cyan, Color.white, shieldFlash * 0.7f);
            float shimmer = s > 0f && s < 1f ? Time.unscaledTime * 2.5f : -1f;      // regenerating: a glint runs along the bar
            FillSegments(shSegs, s, s, sc, shimmer);
            shValue.text = Mathf.CeilToInt(h.Shield).ToString();
            shValue.color = s > 0f ? Cyan : A(UiKit.Danger, 0.9f);
            portraitFrame.color = Color.Lerp(A(Cyan, s > 0f ? 0.8f : 0.3f), UiKit.Danger, damageFlash);
            shieldAlertT = Mathf.Max(0f, shieldAlertT - dt);
            shieldAlert.color = A(UiKit.Danger, shieldAlertT > 0f ? Mathf.Clamp01(shieldAlertT) * (0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 16f)) : 0f);
        }

        static void FillSegments(Image[] segs, float v, float chip, Color full, float shimmer)
        {
            int n = segs.Length;
            float sh = shimmer >= 0f ? (shimmer % 1.6f) * n : -10f;
            for (int i = 0; i < n; i++)
            {
                float a = (i + 1f) / n, b0 = i / (float)n;
                Color c;
                if (v >= a) c = full;
                else if (v > b0) c = Color.Lerp(new Color(1, 1, 1, 0.1f), full, (v - b0) * n);
                else if (chip > b0) c = new Color(1f, 0.95f, 0.85f, 0.7f);
                else c = new Color(1f, 1f, 1f, 0.07f);
                float glint = Mathf.Clamp01(1f - Mathf.Abs(i - sh) / 1.5f);
                if (glint > 0f && v > b0) c = Color.Lerp(c, Color.white, glint * 0.6f);
                segs[i].color = c;
            }
        }

        void UpdateSkills(PlayerController player, float dt)
        {
            void Apply(SkillUi s, float ready01, bool ready, bool active, Color activeCol)
            {
                s.ring.fillAmount = ready01;
                s.ring.color = active ? activeCol : ready ? A(Cyan, 0.95f) : A(Cyan, 0.45f);
                s.icon.color = active ? activeCol : ready ? UiKit.TextColor : new Color(1, 1, 1, 0.35f);
                s.fill.color = ready ? new Color(0.03f, 0.09f, 0.12f, 0.8f) : new Color(0.02f, 0.03f, 0.05f, 0.75f);
            }
            var gr = player.grenades;
            if (gr)
            {
                float r = gr.Charges >= gr.maxCharges ? 1f : gr.RechargeProgress;
                Apply(grenade, gr.Charges > 0 ? 1f : r, gr.Charges > 0, gr.IsAiming, UiKit.Amber);
                for (int i = 0; i < grenade.pips.Length; i++)
                    grenade.pips[i].color = i < gr.Charges ? Cyan : new Color(1, 1, 1, 0.15f);
            }
            Apply(dash, 1f - player.DashCooldown01, player.DashCooldown01 <= 0f, player.IsDashing, Cyan);
            if (player.IsDashing) dash.punch = 1f;
            var sp = player.GetComponent<ScanPulse>();
            if (sp)
            {
                Apply(scanSkill, 1f - sp.Cooldown01, sp.Ready, false, Cyan);
                if (sp.Cooldown01 > 0.97f) scanSkill.punch = 1f;
            }
            foreach (var s in new[] { grenade, dash, scanSkill })
            {
                s.punch = Mathf.MoveTowards(s.punch, 0f, dt * 5f);
                float k = 1f + s.punch * 0.12f;
                s.rt.localScale = new Vector3(k, k, 1f);
            }
        }

        void UpdateWeapon(PlayerController player, float dt)
        {
            var g = player.gun;
            var def = g ? g.Current : null;
            if (def == null)
            {
                if (shownWeapon != null || weaponName.text == "")
                {
                    shownWeapon = null;
                    weaponName.text = UiKit.Spaced("UNARMED");
                    ammoText.text = "--";
                    ammoMax.text = "";
                    weaponState.text = "";
                    cellFill.fillAmount = cellChip.fillAmount = 0f;
                }
            }
            else
            {
                if (shownWeapon != def.kind)
                {
                    shownWeapon = def.kind;
                    weaponName.text = UiKit.Spaced(def.displayName);
                    ammoPunch = 1f;
                    lastAmmo = -1;
                }
                float frac = g.MagazineSize > 0 ? g.Ammo / (float)g.MagazineSize : 0f;
                Color ac = g.Ammo == 0 ? UiKit.Danger : frac < 0.3f ? UiKit.Amber : Cyan;
                if (g.Ammo != lastAmmo)
                {
                    if (lastAmmo >= 0 && g.Ammo < lastAmmo) ammoPunch = Mathf.Max(ammoPunch, 0.5f);
                    lastAmmo = g.Ammo;
                }
                ammoText.text = g.Ammo.ToString();
                ammoText.color = ac;
                ammoMax.text = $"/ {g.MagazineSize}";
                ammoPunch = Mathf.MoveTowards(ammoPunch, 0f, dt * 6f);
                float s = 1f + ammoPunch * 0.08f;
                ammoText.rectTransform.localScale = new Vector3(s, s, 1f);
                bool reloading = g.IsReloading;
                float shown = reloading ? g.ReloadProgress : frac;
                cellFill.fillAmount = shown;
                cellFill.color = reloading ? UiKit.Amber : ac;
                cellChipV = cellChipV < shown ? shown : Mathf.MoveTowards(cellChipV, shown, dt * 0.8f);
                cellChip.fillAmount = cellChipV;
                weaponState.text = reloading ? UiKit.Spaced("RECHARGING") : g.Ammo == 0 ? UiKit.Spaced("CELL EMPTY  [R]") : g.IsBeaming ? UiKit.Spaced("BEAM ACTIVE") : "";
                weaponState.color = g.IsBeaming ? def.color : UiKit.Amber;
            }

            var inv = player.inventory;
            if (inv && inv.Version != lastInvVersion)
            {
                lastInvVersion = inv.Version;
                for (int i = 0; i < slots.Length; i++)
                {
                    var st = i < inv.WeaponSlots.Size ? inv.WeaponSlots[i] : ItemStack.Empty;
                    bool on = i == inv.EquippedIndex && !st.IsEmpty;
                    var sl = slots[i];
                    sl.bg.color = on ? A(Cyan, 0.85f) : new Color(0.02f, 0.05f, 0.08f, 0.7f);
                    sl.frame.color = st.IsEmpty ? new Color(1, 1, 1, 0.1f) : on ? Cyan : A(Cyan, 0.4f);
                    sl.num.color = on ? new Color(0.02f, 0.06f, 0.08f) : st.IsEmpty ? new Color(1, 1, 1, 0.25f) : UiKit.TextColor;
                }
            }
        }

        void UpdateTop(float dt, bool uiOpen)
        {
            var gm = GameManager.I;
            if (!gm) return;
            string area = gm.CurrentArea;
            if (area != lastArea) { lastArea = area; areaText.text = UiKit.Spaced(area); areaT = 0f; }
            if (areaT >= 0f) areaT += dt;
            float aa = areaT < 0f ? 0f : areaT < 0.4f ? areaT / 0.4f : areaT < 4f ? 1f : Mathf.Max(0.45f, 1f - (areaT - 4f) / 1f);
            areaGroup.alpha = uiOpen ? 0f : aa;
            int engaged = gm.AlertedHostiles;
            threatBox.gameObject.SetActive(engaged > 0 && !uiOpen);
            if (engaged > 0)
            {
                threatText.text = UiKit.Spaced($"{engaged} HOSTILE{(engaged > 1 ? "S" : "")} ENGAGED");
                threatText.color = A(UiKit.Danger, 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 7f));
            }
            int total = gm.CampsTotal, cleared = gm.CampsCleared;
            objectiveText.text = total > 0 && cleared >= total
                ? $"{UiKit.Spaced("KEPLER-7 SECURED")}  <color=#{UiKit.Hex(HealthCol)}>{cleared}/{total}</color>"
                : $"{UiKit.Spaced("CLEAR XENO CAMPS")}  <color=#{UiKit.Hex(HealthCol)}>{cleared}/{total}</color>";
            for (int i = 0; i < objTicks.Length; i++)
            {
                objTicks[i].gameObject.SetActive(i < total);
                objTicks[i].color = i < cleared ? HealthCol : new Color(1, 1, 1, 0.14f);
            }
        }

        /// <summary>Slim enemy health bars (alerted / recently hit / scanned) and scan-pulse markers.</summary>
        void UpdateWorldMarkers(PlayerController player, Camera cam, bool uiOpen, float dt)
        {
            foreach (var b in bars) b.used = false;
            foreach (var m in markers) m.used = false;
            if (!uiOpen)
            {
                float now = Time.time;
                foreach (var a in Alien.All)
                {
                    if (!a || !a.IsAlive) continue;
                    bool revealed = a.RevealedUntil > now;
                    bool show = a.IsAlerted || now - a.LastHitTime < 3f || revealed;
                    float h = 1.5f + a.deathSize * 0.7f;
                    if (show && ToLocal(cam, a.transform.position + Vector3.up * h, out var lp))
                    {
                        if (!barOf.TryGetValue(a, out var b) || b.used) { b = GetBar(); barOf[a] = b; b.shown = a.Health01; }
                        b.used = true;
                        b.rt.anchoredPosition = lp;
                        b.fill.fillAmount = a.Health01;
                        b.shown = b.shown < a.Health01 ? a.Health01 : Mathf.MoveTowards(b.shown, a.Health01, dt * 0.9f);
                        b.chip.fillAmount = b.shown;
                        b.rt.sizeDelta = new Vector2(a.type == AlienType.Brute ? 84 : 56, 5);
                    }
                    if (revealed && ToLocal(cam, a.transform.position + Vector3.up * (h + 0.4f), out var mp))
                    {
                        float dist = player ? Vector3.Distance(player.transform.position, a.transform.position) : 0f;
                        var m = GetMarker();
                        m.rt.anchoredPosition = mp;
                        m.icon.color = UiKit.Danger;
                        m.text.text = $"{dist:0}m";
                        m.text.color = UiKit.Danger;
                    }
                }
                var sp = player ? player.GetComponent<ScanPulse>() : null;
                if (sp && sp.CratesRevealedUntil > now && chests != null)
                    foreach (var c in chests)
                    {
                        if (!c || !c.CanInteract) continue;
                        if ((c.transform.position - player.transform.position).sqrMagnitude > sp.radius * sp.radius) continue;
                        if (!ToLocal(cam, c.transform.position + Vector3.up * 1.6f, out var cp)) continue;
                        var m = GetMarker();
                        m.rt.anchoredPosition = cp;
                        m.icon.color = UiKit.Amber;
                        m.text.text = "LOOT";
                        m.text.color = UiKit.Amber;
                    }
            }
            foreach (var b in bars) b.rt.gameObject.SetActive(b.used);
            foreach (var m in markers) m.rt.gameObject.SetActive(m.used);
            List<Alien> stale = null;
            foreach (var kv in barOf) if (!kv.Key || !kv.Value.used) (stale ??= new List<Alien>()).Add(kv.Key);
            if (stale != null) foreach (var a in stale) barOf.Remove(a);
        }

        Bar GetBar()
        {
            foreach (var b in bars) if (!b.used && !barOf.ContainsValue(b)) { b.used = true; return b; }
            var nb = new Bar();
            nb.rt = UiKit.Node("EnemyBar", worldLayer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 5));
            UiKit.Fill("Bg", nb.rt, UiKit.White, new Color(0.04f, 0f, 0f, 0.75f), -1.5f);
            nb.chip = UiKit.Fill("Chip", nb.rt, UiKit.White, new Color(1f, 0.85f, 0.75f, 0.7f));
            nb.chip.type = Image.Type.Filled; nb.chip.fillMethod = Image.FillMethod.Horizontal;
            nb.fill = UiKit.Fill("Fill", nb.rt, UiKit.White, UiKit.Danger);
            nb.fill.type = Image.Type.Filled; nb.fill.fillMethod = Image.FillMethod.Horizontal;
            nb.used = true;
            bars.Add(nb);
            return nb;
        }

        Marker GetMarker()
        {
            foreach (var m in markers) if (!m.used) { m.used = true; return m; }
            var nm = new Marker();
            nm.rt = UiKit.Node("ScanMarker", worldLayer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60, 40));
            nm.icon = UiKit.Img("Diamond", nm.rt, UiKit.Diamond, UiKit.Danger, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(12, 12));
            nm.text = UiKit.Label("Dist", nm.rt, "", 13, UiKit.Danger, TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(80, 18), false);
            nm.used = true;
            markers.Add(nm);
            return nm;
        }

        void UpdateDamageArcs(Camera cam, float dt, bool uiOpen)
        {
            float camYaw = cam ? cam.transform.eulerAngles.y : 0f;
            foreach (var a in dmgArcs)
            {
                if (a.t <= 0f) { a.img.enabled = false; continue; }
                a.t -= dt;
                a.img.enabled = !uiOpen;
                Vector3 r = Quaternion.Euler(0, -camYaw, 0) * a.dir;
                float ang = Mathf.Atan2(r.x, r.z) * Mathf.Rad2Deg;
                a.img.rectTransform.localRotation = Quaternion.Euler(0, 0, -ang);
                a.img.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(ang * Mathf.Deg2Rad), Mathf.Cos(ang * Mathf.Deg2Rad)) * 300f;
                a.img.color = A(UiKit.Danger, Mathf.Clamp01(a.t / 0.6f) * 0.6f);
            }
        }

        void UpdateBanner(float dt, bool uiOpen)
        {
            if (bannerT < 0f) return;
            bannerT += dt;
            float a = bannerT < 0.3f ? bannerT / 0.3f : bannerT > 2.6f ? 1f - (bannerT - 2.6f) / 0.6f : 1f;
            bannerGroup.alpha = uiOpen ? 0f : Mathf.Clamp01(a);
            float grow = Ease.OutCubic(Mathf.Clamp01(bannerT / 0.6f));
            bannerLineL.sizeDelta = new Vector2(320f * grow, 1.5f);
            bannerLineR.sizeDelta = new Vector2(320f * grow, 1.5f);
            float slide = (1f - Ease.OutCubic(Mathf.Clamp01(bannerT / 0.4f))) * 14f;
            bannerTitle.rectTransform.anchoredPosition = new Vector2(0, 3 - slide);
            if (bannerT > 3.2f) bannerT = -1f;
        }

        void UpdateControls(float dt)
        {
            if (PlayerInputSource.Current.help)
                controlsShownUntil = controlsGroup.alpha > 0.5f && Time.timeSinceLevelLoad < controlsShownUntil ? 0f : float.MaxValue;
            bool pinned = controlsShownUntil == float.MaxValue;
            bool fighting = GameManager.I && GameManager.I.AlertedHostiles > 0;
            float target = Time.timeSinceLevelLoad < controlsShownUntil && startT > 0.8f && (pinned || !fighting) ? 1f : 0f;
            controlsGroup.alpha = Mathf.MoveTowards(controlsGroup.alpha, target, dt * 3f);
            controls.SetActive(controlsGroup.alpha > 0.01f);
        }

        void UpdatePrompt(PlayerController player, Camera cam, bool uiOpen)
        {
            var interactor = player.GetComponent<PlayerInteractor>();
            var focus = interactor ? interactor.Focus : null;
            if (Interactables.IsGone(focus) || uiOpen) focus = null;
            promptRect.gameObject.SetActive(focus != null && cam);
            if (focus == null || !cam) return;
            if (!ToLocal(cam, focus.PromptPosition + Vector3.up * 0.6f, out var local)) return;
            promptRect.anchoredPosition = local;
            promptText.text = UiKit.Spaced(focus.Prompt.Replace("[E]", "").Trim());
            promptText.color = focus.PromptColor;
            promptFrame.color = A(focus.PromptColor, 0.55f);
            promptRect.sizeDelta = new Vector2(promptText.preferredWidth + 62f, 40f);
        }

        void UpdateCrosshair(PlayerController player, Camera cam, float dt, bool uiOpen)
        {
            if (!cam) return;
            Vector2 mouse = PlayerInputSource.Current.pointer;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, mouse, UiCam, out Vector2 local);
            crosshair.anchoredPosition = local;
            bool alive = player.CanAct && !uiOpen;
            crosshair.gameObject.SetActive(alive);
            Cursor.visible = uiOpen || !player.CanAct;
            if (!alive) return;

            float spreadPx = 8f;
            var gun = player.gun;
            if (gun && gun.Current != null)
            {
                Vector3 aim = player.AimPoint;
                float d = Vector3.Distance(new Vector3(player.transform.position.x, aim.y, player.transform.position.z), aim);
                Vector3 side = Vector3.Cross(Vector3.up, player.AimDir) * Mathf.Tan(gun.CurrentSpread * Mathf.Deg2Rad) * d;
                Vector3 a = cam.WorldToScreenPoint(aim);
                Vector3 b = cam.WorldToScreenPoint(aim + side);
                spreadPx = Mathf.Max(5f, Vector2.Distance(a, b) / Canvas.scaleFactor);
                float frac = gun.MagazineSize > 0 ? gun.Ammo / (float)gun.MagazineSize : 0f;
                Color ac = gun.Ammo == 0 ? UiKit.Danger : frac < 0.3f ? UiKit.Amber : Cyan;
                ammoArc.fillAmount = 0.25f * frac;
                ammoArc.color = A(ac, 0.85f);
                ammoArcBg.enabled = ammoArc.enabled = true;
                reticleAmmo.text = gun.Ammo.ToString();
                reticleAmmo.color = A(ac, 0.85f);
            }
            else { ammoArcBg.enabled = ammoArc.enabled = false; reticleAmmo.text = ""; }
            chPunch = Mathf.MoveTowards(chPunch, 0f, dt * 6f);
            float gap = spreadPx + 6f + chPunch * 8f;
            chLines[0].anchoredPosition = new Vector2(0, gap);
            chLines[1].anchoredPosition = new Vector2(gap, 0);
            chLines[2].anchoredPosition = new Vector2(0, -gap);
            chLines[3].anchoredPosition = new Vector2(-gap, 0);

            bool reloading = gun && gun.IsReloading;
            Color tint = gun && gun.Current != null ? Color.Lerp(Cyan, gun.Current.color, 0.2f) : Cyan;
            Color chColor = reloading ? new Color(1, 1, 1, 0.3f) : Color.Lerp(Color.white, tint, 0.55f);
            foreach (var img in chImages) img.color = chColor;
            chDot.color = chColor;
            reloadRing.enabled = reloading;
            if (reloading) reloadRing.fillAmount = gun.ReloadProgress;

            if (hitT > 0f)
            {
                hitT -= dt;
                float k = Mathf.Clamp01(hitT / hitDur);
                float dist = (hitKill ? 15f : 11f) + (1f - k) * 6f;
                for (int i = 0; i < 4; i++)
                {
                    float ang = (45f + 90f * i) * Mathf.Deg2Rad;
                    hmLines[i].anchoredPosition = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                    hmLines[i].localRotation = Quaternion.Euler(0, 0, 45f + 90f * i - 90f);
                    hmLines[i].sizeDelta = new Vector2(hitKill ? 3.5f : 2.5f, hitKill ? 18f : 12f);
                    var c = hmImages[i].color; c.a = k; hmImages[i].color = c;
                }
                if (hitT <= 0f) foreach (var img in hmImages) img.enabled = false;
            }
        }

        void UpdateFloaters(Camera cam, float dt)
        {
            if (!cam) return;
            foreach (var f in floaters)
            {
                if (!f.active) continue;
                f.t += dt;
                if (f.t >= f.life) { f.active = false; f.rt.gameObject.SetActive(false); continue; }
                f.vel += Vector3.down * 9f * dt;
                f.world += f.vel * dt;
                if (!ToLocal(cam, f.world, out var local)) { f.rt.gameObject.SetActive(false); continue; }
                f.rt.anchoredPosition = local;
                float k = f.t / f.life;
                float s = k < 0.15f ? Mathf.Lerp(f.scale, 1f, k / 0.15f) : 1f;
                f.rt.localScale = new Vector3(s, s, 1f);
                var c = f.text.color;
                c.a = k > 0.6f ? 1f - (k - 0.6f) / 0.4f : 1f;
                f.text.color = c;
            }
        }
    }
}
