using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Zcom
{
    /// <summary>
    /// Tac-link visor HUD, built at runtime. The side clusters are bent like print on a curved visor (VisorWarp),
    /// lag behind the operative's motion (helmet inertia), glitch on hits and boot up with a scan sweep:
    ///  bottom-left   live 3D helmet portrait, name, shield + health bars over a tick ruler, skill tiles
    ///  bottom-right  loadout tabs, live 3D weapon render, decoded name, big cell count over a per-shot tick strip
    ///  top-right     objective tracker;  top-centre  decoded area name + grid readout + hostiles alert
    ///  world         reticle with ammo arc, ground vitals ring (PlayerRing), enemy bars, damage numbers, markers
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public static Hud I { get; private set; }
        public Canvas Canvas { get; private set; }

        /// <summary>Line / reticle colour of the active design (cyan hologram, or paper white for the field kit).</summary>
        static Color Cyan => UiStyle.Kit ? FieldKit.Paper : TacKit.Cyan;
        FieldHud field;
        static readonly Color HealthCol = new Color(1f, 0.72f, 0.26f);
        static readonly Color Glass = new Color(0.02f, 0.045f, 0.07f, 0.62f);
        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);
        static Color Dim => UiKit.TextDim;

        RectTransform root, worldLayer, dmgLayer;
        CanvasGroup bottomL, bottomR, topR;
        float startT;

        // visor dynamics
        class Cluster { public CanvasGroup g; public RectTransform rt; public Vector2 off, vel; public float boot; }
        readonly List<Cluster> clusters = new List<Cluster>();
        readonly List<RawImage> scanlines = new List<RawImage>();
        Vector2 kick;
        float glitchT, bootSweepT;
        RectTransform bootSweep;
        Text bootText;
        PlayerRing ring;

        // vitals
        Image suit;
        RectTransform suitScan;
        Image portraitFrame;
        Text hpValue, shValue, shieldAlert, linkText;
        Image[] hpSegs, shSegs;
        Image hpGlow, shGlow;
        const float VitalBarW = 324f;
        /// <summary>Design A icon family (Resources/UI/a_*), falling back to the older sheet.</summary>
        static Sprite Icon(string name, string fallback) => UiKit.Art(name) ?? UiKit.Art(fallback);
        float chip = 1f, chipDelay, shieldFlash, shieldAlertT;

        class SkillUi { public RectTransform rt; public Image icon, frame, fill, line; public Image[] pips; public float punch; public bool wasReady = true; }
        SkillUi grenade, dash, scanSkill;

        // weapon
        RawImage weaponView;
        Image weaponArt;
        Text weaponName, ammoText, ammoMax, weaponState, fireMode;
        DecodeText weaponDecode;
        RectTransform tickStrip;
        readonly List<Image> ticks = new List<Image>();
        float[] tickFlash = new float[0];
        class Slot { public RectTransform rt; public Image bg, frame, line, icon; public Text num; public RawImage hatch; }
        readonly Slot[] slots = new Slot[3];
        int lastInvVersion = -1, lastAmmo = -1, tickCount = -1;
        float ammoPunch;
        WeaponKind? shownWeapon;

        // top
        Text areaText, threatText, objectiveText, gridText;
        DecodeText areaDecode;
        CanvasGroup areaGroup;
        RectTransform threatBox;
        RawImage threatHatch;
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
        DecodeText bannerDecode;
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
            if (!UiStyle.Kit) ring = PlayerRing.Create();
            Cursor.visible = false;
        }

        void Start() { chests = FindObjectsByType<LootChest>(FindObjectsSortMode.None); }
        void OnDestroy()
        {
            Cursor.visible = true;
            if (ring) Destroy(ring.gameObject);
        }

        // ================================================================== construction

        CanvasGroup Group(string name, float bootAt)
        {
            var rt = UiKit.Stretch(name, root);
            var g = rt.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            clusters.Add(new Cluster { g = g, rt = rt, boot = bootAt });
            return g;
        }

        /// <summary>Visor glass plate with drifting scanlines.</summary>
        RectTransform Plate(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, float frameA = 0.38f)
        {
            if (UiStyle.Kit) return FieldKit.Block(name, parent, anchor, pivot, pos, size, FieldKit.Ink);
            var p = TacKit.GlassPlate(name, parent, anchor, pivot, pos, size, Cyan, frameA);
            var sl = HoloKit.Tiled("Scan", p, HoloKit.Scanlines, new Color(0.6f, 0.9f, 1f, 0.035f));
            sl.uvRect = new Rect(0, 0, size.x / 4f, size.y / 6f);
            scanlines.Add(sl);
            return p;
        }

        /// <summary>Frosted glass panel used by the banner/prompt/controls/game over cards.</summary>
        RectTransform GlassPanel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, float frameAlpha = 0.35f) =>
            Plate(name, parent, anchor, pivot, pos, size, frameAlpha);

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
            if (!UiStyle.Kit) UiKit.Fill("VisorFrame", root, HoloKit.Visor, A(Cyan, 0.09f), 6f);
            vignette = UiKit.Fill("Vignette", root, ProcTex.VignetteSprite, new Color(1f, 0.08f, 0.04f, 0f));
            worldLayer = UiKit.Stretch("WorldLayer", root);
            dmgLayer = UiKit.Node("DamageArcs", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            if (UiStyle.Kit)
            {
                field = new FieldHud();
                field.Build(root);
                UiKit.KeyCap("HKey", root, "H", new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -198), 14);
                UiKit.Label("ControlsHint", root, UiKit.Spaced("CONTROLS"), 14, A(UiKit.TextColor, 0.55f), TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(74, -198), new Vector2(200, 26), false);
            }
            else
            {
                bottomL = Group("Vitals", 0.45f);
                bottomR = Group("Weapon", 0.6f);
                topR = Group("Objective", 0.75f);
                BuildVitals((RectTransform)bottomL.transform);
                BuildWeapon((RectTransform)bottomR.transform);
                BuildTop((RectTransform)topR.transform);
                // visor curvature: each side cluster hinges on its inner edge and its outer edge leans towards the eye
                TacKit.Warp((RectTransform)bottomL.transform, new Vector2(0, 0), new Vector2(VitalsX + VitalsW, VitalsY + VitalsH * 0.5f), -9f);
                TacKit.Warp((RectTransform)bottomR.transform, new Vector2(1, 0), new Vector2(-WeaponX - WeaponW, WeaponY + WeaponH * 0.5f), 9f);
                TacKit.Warp((RectTransform)topR.transform, new Vector2(1, 1), new Vector2(-48 - 400, -40 - 49), 7f);
            }
            BuildCrosshair();
            BuildBanner();
            BuildPrompt();
            BuildControls();
            gameObject.AddComponent<InventoryUI>().Init(root, UiKit.Bold);
            BuildGameOver();
            gameObject.AddComponent<RaidUI>().Init(root);
            gameObject.AddComponent<BaseUI>().Init(root);
            if (!UiStyle.Kit) BuildBoot();
        }

        // ------------------------------------------------------------------ bottom-left

        const float VitalsX = 60f, VitalsY = 40f, VitalsW = 676f, VitalsH = 236f;

        static Text Font(Text t, Font f) { t.font = f; return t; }

        void BuildVitals(RectTransform g)
        {
            var p = Plate("VitalsPlate", g, new Vector2(0, 0), new Vector2(0, 0), new Vector2(VitalsX, VitalsY), new Vector2(VitalsW, VitalsH));

            // suit status schematic (tinted by the suit's condition) in a slim frame, a scan line running down it
            var sf = UiKit.Node("Suit", p, new Vector2(0, 0), new Vector2(0, 0), new Vector2(18, 18), new Vector2(120, 200));
            UiKit.Fill("Bg", sf, TacKit.Slot, new Color(0.015f, 0.05f, 0.075f, 0.75f));
            var sm = UiKit.Fill("Mask", sf, TacKit.Slot, Color.white);
            sm.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            UiKit.Img("Glow", sm.transform, HoloKit.SoftDot, A(Cyan, 0.2f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220, 280));
            suit = UiKit.Img("Figure", sm.transform, UiKit.Art("hud_suit"), Cyan, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(82, 184));
            suit.preserveAspect = true;
            suitScan = UiKit.Img("ScanLine", sm.transform, TacKit.GradUp, A(Cyan, 0.35f), new Vector2(0.5f, 1), new Vector2(0.5f, 0), Vector2.zero, new Vector2(120, 26)).rectTransform;
            var sscan = HoloKit.Tiled("Scan", sm.transform, HoloKit.Scanlines, new Color(0.6f, 0.95f, 1f, 0.05f));
            sscan.uvRect = new Rect(0, 0, 30, 33);
            scanlines.Add(sscan);
            portraitFrame = UiKit.Fill("Frame", sf, TacKit.SlotFrame, A(Cyan, 0.6f));
            TacKit.Brackets(sf, Cyan, 12f, 3f, true);

            const float x0 = 160f;
            var nm = Font(UiKit.Label("Name", p, UiKit.Spaced("ORBITAL MARSHAL"), 22, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(x0, 200), new Vector2(420, 30), true), TacKit.Display);
            for (int i = 0; i < 4; i++)
                UiKit.Img("NameTick" + i, p, UiKit.Skew, A(Cyan, 0.75f - i * 0.15f), new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(x0 + nm.preferredWidth + 16 + i * 9, 200), new Vector2(9, 11));
            linkText = TacKit.MicroText("Link", p, "LINK 99%", new Vector2(1, 0), new Vector2(1, 0.5f), new Vector2(-22, 200), TextAnchor.MiddleRight, 12);
            shieldAlert = Font(UiKit.Label("ShieldAlert", p, UiKit.Spaced("SHIELD DOWN"), 13, UiKit.Danger, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(x0, 178), new Vector2(220, 18), false), TacKit.Cap);
            shieldAlert.color = A(UiKit.Danger, 0f);

            // shield: thin bar; health: thick bar with the big readout; tick ruler underneath
            const float bx = x0 + 104f, bw = 324f;
            Font(UiKit.Label("ShdLbl", p, UiKit.Spaced("SHIELD"), 16, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(x0, 152), new Vector2(110, 22), true), TacKit.Cap);
            shGlow = UiKit.Img("ShieldGlow", p, HoloKit.SoftDot, A(Cyan, 0.2f), new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(bx - 14, 152), new Vector2(bw + 28, 40));
            shSegs = Bars(p, "Shield", new Vector2(bx, 152), bw, 12f, 16);
            shValue = Font(UiKit.Label("ShieldVal", p, "50", 24, Cyan, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(bx + bw + 20, 152), new Vector2(70, 28), true), TacKit.Num);
            Font(UiKit.Label("HpLbl", p, UiKit.Spaced("HEALTH"), 16, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(x0, 120), new Vector2(110, 22), true), TacKit.Cap);
            hpGlow = UiKit.Img("HealthGlow", p, HoloKit.SoftDot, A(HealthCol, 0.2f), new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(bx - 14, 120), new Vector2(bw + 28, 46));
            hpSegs = Bars(p, "Health", new Vector2(bx, 120), bw, 17f, 16);
            hpValue = Font(UiKit.Label("HP", p, "100", 34, HealthCol, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(bx + bw + 18, 121), new Vector2(90, 40), true), TacKit.Num);
            var ruler = HoloKit.Tiled("Ruler", p, TacKit.Ruler, A(Cyan, 0.3f));
            var rrt = ruler.rectTransform;
            rrt.anchorMin = rrt.anchorMax = Vector2.zero; rrt.pivot = new Vector2(0, 0.5f);
            rrt.anchoredPosition = new Vector2(bx, 98); rrt.sizeDelta = new Vector2(bw, 5);
            ruler.uvRect = new Rect(0, 0, bw / 8f / 1.5f, 1);
            for (int i = 0; i <= 4; i++)
                UiKit.Img("Major" + i, p, UiKit.White, A(Cyan, 0.55f), new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(bx + i * bw / 4f, 98), new Vector2(1.5f, 10));
            UiKit.Img("Rule", p, UiKit.FadeLine, A(Cyan, 0.3f), new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(x0, 82), new Vector2(VitalsW - x0 - 24, 1.5f));

            grenade = MakeSkill(p, "Grenade", new Vector2(x0, 20), "icon_grenade", "G", 4);
            dash = MakeSkill(p, "Dash", new Vector2(x0 + 166, 20), "icon_dash", "SPACE", 0);
            scanSkill = MakeSkill(p, "Scan", new Vector2(x0 + 332, 20), "icon_scan", "Q", 0);
        }

        Image[] Bars(Transform parent, string name, Vector2 pos, float width, float h, int count)
        {
            var arr = new Image[count];
            float gap = 2f, w = (width - gap * (count - 1)) / count;
            for (int i = 0; i < count; i++)
                arr[i] = UiKit.Img($"{name}{i}", parent, UiKit.Skew, Color.white, new Vector2(0, 0), new Vector2(0, 0.5f), pos + new Vector2(i * (w + gap), 0), new Vector2(w + h * 0.2f, h));
            return arr;
        }

        const float SkillW = 158f, SkillH = 48f, SkillLine = 96f;

        /// <summary>Slanted skill tile: icon, charge pips, key cap, cooldown fill rising from the bottom, ready line.</summary>
        SkillUi MakeSkill(Transform parent, string name, Vector2 pos, string icon, string key, int pips)
        {
            var s = new SkillUi();
            s.rt = UiKit.Node(name, parent, new Vector2(0, 0), new Vector2(0.5f, 0.5f), pos + new Vector2(SkillW, SkillH) * 0.5f, new Vector2(SkillW, SkillH));
            UiKit.Fill("Bg", s.rt, UiKit.Skew, new Color(0.02f, 0.055f, 0.085f, 0.85f));
            s.fill = UiKit.Fill("Fill", s.rt, UiKit.Skew, A(Cyan, 0.22f));
            s.fill.type = Image.Type.Filled; s.fill.fillMethod = Image.FillMethod.Vertical; s.fill.fillOrigin = 0;
            s.frame = UiKit.Fill("Frame", s.rt, UiKit.Skew, A(Cyan, 0.0f));
            s.line = UiKit.Img("Line", s.rt, UiKit.White, Cyan, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(SkillLine, 2.5f));
            s.icon = UiKit.Img("Icon", s.rt, Icon("a_" + icon, icon), UiKit.TextColor, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(36, 1), new Vector2(32, 32));
            s.icon.preserveAspect = true;
            var cap = UiKit.KeyCap("Key", s.rt, key, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-22, 1), 14);
            cap.GetComponentInChildren<Text>().font = TacKit.Cap;
            s.pips = new Image[pips];
            for (int i = 0; i < pips; i++)
                s.pips[i] = UiKit.Img($"Pip{i}", s.rt, UiKit.Circle, UiKit.Amber, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(66 + i * 15, 1), new Vector2(10, 10));
            return s;
        }

        // ------------------------------------------------------------------ bottom-right

        const float WeaponX = 60f, WeaponY = 40f, WeaponW = 656f, WeaponH = 190f, WeaponCol = 356f;

        void BuildWeapon(RectTransform g)
        {
            var p = Plate("WeaponPlate", g, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-WeaponX, WeaponY), new Vector2(WeaponW, WeaponH));
            UiKit.Img("ViewGlow", p, HoloKit.SoftDot, A(Cyan, 0.12f), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(172, 0), new Vector2(360, 230));
            var go = new GameObject("WeaponView", typeof(RectTransform));
            go.transform.SetParent(p, false);
            weaponView = go.AddComponent<RawImage>();
            weaponView.raycastTarget = false;
            var wr = weaponView.rectTransform;
            wr.anchorMin = wr.anchorMax = new Vector2(0, 0.5f);
            wr.pivot = new Vector2(0.5f, 0.5f);
            wr.anchoredPosition = new Vector2(172, 6);
            wr.sizeDelta = new Vector2(312, 130);
            if (PreviewStage.I) weaponView.texture = PreviewStage.I.WeaponTex;
            // painted profile of the weapon (takes over from the 3D render when the item has one)
            weaponArt = UiKit.Img("WeaponArt", p, null, Color.white, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(172, 6), new Vector2(296, 116));
            weaponArt.preserveAspect = true;
            weaponArt.enabled = false;
            // holo pedestal lines under the render
            UiKit.Img("Pedestal", p, UiKit.FadeLine, A(Cyan, 0.45f), new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(172, 30), new Vector2(130, 1.5f));
            UiKit.Img("PedestalL", p, UiKit.FadeLine, A(Cyan, 0.45f), new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(172, 30), new Vector2(130, 1.5f)).rectTransform.localScale = new Vector3(-1, 1, 1);
            TacKit.MicroText("Serial", p, "EMITTER  SN-4471", new Vector2(0, 0), new Vector2(0, 0), new Vector2(28, 12), TextAnchor.LowerLeft);
            UiKit.Img("Divider", p, UiKit.White, A(Cyan, 0.25f), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(WeaponCol - 20, 0), new Vector2(1.5f, 140));

            weaponName = Font(UiKit.Label("Name", p, "", 19, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(WeaponCol, -30), new Vector2(280, 26), true), TacKit.Display);
            weaponDecode = weaponName.gameObject.AddComponent<DecodeText>();
            fireMode = Font(UiKit.Label("Mode", p, "", 12, TacKit.Micro, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(WeaponCol + 1, -52), new Vector2(280, 16), true, false), TacKit.Cap);
            UiKit.Img("Rule", p, UiKit.FadeLine, A(Cyan, 0.35f), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(WeaponCol, -66), new Vector2(WeaponW - WeaponCol - 24, 1.5f));
            UiKit.Img("AmmoGlow", p, HoloKit.SoftDot, A(Cyan, 0.16f), new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(WeaponCol + 70, 72), new Vector2(260, 130));
            ammoText = Font(UiKit.Label("Ammo", p, "", 70, Cyan, TextAnchor.LowerLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(WeaponCol - 2, 34), new Vector2(170, 80), true), TacKit.Num);
            ammoMax = Font(UiKit.Label("AmmoMax", p, "", 30, new Color(0.75f, 0.83f, 0.9f, 0.8f), TextAnchor.LowerLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(WeaponCol + 110, 46), new Vector2(110, 40), true), TacKit.Cap);
            weaponState = Font(UiKit.Label("State", p, "", 13, UiKit.Amber, TextAnchor.MiddleRight, new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-24, -52), new Vector2(200, 16), false), TacKit.Cap);
            tickStrip = UiKit.Node("Ticks", p, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(WeaponCol, 24), new Vector2(WeaponW - WeaponCol - 26, 14));

            // loadout tabs above the plate: number + weapon silhouette
            for (int i = 0; i < 3; i++)
            {
                var s = new Slot();
                s.rt = UiKit.Node($"Slot{i}", g, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-WeaponX - (2 - i) * 164, WeaponY + WeaponH + 10), new Vector2(158, 44));
                s.bg = UiKit.Fill("Bg", s.rt, TacKit.Slot, new Color(0.02f, 0.05f, 0.08f, 0.75f));
                s.hatch = HoloKit.Tiled("Hatch", s.rt, TacKit.Hatch, new Color(1, 1, 1, 0.035f));
                s.hatch.uvRect = new Rect(0, 0, 158f / 16f, 44f / 16f);
                s.frame = UiKit.Fill("Frame", s.rt, TacKit.SlotFrame, A(Cyan, 0.3f));
                s.line = UiKit.Img("Line", s.rt, UiKit.White, Cyan, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(-6, 1), new Vector2(130, 2.5f));
                s.num = Font(UiKit.Label("Num", s.rt, (i + 1).ToString(), 20, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 1), new Vector2(24, 26), true), TacKit.Num);
                s.icon = UiKit.Img("Icon", s.rt, null, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(16, 0), new Vector2(104, 36));
                s.icon.preserveAspect = true;
                slots[i] = s;
            }
        }

        void RebuildTicks(int count)
        {
            tickCount = count;
            foreach (var t in ticks) Destroy(t.gameObject);
            ticks.Clear();
            tickFlash = new float[count];
            if (count <= 0) return;
            float w = tickStrip.sizeDelta.x, gap = count > 24 ? 1.5f : 2.5f;
            float tw = (w - gap * (count - 1)) / count;
            for (int i = 0; i < count; i++)
            {
                var img = UiKit.Img("T" + i, tickStrip, TacKit.TickBar, Cyan, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * (tw + gap), 0), new Vector2(tw + 2.5f, 12));
                var w2 = img.gameObject.AddComponent<VisorWarp>();
                var src = tickStrip.parent.GetComponent<VisorWarp>() ?? tickStrip.parent.GetComponentInChildren<VisorWarp>();
                if (src) { w2.space = src.space; w2.hingeAnchor = src.hingeAnchor; w2.hingeOffset = src.hingeOffset; w2.angle = src.angle; }
                ticks.Add(img);
            }
        }

        // ------------------------------------------------------------------ top

        void BuildTop(RectTransform g)
        {
            var p = Plate("Objective", g, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -40), new Vector2(400, 98), 0.3f);
            objectiveText = Font(UiKit.Label("Text", p, "", 20, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(24, -34), new Vector2(360, 26), true), TacKit.Cap);
            objTicks = new Image[6];
            for (int i = 0; i < objTicks.Length; i++)
            {
                objTicks[i] = UiKit.Img($"Tick{i}", p, UiKit.RoundFill, A(HealthCol, 0.2f), new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(24 + i * 34, 28), new Vector2(18, 18));
                objTicks[i].gameObject.SetActive(false);
            }
            TacKit.MicroText("Op", p, "OP  DEEP-SIGNAL", new Vector2(1, 0), new Vector2(1, 0.5f), new Vector2(-24, 28), TextAnchor.MiddleRight);

            var area = UiKit.Node("Area", root, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(700, 62));
            areaGroup = area.gameObject.AddComponent<CanvasGroup>();
            areaGroup.alpha = 0f;
            areaText = Font(UiKit.Label("Text", area, "", 27, UiKit.TextColor, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(700, 36), true), TacKit.Display);
            areaDecode = areaText.gameObject.AddComponent<DecodeText>();
            gridText = Font(UiKit.Label("Grid", area, "", 13, new Color(0.75f, 0.86f, 0.93f, 0.75f), TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -47), new Vector2(500, 16), true, false), TacKit.Cap);
            for (int s = -1; s <= 1; s += 2)
            {
                UiKit.Img("Wing", area, UiKit.FadeLine, A(Cyan, 0.6f), new Vector2(0.5f, 1), new Vector2(0, 0.5f), new Vector2(s * 300, -20), new Vector2(120, 1.5f)).rectTransform.localScale = new Vector3(s, 1, 1);
                UiKit.Img("WingTick", area, UiKit.White, Cyan, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(s * 296, -20), new Vector2(2, 10));
            }

            threatBox = UiKit.Node("Threat", root, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -90), new Vector2(420, 38));
            UiKit.Img("Glow", threatBox, HoloKit.SoftDot, A(UiKit.Danger, 0.22f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 90));
            UiKit.Fill("Fill", threatBox, TacKit.Plate, new Color(0.2f, 0.02f, 0.02f, 0.78f));
            var hmask = UiKit.Fill("HatchMask", threatBox, TacKit.Plate, Color.white);
            hmask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            threatHatch = HoloKit.Tiled("Hatch", hmask.transform, TacKit.Hatch, A(UiKit.Danger, 0.14f));
            threatHatch.uvRect = new Rect(0, 0, 420f / 16f, 38f / 16f);
            UiKit.Fill("Frame", threatBox, TacKit.PlateFrame, A(UiKit.Danger, 0.9f));
            UiKit.Img("Warn", threatBox, HoloKit.Warn, UiKit.Danger, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(46, 1), new Vector2(24, 24));
            threatText = Font(UiKit.Label("Text", threatBox, "", 18, UiKit.Danger, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(14, 0), new Vector2(340, 28), true), TacKit.Cap);

            UiKit.KeyCap("HKey", root, "H", new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -34), 14);
            UiKit.Label("ControlsHint", root, UiKit.Spaced("CONTROLS"), 14, A(UiKit.TextColor, 0.55f), TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(74, -34), new Vector2(200, 26), false);
        }

        /// <summary>Boot overlay: a scan line sweeps the visor and a status line types out.</summary>
        void BuildBoot()
        {
            bootSweep = UiKit.Node("BootSweep", root, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000, 140));
            bootSweep.anchorMin = new Vector2(0, 1); bootSweep.anchorMax = new Vector2(1, 1);
            bootSweep.sizeDelta = new Vector2(0, 140);
            UiKit.Fill("Glow", bootSweep, TacKit.GradUp, A(Cyan, 0.08f));
            var line = UiKit.Img("Line", bootSweep, UiKit.White, A(Cyan, 0.55f), new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000, 1.5f));
            line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0); line.rectTransform.sizeDelta = new Vector2(0, 1.5f);
            bootText = UiKit.Label("BootText", root, "", 13, A(Cyan, 0.8f), TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(40, -64), new Vector2(500, 18), false, false);
            bootText.gameObject.AddComponent<DecodeText>();
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
            bannerDecode = bannerTitle.gameObject.AddComponent<DecodeText>();
            bannerLineL = UiKit.Img("LineL", b, UiKit.FadeLine, Cyan, new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(-6, -24), new Vector2(300, 1.5f)).rectTransform;
            bannerLineL.localScale = new Vector3(-1, 1, 1);
            bannerLineR = UiKit.Img("LineR", b, UiKit.FadeLine, Cyan, new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(6, -24), new Vector2(300, 1.5f)).rectTransform;
            bannerSub = UiKit.Label("Sub", b, "", 17, Dim, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -42), new Vector2(1000, 22), false);
        }

        void BuildPrompt()
        {
            promptRect = UiKit.Node("Prompt", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 40));
            UiKit.Fill("Fill", promptRect, TacKit.Slot, new Color(0.012f, 0.03f, 0.05f, 0.85f));
            promptFrame = UiKit.Fill("Frame", promptRect, TacKit.SlotFrame, A(Cyan, 0.5f));
            UiKit.KeyCap("Key", promptRect, "E", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), 16);
            promptText = UiKit.Label("Text", promptRect, "", 18, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(44, 0), new Vector2(400, 30), false);
            promptRect.gameObject.SetActive(false);
        }

        void BuildControls()
        {
            var p = GlassPanel("Controls", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, UiStyle.Kit ? -232 : -86), new Vector2(390, 392));
            controls = p.gameObject;
            controlsGroup = controls.AddComponent<CanvasGroup>();
            UiKit.Label("Title", p, UiKit.Spaced("CONTROLS"), 16, Cyan, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -16), new Vector2(300, 20), false);
            var rows = new[]
            {
                ("WASD", "Move"), ("MOUSE", "Aim"), ("LMB", "Fire"), ("R", "Recharge cell"), ("SPACE", "Dash"),
                ("G", "Grenade (hold, release)"), ("Q", "Scan pulse"), ("E", "Interact / open crate"), ("TAB", "Inventory"), ("1 2 3", "Switch weapon"),
                ("F6", "Switch UI design (restarts)"),
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
            bannerDecode.Set(UiKit.Spaced(title), 0.55f, UiStyle.Kit);
            if (!UiStyle.Kit) bannerDecode.Replay();
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
            glitchT = Mathf.Max(glitchT, Mathf.Clamp(0.12f + amount / 120f, 0.12f, 0.35f));
            kick += Random.insideUnitCircle.normalized * Mathf.Clamp(6f + amount * 0.4f, 6f, 18f);
            if (ring) ring.Flash();
            field?.OnDamaged(amount);
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

        public void ShieldBroken() { shieldAlertT = 1.8f; shieldFlash = 1f; glitchT = Mathf.Max(glitchT, 0.5f); field?.ShieldBroken(); }

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
            bool uiOpen = (player && player.inventory && player.inventory.PanelOpen) || (BaseUI.I && BaseUI.I.IsOpen);

            // F6: flip between the two UI designs (the choice is saved) and restart the raid to rebuild everything
            if (Keyboard.current != null && Keyboard.current.f6Key.wasPressedThisFrame && GameManager.I)
            {
                UiStyle.Toggle();
                GameManager.I.Restart();
                return;
            }

            if (field != null) field.Tick(player, dt, uiOpen);
            else UpdateVisor(player, cam, dt, uiOpen);

            if (player)
            {
                if (field == null)
                {
                    UpdateVitals(player, dt);
                    UpdateSkills(player, dt);
                    UpdateWeapon(player, dt);
                }
                UpdateCrosshair(player, cam, dt, uiOpen);
                UpdatePrompt(player, cam, uiOpen);
            }
            if (field == null) UpdateTop(player, dt, uiOpen);
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

        /// <summary>Boot flicker, helmet inertia (clusters trail the operative's motion), hit kicks, glitch and scanline drift.</summary>
        void UpdateVisor(PlayerController player, Camera cam, float dt, bool uiOpen)
        {
            // boot: sweep + status line, then each cluster flickers on
            bootSweepT += dt;
            float sweepK = Mathf.Clamp01((bootSweepT - 0.1f) / 0.75f);
            bootSweep.gameObject.SetActive(sweepK > 0f && sweepK < 1f);
            bootSweep.anchoredPosition = new Vector2(0, -Mathf.Lerp(-70f, root.rect.height + 70f, Ease.OutCubic(sweepK)));
            if (startT > 0.2f && startT - dt <= 0.2f) { bootText.GetComponent<DecodeText>().Set("TAC-LINK  v4.2   //   VISOR ONLINE   //   BIOMETRICS NOMINAL", 0.9f); ProcAudio.Play("ready", 0.25f, 0f, 0.8f); }
            bootText.color = A(Cyan, startT < 4f ? 0.8f : Mathf.MoveTowards(bootText.color.a, 0f, dt * 0.8f));

            // inertia: player velocity in screen axes, smoothed through a spring
            Vector2 vs = Vector2.zero;
            if (player && cam)
            {
                Vector3 v = player.Velocity;
                Vector3 r = cam.transform.right; r.y = 0f; r.Normalize();
                Vector3 f = cam.transform.forward; f.y = 0f; f.Normalize();
                vs = new Vector2(Vector3.Dot(v, r), Vector3.Dot(v, f));
                if (player.IsDashing) vs *= 0.55f;
            }
            Vector2 target = -vs * 1.6f;
            kick = Vector2.MoveTowards(kick, Vector2.zero, dt * 60f) * Mathf.Exp(-dt * 6f);
            glitchT = Mathf.Max(0f, glitchT - dt);
            bool glitchFrame = glitchT > 0f && (Time.frameCount % 3 == 0);
            foreach (var c in clusters)
            {
                // critically damped spring towards the target offset
                Vector2 to = target + kick;
                c.vel += ((to - c.off) * 90f - c.vel * 17f) * dt;
                c.off += c.vel * dt;
                Vector2 jitter = glitchFrame ? new Vector2(Random.Range(-7f, 7f), Random.Range(-1.5f, 1.5f)) : Vector2.zero;
                c.rt.anchoredPosition = c.off + jitter;

                float bootA = startT < c.boot ? 0f : startT < c.boot + 0.28f ? (Mathf.Sin((startT - c.boot) * 85f) > -0.2f ? 0.9f : 0.15f) : 1f;
                float want = uiOpen ? 0f : bootA;
                if (glitchT > 0f) want *= glitchFrame ? 0.55f : 1f;
                c.g.alpha = startT < c.boot + 0.28f ? want : Mathf.MoveTowards(c.g.alpha, want, dt * 6f);
            }

            float drift = Time.unscaledTime * 0.35f;
            foreach (var s in scanlines)
            {
                var uv = s.uvRect; uv.y = -drift; s.uvRect = uv;
            }
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
            hpGlow.rectTransform.sizeDelta = new Vector2((VitalBarW + 28f) * v, 46f);
            hpGlow.color = A(full, 0.2f);
            string hp = Mathf.CeilToInt(h.Health).ToString();
            if (hpValue.text != hp) hpValue.text = hp;
            hpValue.color = full;

            float s = h.Shield01;
            shieldFlash = Mathf.MoveTowards(shieldFlash, 0f, dt * 3f);
            Color sc = Color.Lerp(Cyan, Color.white, shieldFlash * 0.7f);
            float shimmer = s > 0f && s < 1f ? Time.unscaledTime * 2.5f : -1f;      // regenerating: a glint runs along the bar
            FillSegments(shSegs, s, s, sc, shimmer);
            shGlow.rectTransform.sizeDelta = new Vector2((VitalBarW + 28f) * s, 40f);
            string sv = Mathf.CeilToInt(h.Shield).ToString();
            if (shValue.text != sv) shValue.text = sv;
            shValue.color = s > 0f ? Cyan : A(UiKit.Danger, 0.9f);
            portraitFrame.color = Color.Lerp(A(Cyan, s > 0f ? 0.6f : 0.3f), UiKit.Danger, damageFlash);
            // the schematic shows the suit's condition: cyan while shielded, amber on bare armour, red when critical
            Color suitCol = v < 0.25f ? UiKit.Danger : s > 0f ? Cyan : HealthCol;
            suit.color = Color.Lerp(suitCol, Color.white, damageFlash * 0.8f);
            float sy = (Time.unscaledTime * 0.45f) % 1.3f;
            suitScan.anchoredPosition = new Vector2(0, -sy * 200f);
            shieldAlertT = Mathf.Max(0f, shieldAlertT - dt);
            shieldAlert.color = A(UiKit.Danger, shieldAlertT > 0f ? Mathf.Clamp01(shieldAlertT) * (0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 16f)) : 0f);
            if (Time.frameCount % 20 == 0)
                linkText.text = damageFlash > 0.2f ? $"LINK {Random.Range(41, 77)}%" : $"LINK {Random.Range(97, 100)}%";
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
                if (segs[i].color != c) segs[i].color = c;
            }
        }

        void UpdateSkills(PlayerController player, float dt)
        {
            void Apply(SkillUi s, float ready01, bool ready, bool active, Color activeCol)
            {
                if (ready && !s.wasReady) s.punch = 1f;          // pops when it comes off cooldown
                s.wasReady = ready;
                s.fill.fillAmount = ready ? 1f : ready01;
                s.fill.color = active ? A(activeCol, 0.45f) : ready ? A(Cyan, 0.16f) : A(Cyan, 0.1f);
                s.line.color = active ? activeCol : ready ? Cyan : new Color(1, 1, 1, 0.12f);
                s.line.rectTransform.sizeDelta = new Vector2(SkillLine * (ready ? 1f : ready01), 2.5f);
                s.icon.color = active ? activeCol : ready ? UiKit.TextColor : new Color(1, 1, 1, 0.35f);
                s.frame.color = new Color(1, 1, 1, s.punch * 0.35f);
            }
            var gr = player.grenades;
            if (gr)
            {
                float r = gr.Charges >= gr.maxCharges ? 1f : gr.RechargeProgress;
                Apply(grenade, gr.Charges > 0 ? 1f : r, gr.Charges > 0, gr.IsAiming, UiKit.Amber);
                for (int i = 0; i < grenade.pips.Length; i++)
                {
                    grenade.pips[i].enabled = i < gr.maxCharges;
                    grenade.pips[i].color = i < gr.Charges ? UiKit.Amber : new Color(1, 1, 1, 0.15f);
                }
            }
            Apply(dash, 1f - player.DashCooldown01, player.DashCooldown01 <= 0f, player.IsDashing, Cyan);
            var sp = player.GetComponent<ScanPulse>();
            if (sp) Apply(scanSkill, 1f - sp.Cooldown01, sp.Ready, sp.Cooldown01 > 0.97f, Cyan);
            foreach (var s in new[] { grenade, dash, scanSkill })
            {
                s.punch = Mathf.MoveTowards(s.punch, 0f, dt * 4f);
                float k = 1f + s.punch * 0.1f;
                s.rt.localScale = new Vector3(k, k, 1f);
            }
        }

        void UpdateWeapon(PlayerController player, float dt)
        {
            var g = player.gun;
            var def = g ? g.Current : null;
            if (def == null)
            {
                if (shownWeapon != null || weaponDecode.Target == "")
                {
                    shownWeapon = null;
                    weaponDecode.Set(UiKit.Spaced("UNARMED"));
                    weaponArt.enabled = false;
                    fireMode.text = "";
                    ammoText.text = "--";
                    ammoMax.text = "";
                    weaponState.text = "";
                    if (tickCount != 0) RebuildTicks(0);
                }
            }
            else
            {
                if (shownWeapon != def.kind)
                {
                    shownWeapon = def.kind;
                    weaponDecode.Set(UiKit.Spaced(def.displayName), 0.45f);
                    var art = Items.Get(Items.ForWeapon(def.kind)).icon is string ic && !string.IsNullOrEmpty(ic) ? UiKit.Art(ic) : null;
                    weaponArt.sprite = art;
                    weaponArt.enabled = art;
                    weaponView.enabled = !art;
                    if (PreviewStage.I) PreviewStage.I.WeaponViewActive = !art;
                    fireMode.text = def.beam ? "CONTINUOUS BEAM  //  PIERCING" : def.pellets > 1 ? $"SCATTER  x{def.pellets}  //  SEMI" : "FULL AUTO  //  PLASMA BOLT";
                    ammoPunch = 1f;
                    lastAmmo = -1;
                    int n = def.cellSize <= 40 ? def.cellSize : 40;
                    RebuildTicks(n);
                }
                int mag = Mathf.Max(1, g.MagazineSize);
                float frac = g.Ammo / (float)mag;
                Color ac = g.Ammo == 0 ? UiKit.Danger : frac < 0.3f ? UiKit.Amber : Cyan;
                if (g.Ammo != lastAmmo)
                {
                    if (lastAmmo >= 0 && g.Ammo < lastAmmo)
                    {
                        ammoPunch = Mathf.Max(ammoPunch, 0.5f);
                        // flash the ticks that just emptied
                        int n = ticks.Count;
                        int from = Mathf.CeilToInt(g.Ammo / (float)mag * n), to = Mathf.CeilToInt(lastAmmo / (float)mag * n);
                        for (int i = from; i < to && i < n; i++) tickFlash[i] = 1f;
                        foreach (var c in clusters) if (c.g == bottomR) c.vel += new Vector2(0f, -40f);
                    }
                    lastAmmo = g.Ammo;
                    ammoText.text = g.Ammo.ToString();
                }
                ammoText.color = ac;
                ammoMax.text = $"/ {g.MagazineSize}";
                ammoMax.rectTransform.anchoredPosition = new Vector2(WeaponCol + ammoText.preferredWidth + 12f, 46f);
                ammoPunch = Mathf.MoveTowards(ammoPunch, 0f, dt * 6f);
                // the weapon art floats gently and kicks back with every shot
                weaponArt.rectTransform.anchoredPosition = new Vector2(172f - ammoPunch * 10f, 6f + Mathf.Sin(Time.unscaledTime * 1.3f) * 2.5f);
                weaponArt.rectTransform.localRotation = Quaternion.Euler(0f, 0f, ammoPunch * 2.5f);
                float s = 1f + ammoPunch * 0.08f;
                ammoText.rectTransform.localScale = new Vector3(s, s, 1f);

                bool reloading = g.IsReloading;
                int count = ticks.Count;
                float shown = reloading ? g.ReloadProgress : frac;
                for (int i = 0; i < count; i++)
                {
                    tickFlash[i] = Mathf.MoveTowards(tickFlash[i], 0f, dt * 5f);
                    bool on = (i + 0.5f) / count <= shown;
                    Color c = on ? (reloading ? UiKit.Amber : ac) : new Color(1, 1, 1, 0.08f);
                    if (tickFlash[i] > 0f) c = Color.Lerp(c, Color.white, tickFlash[i]);
                    if (ticks[i].color != c) ticks[i].color = c;
                }
                string st = reloading ? UiKit.Spaced("RECHARGING") : g.Ammo == 0 ? UiKit.Spaced("CELL EMPTY  [R]") : g.IsBeaming ? UiKit.Spaced("BEAM ACTIVE") : "";
                if (weaponState.text != st) weaponState.text = st;
                float blink = g.Ammo == 0 && !reloading ? (Mathf.Sin(Time.unscaledTime * 10f) > 0f ? 1f : 0.35f) : 1f;
                weaponState.color = A(g.IsBeaming ? def.color : UiKit.Amber, blink);
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
                    sl.bg.color = on ? new Color(0.05f, 0.16f, 0.2f, 0.85f) : new Color(0.02f, 0.05f, 0.08f, 0.7f);
                    sl.frame.color = st.IsEmpty ? new Color(1, 1, 1, 0.1f) : on ? Cyan : A(Cyan, 0.3f);
                    sl.line.enabled = on;
                    sl.hatch.enabled = st.IsEmpty;
                    sl.num.color = on ? Cyan : st.IsEmpty ? new Color(1, 1, 1, 0.25f) : UiKit.TextColor;
                    sl.icon.enabled = !st.IsEmpty;
                    if (!st.IsEmpty) { sl.icon.sprite = ItemIcons.Get(st.id); sl.icon.color = on ? Color.white : new Color(1, 1, 1, 0.5f); }
                    sl.rt.localScale = on ? new Vector3(1.04f, 1.04f, 1f) : Vector3.one;
                }
            }
        }

        void UpdateTop(PlayerController player, float dt, bool uiOpen)
        {
            var gm = GameManager.I;
            if (!gm) return;
            string area = gm.CurrentArea;
            if (area != lastArea) { lastArea = area; areaDecode.Set(UiKit.Spaced(area), 0.5f); areaT = 0f; }
            if (areaT >= 0f) areaT += dt;
            float aa = areaT < 0f ? 0f : areaT < 0.3f ? areaT / 0.3f : areaT < 4f ? 1f : Mathf.Max(0.45f, 1f - (areaT - 4f) / 1f);
            areaGroup.alpha = uiOpen ? 0f : aa;
            if (player && Time.frameCount % 6 == 0)
            {
                Vector3 pp = player.transform.position;
                gridText.text = $"GRID  {pp.x + 500f:000.0} : {pp.z + 500f:000.0}     ALT  {pp.y:+0.0;-0.0}";
            }
            int engaged = gm.AlertedHostiles;
            threatBox.gameObject.SetActive(engaged > 0 && !uiOpen);
            if (engaged > 0)
            {
                threatText.text = UiKit.Spaced($"{engaged} HOSTILE{(engaged > 1 ? "S" : "")} ENGAGED");
                threatText.color = A(UiKit.Danger, 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 7f));
                var uv = threatHatch.uvRect; uv.x = -Time.unscaledTime * 1.2f; threatHatch.uvRect = uv;
            }
            int total = gm.CampsTotal, cleared = gm.CampsCleared;
            string obj = total > 0 && cleared >= total
                ? $"{UiKit.Spaced("KEPLER-7 SECURED")}  <color=#{UiKit.Hex(HealthCol)}>{cleared}/{total}</color>"
                : $"{UiKit.Spaced("CLEAR XENO CAMPS")}  <color=#{UiKit.Hex(HealthCol)}>{cleared}/{total}</color>";
            if (objectiveText.text != obj) objectiveText.text = obj;
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
            float target = Time.timeSinceLevelLoad < controlsShownUntil && startT > 1.2f && (pinned || !fighting) ? 1f : 0f;
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
