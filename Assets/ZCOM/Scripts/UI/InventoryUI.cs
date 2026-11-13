using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Zcom
{
    /// <summary>
    /// Tac-link inventory: operative paper-doll (live 3D model on a holo pedestal, leader lines to the loadout),
    /// backpack grid and an optional crate. Panels fold in with a scan sweep, slots cascade on, a spring-loaded
    /// selector follows the mouse, crates are scanned item by item, the world behind goes out of focus.
    /// LMB drag: move / merge / swap. Shift+drag: split. RMB: quick move. Drag outside the window: drop on the ground.
    /// Click a weapon slot: equip. F: take all from the crate. Tab / Esc: close.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        public static InventoryUI I { get; private set; }

        const float Slot = 92f, Pitch = 100f, PanelH = 592f, Header = 74f;
        // colours follow the active design: cyan hologram + amber, or field-kit paper + signal orange
        static bool Kit => UiStyle.Kit;
        static Color Cyan => Kit ? FieldKit.Paper : TacKit.Cyan;
        static Color Amber => Kit ? FieldKit.Signal : UiKit.Amber;
        static readonly Color Warn = new Color(1f, 0.35f, 0.25f);
        static Color SlotBg => Kit ? FieldKit.InkSoft : new Color(0.025f, 0.055f, 0.08f, 0.92f);
        Text takeAllLabel, titleSub;
        RectTransform tooltipIconBox;
        SlotUi lastHover;
        RectTransform titleRoot;
        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        class SlotUi
        {
            public RectTransform rt;
            public Image bg, glow, halo, frame, strip, icon, pill, scanBar, tag, sticker;
            public Text tagText;
            public RawImage hatch;
            public Text count, label, sub, hotkey, pillText;
            public GameObject scan;
            public ItemContainer container;
            public int index, order;
            public bool wide;
            public float pop, fadeA;
            public ItemId shownId;
            public int shownCount;
        }

        class PanelAnim { public RectTransform rt; public CanvasGroup g; public RectTransform sweep; public float delay; public Vector2 home; }

        Font font;
        RectTransform root, window;
        CanvasGroup dimGroup;
        GameObject dim;
        RectTransform loadoutPanel, backpackPanel, containerPanel, stashPanel, hintBar, takeAllButton;
        readonly List<PanelAnim> panels = new List<PanelAnim>();
        Text containerTitle, containerMicro, backpackCount, carryText, valueText;
        Image takeAllFrame, takeAllFill, massFill, hpFill, shFill;
        Text hpText, shText;
        Image[] nadePips;
        readonly List<SlotUi> loadoutSlots = new List<SlotUi>();
        readonly List<SlotUi> backpackSlots = new List<SlotUi>();
        readonly List<SlotUi> containerSlots = new List<SlotUi>();
        // containerSlots = the crate's 3x3 grid followed by the ship stash's 5x5 grid (only one is bound at a time)
        readonly List<SlotUi> crateSlots = new List<SlotUi>(), stashSlots = new List<SlotUi>();
        Text stashMicro;
        const int StashCols = 5;
        readonly List<Image[]> leaders = new List<Image[]>();
        RawImage operativeView;
        RectTransform pedestalA, pedestalB;
        bool rotating;
        Vector2 lastPointer;
        float openT = -1f, shownValue;

        // selector (spring-loaded brackets that jump between slots)
        RectTransform selector;
        Image[] selBrackets;
        Vector2 selPos, selVel, selSize, selSizeVel;
        float selAlpha;

        // tooltip
        RectTransform tooltip;
        Image tooltipIcon, tooltipIconFrame, tooltipPill, tooltipHalo;
        Text tooltipName, tooltipPillText, tooltipDesc, tooltipFoot;
        class StatRow { public RectTransform rt; public Text label, value, delta; public Image fill, marker; }
        readonly StatRow[] statRows = new StatRow[5];
        ItemId tipShown;

        // drag ghost
        RectTransform ghost;
        Image ghostIcon, ghostFrame;
        Text ghostCount, dropLabel;

        // crate scan
        readonly HashSet<ItemContainer> scanned = new HashSet<ItemContainer>();
        float scanStart = -1f;
        const float ScanLead = 0.25f, ScanStep = 0.11f;

        // world focus while open
        Volume focus;

        Inventory inv;
        ItemContainer boundContainer;
        int seenVersion = -1, seenContainerVersion = -1;

        SlotUi dragFrom, pressed;
        int dragAmount;
        Vector2 pressPos;
        bool dragging;

        Camera UiCam => Hud.I && Hud.I.Canvas.renderMode != RenderMode.ScreenSpaceOverlay ? Hud.I.Canvas.worldCamera : null;

        public bool IsOpen => inv && inv.PanelOpen;

        /// <summary>World blur behind the open window. The smoke test turns it off: its captures render the camera into a
        /// texture, where the overlay UI goes through post-processing too (on screen URP draws it after post).</summary>
        public static bool FocusBlur = true;

        // ------------------------------------------------------------------ build

        public void Init(RectTransform canvasRoot, Font f)
        {
            I = this;
            root = canvasRoot;
            font = f;

            var d = UiKit.Fill("InventoryDim", root, UiKit.White, Kit ? new Color(0.01f, 0.012f, 0.015f, 0.6f) : new Color(0f, 0.012f, 0.03f, 0.55f));
            dim = d.gameObject;
            dimGroup = dim.AddComponent<CanvasGroup>();
            UiKit.Fill("Vignette", d.rectTransform, ProcTex.VignetteSprite, new Color(0f, 0.01f, 0.02f, 0.6f));
            var scan = HoloKit.Tiled("Scan", d.rectTransform, HoloKit.Scanlines, new Color(0.5f, 0.85f, 1f, 0.025f));
            scan.uvRect = new Rect(0, 0, 480, 180);
            scan.enabled = !Kit;

            window = new GameObject("InventoryWindow", typeof(RectTransform)).GetComponent<RectTransform>();
            window.SetParent(root, false);
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.sizeDelta = Vector2.zero;

            BuildLoadout();

            // Backpack 6x4
            backpackPanel = Panel("Backpack", new Vector2(6 * Pitch + 44, PanelH), "BACKPACK", "", Cyan);
            if (Kit) FieldKit.Mono("Tray", backpackPanel, "TRAY / 24", 12, FieldKit.InkSolid, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(232, -30));
            backpackCount = Txt("Count", backpackPanel, "", 18, UiKit.TextColor, TextAnchor.MiddleRight, new Vector2(-24, Kit ? -40 : -29), new Vector2(200, 26), new Vector2(1, 1));
            if (Kit) { backpackCount.font = FieldKit.Display; backpackCount.fontSize = 28; backpackCount.color = FieldKit.InkSolid; }
            backpackCount.rectTransform.pivot = new Vector2(1, 0.5f);
            backpackCount.supportRichText = true;
            for (int i = 0; i < 24; i++)
            {
                int cx = i % 6, cy = i / 6;
                var s = MakeSlot(backpackPanel, new Vector2(22 + cx * Pitch + Slot * 0.5f, -(Header + 6 + cy * Pitch + Slot * 0.5f)), new Vector2(Slot, Slot), false);
                s.order = cx + cy;
                backpackSlots.Add(s);
            }
            // footer: mass bar + value counter
            float fy = -(Header + 6 + 4 * Pitch + 26);
            var mico = UiKit.Art("a_icon_mass");
            if (mico) UiKit.Img("MassIcon", backpackPanel, mico, UiKit.TextDim, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(22, fy), new Vector2(20, 20));
            TacKit.MicroText("MassLbl", backpackPanel, "MASS", new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(48, fy), TextAnchor.MiddleLeft, 12);
            var mass = UiKit.Node("Mass", backpackPanel, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(92, fy), new Vector2(228, 8));
            UiKit.Fill("Bg", mass, UiKit.White, new Color(1, 1, 1, 0.07f));
            massFill = UiKit.Fill("Fill", mass, UiKit.White, Cyan);
            massFill.type = Image.Type.Filled; massFill.fillMethod = Image.FillMethod.Horizontal;
            var mt = HoloKit.Tiled("Ticks", mass, TacKit.Ruler, new Color(0, 0, 0, 0.55f));
            mt.uvRect = new Rect(0, 0, 228f / 8f / 1.25f, 1);
            carryText = Txt("Carry", backpackPanel, "", 17, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(330, fy), new Vector2(120, 24), new Vector2(0, 1));
            carryText.rectTransform.pivot = new Vector2(0, 0.5f);
            TacKit.MicroText("ValLbl", backpackPanel, "VALUE", new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-150, fy), TextAnchor.MiddleRight, 12);
            var cico = UiKit.Art("a_icon_credits");
            if (cico) UiKit.Img("ValIcon", backpackPanel, cico, Amber, new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-196, fy), new Vector2(20, 20));
            valueText = Txt("Value", backpackPanel, "", Kit ? 34 : 24, Amber, TextAnchor.MiddleRight, new Vector2(-24, fy + 1), new Vector2(140, 30), new Vector2(1, 1));
            valueText.rectTransform.pivot = new Vector2(1, 0.5f);
            if (Kit) valueText.font = FieldKit.Display;

            // Container 3x3
            containerPanel = Panel("Container", new Vector2(3 * Pitch + 44, PanelH), "SUPPLY CRATE", "", Amber);
            foreach (var tx in containerPanel.GetComponentsInChildren<Text>(true)) if (tx.name == "Title") containerTitle = tx;
            containerMicro = TacKit.MicroText("Status", containerPanel, "", new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(24, -78), TextAnchor.MiddleLeft, 11);
            for (int i = 0; i < 9; i++)
            {
                int cx = i % 3, cy = i / 3;
                var s = MakeSlot(containerPanel, new Vector2(22 + cx * Pitch + Slot * 0.5f, -(Header + 14 + cy * Pitch + Slot * 0.5f)), new Vector2(Slot, Slot), false);
                s.order = cx + cy;
                containerSlots.Add(s);
                crateSlots.Add(s);
            }
            takeAllButton = UiKit.Node("TakeAll", containerPanel, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -(Header + 14 + 3 * Pitch + 22)), new Vector2(3 * Pitch - 8, 54));
            takeAllFill = UiKit.Fill("Fill", takeAllButton, TacKit.Slot, new Color(0.22f, 0.14f, 0.03f, 0.9f));
            var tah = HoloKit.Tiled("Hatch", takeAllButton, TacKit.Hatch, Kit ? A(FieldKit.InkSolid, 0.14f) : A(Amber, 0.06f));
            tah.uvRect = new Rect(0, 0, (3 * Pitch - 8) / 16f, 54f / 16f);
            takeAllFrame = UiKit.Fill("Frame", takeAllButton, TacKit.SlotFrame, Amber);
            UiKit.KeyCap("Key", takeAllButton, "F", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), 15);
            takeAllLabel = Txt("Label", takeAllButton, Kit ? "TAKE ALL" : UiKit.Spaced("TAKE ALL"), Kit ? 40 : 22, UiKit.TextColor, TextAnchor.MiddleCenter, new Vector2(16, 1), new Vector2(240, 40), new Vector2(0.5f, 0.5f));

            if (Kit) takeAllLabel.font = FieldKit.Display;

            // Ship stash 5x5 (persistent storage, Profile.Stash)
            int stashRows = Mathf.CeilToInt(Profile.StashSize / (float)StashCols);
            stashPanel = Panel("Stash", new Vector2(StashCols * Pitch + 44, PanelH), "STASH", "", Amber);
            stashMicro = Txt("Count", stashPanel, "", 18, UiKit.TextColor, TextAnchor.MiddleRight, new Vector2(-24, Kit ? -40 : -29), new Vector2(260, 26), new Vector2(1, 1));
            if (Kit) { stashMicro.font = FieldKit.Display; stashMicro.fontSize = 28; stashMicro.color = FieldKit.InkSolid; }
            stashMicro.rectTransform.pivot = new Vector2(1, 0.5f);
            stashMicro.supportRichText = true;
            for (int i = 0; i < StashCols * stashRows; i++)
            {
                int cx = i % StashCols, cy = i / StashCols;
                var s = MakeSlot(stashPanel, new Vector2(22 + cx * Pitch + Slot * 0.5f, -(Header + 6 + cy * Pitch + Slot * 0.5f)), new Vector2(Slot, Slot), false);
                s.order = cx + cy;
                containerSlots.Add(s);
                stashSlots.Add(s);
            }

            // Bottom hint bar
            hintBar = UiKit.Node("HintBar", window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1), new Vector2(0, -PanelH * 0.5f - 22), new Vector2(1400, 44));
            UiKit.Fill("Fill", hintBar, Kit ? TacKit.Slot : TacKit.Plate, Kit ? FieldKit.Ink : TacKit.Glass);
            UiKit.Fill("Frame", hintBar, TacKit.PlateFrame, A(Cyan, Kit ? 0f : 0.25f));
            string K(string k) => $"<color=#{Hex(Amber)}>{k}</color>";
            string sep = Kit ? "<color=#4a4a46>  /  </color>" : "<color=#2c4656>     /     </color>";
            var hint = Txt("Hints", hintBar,
                GameManager.InHub ? $"{K("LMB")}  DRAG{sep}{K("SHIFT")}  SPLIT{sep}{K("RMB")}  QUICK MOVE{sep}{K("CLICK")}  EQUIP{sep}{K("TAB")}  CLOSE"
                : $"{K("LMB")}  DRAG{sep}{K("SHIFT")}  SPLIT{sep}{K("RMB")}  QUICK MOVE{sep}{K("DRAG OUT")}  DROP{sep}{K("CLICK")}  EQUIP{sep}{K("F")}  TAKE ALL{sep}{K("TAB")}  CLOSE",
                16, UiKit.TextDim, TextAnchor.MiddleCenter, new Vector2(0, 1), new Vector2(1400, 40), new Vector2(0.5f, 0.5f));
            hint.supportRichText = true;
            if (Kit) { hint.font = FieldKit.MonoFont; hint.fontSize = 13; }
            else
            {
                // hologram design: amber key caps instead of a text line
                hint.gameObject.SetActive(false);
                var row = UiKit.Node("Keys", hintBar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                var items = GameManager.InHub
                    ? new[] { ("LMB", "DRAG"), ("SHIFT", "SPLIT"), ("RMB", "QUICK MOVE"), ("CLICK", "EQUIP"), ("TAB", "CLOSE") }
                    : new[] { ("LMB", "DRAG"), ("SHIFT", "SPLIT"), ("RMB", "QUICK MOVE"), ("DRAG OUT", "DROP"), ("CLICK", "EQUIP"), ("F", "TAKE ALL"), ("TAB", "CLOSE") };
                float hx = 0f;
                foreach (var (k, a) in items)
                {
                    var cap = UiKit.KeyCap("Key", row, k, new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(hx, 0), 14);
                    cap.Find("Frame").GetComponent<Image>().color = Amber;
                    cap.Find("Fill").GetComponent<Image>().color = A(Amber, 0.1f);
                    var kt = cap.GetComponentInChildren<Text>();
                    kt.color = Amber; kt.font = TacKit.Cap;
                    hx += cap.sizeDelta.x + 12f;
                    var at = Txt("Action", row, a, 16, UiKit.TextColor, TextAnchor.MiddleLeft, Vector2.zero, new Vector2(200, 24), new Vector2(0.5f, 0.5f));
                    at.rectTransform.pivot = new Vector2(0, 0.5f);
                    at.rectTransform.anchoredPosition = new Vector2(hx, 0);
                    hx += at.preferredWidth + 36f;
                }
                row.anchoredPosition = new Vector2(-(hx - 36f) * 0.5f, 0);
            }

            // screen title above the panels
            titleRoot = UiKit.Node("ScreenTitle", window, new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(900, 60));
            UiKit.Img("Bar", titleRoot, UiKit.White, Kit ? FieldKit.Signal : Cyan, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(Kit ? 9 : 6, 44));
            var st = Txt("T", titleRoot, "INVENTORY", Kit ? 60 : 46, Kit ? FieldKit.Paper : UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(24, Kit ? -3 : 0), new Vector2(400, 60), new Vector2(0, 0.5f));
            st.font = Kit ? FieldKit.Display : TacKit.Num;
            UiKit.Img("Div", titleRoot, UiKit.White, new Color(1, 1, 1, 0.35f), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(24 + st.preferredWidth + 22, 0), new Vector2(1.5f, 30));
            titleSub = Txt("Sub", titleRoot, "", Kit ? 15 : 18, new Color(0.75f, 0.82f, 0.88f, 0.85f), TextAnchor.MiddleLeft, new Vector2(24 + st.preferredWidth + 44, 0), new Vector2(600, 24), new Vector2(0, 0.5f));
            if (Kit) titleSub.font = FieldKit.MonoFont;

            BuildSelector();
            BuildTooltip();

            // Drag ghost
            ghost = new GameObject("DragGhost", typeof(RectTransform)).GetComponent<RectTransform>();
            ghost.SetParent(root, false);
            ghost.anchorMin = ghost.anchorMax = new Vector2(0.5f, 0.5f);
            ghost.sizeDelta = new Vector2(Slot, Slot);
            UiKit.Fill("Glow", ghost, HoloKit.SoftDot, A(Cyan, 0.25f), -30f);
            UiKit.Fill("Bg", ghost, TacKit.Slot, new Color(0.03f, 0.08f, 0.11f, 0.6f));
            ghostFrame = UiKit.Fill("Frame", ghost, TacKit.SlotFrame, Cyan);
            ghostIcon = Img("Icon", ghost, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-14, -14), Color.white);
            ghostIcon.preserveAspect = true;
            ghostCount = Txt("Count", ghost, "", 18, Color.white, TextAnchor.LowerRight, new Vector2(-6, 2), new Vector2(80, 24), new Vector2(1, 0));
            dropLabel = Txt("Drop", ghost, UiKit.Spaced("DROP"), 18, Warn, TextAnchor.UpperCenter, new Vector2(0, 26), new Vector2(120, 24), new Vector2(0.5f, 1));

            BuildFocusVolume();
            SetVisible(false);
        }

        void BuildLoadout()
        {
            loadoutPanel = Panel("Loadout", new Vector2(560, PanelH), "OPERATIVE", "MK-07  //  VANGUARD", Cyan);
            var lp = loadoutPanel;

            // holo pedestal: glow, two counter-rotating tick rings squashed into ellipses, beam
            const float cx = 150f, padY = -(PanelH - 132);
            UiKit.Img("Beam", lp, TacKit.GradUp, A(Cyan, 0.07f), new Vector2(0, 1), new Vector2(0.5f, 0), new Vector2(cx, padY), new Vector2(220, 380));
            UiKit.Img("PadGlow", lp, HoloKit.SoftDot, A(Cyan, 0.2f), new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(cx, padY), new Vector2(320, 80));
            var squashA = UiKit.Node("PadA", lp, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(cx, padY), new Vector2(240, 240));
            squashA.localScale = new Vector3(1f, 0.24f, 1f);
            pedestalA = UiKit.Img("Ring", squashA, HoloKit.RingTicks, A(Cyan, 0.55f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240, 240)).rectTransform;
            var squashB = UiKit.Node("PadB", lp, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(cx, padY - 2), new Vector2(190, 190));
            squashB.localScale = new Vector3(1f, 0.24f, 1f);
            pedestalB = UiKit.Img("Ring", squashB, HoloKit.Arc, A(Cyan, 0.35f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190, 190)).rectTransform;
            var arcFill = pedestalB.GetComponent<Image>();
            arcFill.type = Image.Type.Filled; arcFill.fillMethod = Image.FillMethod.Radial360; arcFill.fillAmount = 0.62f;

            // live 3D operative (the in-game model on the PreviewStage), drag to rotate
            var stage = PreviewStage.Ensure();
            var go = new GameObject("Operative", typeof(RectTransform));
            go.transform.SetParent(lp, false);
            operativeView = go.AddComponent<RawImage>();
            operativeView.texture = stage.Body;
            operativeView.raycastTarget = false;
            var ort = operativeView.rectTransform;
            ort.anchorMin = ort.anchorMax = new Vector2(0, 1);
            ort.pivot = new Vector2(0.5f, 1);
            ort.anchoredPosition = new Vector2(cx, -Header + 4);
            float oh = -padY - Header + 4f;
            ort.sizeDelta = new Vector2(oh * 292f / 438f, oh);
            var oscan = HoloKit.Tiled("Scan", ort, HoloKit.Scanlines, new Color(0.6f, 0.95f, 1f, 0.04f));
            oscan.uvRect = new Rect(0, 0, oh / 6f, oh / 6f);
            if (Kit)
            {
                foreach (var n in new[] { "Beam", "PadGlow" }) lp.Find(n).GetComponent<Image>().enabled = false;
                pedestalA.GetComponent<Image>().enabled = false;
                pedestalB.GetComponent<Image>().enabled = false;
                oscan.enabled = false;
                var marker = UiKit.Node("FloorMarker", lp, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(cx, padY + 6), new Vector2(210, 34));
                marker.SetSiblingIndex(operativeView.transform.GetSiblingIndex());
                TacKit.Brackets(marker, A(FieldKit.Paper, 0.7f), 16f, 0f);
                // height ruler along the left edge, like a printed equipment sheet
                for (int i = 0; i <= 10; i++)
                {
                    float ry = padY + 8 + i * (oh - 30f) / 10f;
                    UiKit.Img("RulerTick" + i, lp, UiKit.White, A(FieldKit.Paper, 0.45f), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(18, ry), new Vector2(i % 5 == 0 ? 12 : 7, 1.5f));
                    if (i % 5 == 0) FieldKit.Mono("RulerNum" + i, lp, (i * 20).ToString(), 9, A(FieldKit.Paper, 0.55f), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(34, ry));
                }
            }
            Txt("RotateHint", lp, "DRAG TO ROTATE", 11, new Color(1, 1, 1, 0.3f), TextAnchor.MiddleCenter, new Vector2(cx, padY - 30), new Vector2(260, 16), new Vector2(0, 1)).rectTransform.pivot = new Vector2(0.5f, 0.5f);

            // status block under the weapon slots
            const float sx = 322f, sy = -(Header + 8 + 3 * 128 + 22);
            UiKit.Img("StatusRule", lp, UiKit.FadeLine, A(Cyan, 0.3f), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(sx, sy + 14), new Vector2(222, 1.5f));
            TacKit.MicroText("StatusLbl", lp, "BIOMETRICS", new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(sx, sy + 2), TextAnchor.MiddleLeft, 11);
            hpFill = StatusBar(lp, "HP", new Vector2(sx, sy - 22), new Color(1f, 0.72f, 0.26f), out hpText);
            shFill = StatusBar(lp, "SHD", new Vector2(sx, sy - 46), Cyan, out shText);
            var gico = UiKit.Art("a_icon_grenade");
            if (gico) UiKit.Img("NadeIcon", lp, gico, UiKit.TextDim, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(sx - 2, sy - 70), new Vector2(18, 18));
            TacKit.MicroText("NadeLbl", lp, "GRN", new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(sx + 21, sy - 70), TextAnchor.MiddleLeft, 12);
            nadePips = new Image[3];
            for (int i = 0; i < nadePips.Length; i++)
                nadePips[i] = UiKit.Img("Nade" + i, lp, TacKit.Pip, Amber, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(sx + 60 + i * 16, sy - 70), new Vector2(11, 11));

            // weapon slots on the right, wired to the model with leader lines
            Vector2 anchorPt = new Vector2(cx + 28, -Header - 135);
            for (int i = 0; i < 3; i++)
            {
                var s = MakeSlot(lp, new Vector2(320 + 113, -(Header + 8 + i * 128 + 57)), new Vector2(226, 114), true);
                s.order = i;
                loadoutSlots.Add(s);
                Vector2 end = new Vector2(320 - 4, -(Header + 8 + i * 128 + 57));
                Vector2 elbow = new Vector2(end.x - 26, end.y);
                Vector2 start = anchorPt + new Vector2(0, (i - 1) * 26);
                var l1 = Line(lp, start, elbow);
                var l2 = Line(lp, elbow, end);
                var dot = UiKit.Img("Dot", lp, TacKit.Pip, Cyan, new Vector2(0, 1), new Vector2(0.5f, 0.5f), start, new Vector2(9, 9));
                leaders.Add(new[] { l1, l2, dot });
            }
        }

        Image StatusBar(RectTransform p, string label, Vector2 pos, Color c, out Text value)
        {
            var ico = UiKit.Art(label == "HP" ? "a_icon_vitals" : "a_icon_shield");
            if (ico) UiKit.Img(label + "Icon", p, ico, c, new Vector2(0, 1), new Vector2(0, 0.5f), pos + new Vector2(-2, 0), new Vector2(18, 18));
            TacKit.MicroText(label + "Lbl", p, label, new Vector2(0, 1), new Vector2(0, 0.5f), pos + new Vector2(21, 0), TextAnchor.MiddleLeft, 12).color = A(c, 0.85f);
            const float W = 92f;
            var bar = UiKit.Node(label + "Bar", p, new Vector2(0, 1), new Vector2(0, 0.5f), pos + new Vector2(52, 0), new Vector2(W, 7));
            UiKit.Fill("Bg", bar, UiKit.White, new Color(1, 1, 1, 0.07f));
            var fill = UiKit.Fill("Fill", bar, UiKit.White, c);
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
            var t = HoloKit.Tiled("Ticks", bar, TacKit.Ruler, new Color(0, 0, 0, 0.5f));
            t.uvRect = new Rect(0, 0, W / 8f / 1.25f, 1);
            value = Txt(label + "Val", p, "", 16, c, TextAnchor.MiddleLeft, pos + new Vector2(52 + W + 8, 0), new Vector2(80, 20), new Vector2(0, 1));
            value.rectTransform.pivot = new Vector2(0, 0.5f);
            value.supportRichText = true;
            return fill;
        }

        Image Line(RectTransform parent, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            var img = UiKit.Img("Leader", parent, UiKit.White, A(Cyan, 0.5f), new Vector2(0, 1), new Vector2(0, 0.5f), a, new Vector2(d.magnitude, 1.5f));
            img.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            return img;
        }

        void BuildSelector()
        {
            selector = UiKit.Node("Selector", window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Slot, Slot));
            selBrackets = TacKit.Brackets(selector, Cyan, 18f, 5f);
            var glow = UiKit.Fill("Glow", selector, TacKit.SlotGlow, A(Cyan, 0.0f), -2f);
            glow.name = "Glow";
        }

        void BuildTooltip()
        {
            tooltip = Kit
                ? FieldKit.PaperBlock("Tooltip", root, new Vector2(0.5f, 0.5f), new Vector2(0, 1), Vector2.zero, new Vector2(470, 200))
                : TacKit.GlassPlate("Tooltip", root, new Vector2(0.5f, 0.5f), new Vector2(0, 1), Vector2.zero, new Vector2(470, 200), Cyan, 0.6f);
            if (!Kit) tooltip.Find("Fill").GetComponent<Image>().color = new Color(0.01f, 0.025f, 0.04f, 0.96f);
            var iconBox = UiKit.Node("IconBox", tooltip, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -16), new Vector2(112, 112));
            tooltipIconBox = iconBox;
            UiKit.Fill("Fill", iconBox, TacKit.Slot, SlotBg);
            tooltipHalo = UiKit.Fill("Halo", iconBox, HoloKit.SoftDot, A(Cyan, 0.25f), -10f);
            tooltipIconFrame = UiKit.Fill("Frame", iconBox, TacKit.SlotFrame, Cyan);
            tooltipIcon = Img("Icon", iconBox, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-18, -18), Color.white);
            tooltipIcon.preserveAspect = true;
            tooltipName = Txt("Name", tooltip, "", 24, UiKit.TextColor, TextAnchor.UpperLeft, new Vector2(144, -14), new Vector2(300, 30), new Vector2(0, 1));
            tooltipName.gameObject.AddComponent<DecodeText>();
            if (Kit) { tooltipName.font = FieldKit.Display; tooltipName.fontSize = 40; tooltipName.color = FieldKit.InkSolid; tooltipName.rectTransform.anchoredPosition = new Vector2(144, -8); }
            var pill = UiKit.Node("Pill", tooltip, new Vector2(0, 1), new Vector2(0, 1), new Vector2(144, Kit ? -56 : -48), new Vector2(96, 20));
            tooltipPill = UiKit.Fill("Fill", pill, UiKit.Skew, Cyan);
            tooltipPillText = Txt("Text", pill, "", 13, new Color(0.02f, 0.04f, 0.06f), TextAnchor.MiddleCenter, new Vector2(0, 1), new Vector2(120, 20), new Vector2(0.5f, 0.5f));
            tooltipPillText.GetComponent<Outline>().enabled = false;
            tooltipDesc = Txt("Desc", tooltip, "", 15, new Color(0.72f, 0.78f, 0.84f), TextAnchor.UpperLeft, new Vector2(144, -76), new Vector2(306, 200), new Vector2(0, 1));
            tooltipDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (Kit) { tooltipDesc.font = FieldKit.MonoFont; tooltipDesc.fontSize = 13; tooltipDesc.color = FieldKit.InkSolid; tooltipDesc.rectTransform.anchoredPosition = new Vector2(144, -84); }
            for (int i = 0; i < statRows.Length; i++)
            {
                var r = new StatRow();
                r.rt = UiKit.Node("Stat" + i, tooltip, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(438, 22));
                r.label = TacKit.MicroText("Label", r.rt, "", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, TextAnchor.MiddleLeft, 12);
                var bar = UiKit.Node("Bar", r.rt, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(96, 0), new Vector2(210, 6));
                UiKit.Fill("Bg", bar, UiKit.White, new Color(1, 1, 1, 0.07f));
                r.fill = UiKit.Fill("Fill", bar, UiKit.White, Cyan);
                r.fill.type = Image.Type.Filled; r.fill.fillMethod = Image.FillMethod.Horizontal;
                r.marker = UiKit.Img("Marker", bar, UiKit.White, Color.white, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2, 12));
                r.value = Txt("Value", r.rt, "", 16, UiKit.TextColor, TextAnchor.MiddleRight, new Vector2(370, 0), new Vector2(60, 20), new Vector2(0, 0.5f));
                r.value.rectTransform.pivot = new Vector2(1, 0.5f);
                r.delta = Txt("Delta", r.rt, "", 13, Color.green, TextAnchor.MiddleLeft, new Vector2(378, 0), new Vector2(60, 20), new Vector2(0, 0.5f));
                r.delta.rectTransform.pivot = new Vector2(0, 0.5f);
                if (Kit)
                {
                    r.label.font = FieldKit.Caption; r.label.fontSize = 16; r.label.color = FieldKit.InkSolid;
                    bar.Find("Bg").GetComponent<Image>().color = A(FieldKit.InkSolid, 0.16f);
                    r.marker.color = FieldKit.Signal;
                    r.value.font = FieldKit.Display; r.value.fontSize = 22; r.value.color = FieldKit.InkSolid;
                    r.delta.fontSize = 15;
                }
                statRows[i] = r;
            }
            tooltipFoot = Txt("Foot", tooltip, "", 15, Color.white, TextAnchor.UpperLeft, Vector2.zero, new Vector2(438, 60), new Vector2(0, 1));
            tooltipFoot.supportRichText = true;
            tooltipFoot.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (Kit) { tooltipFoot.color = FieldKit.InkSolid; tooltipFoot.fontSize = 17; }
        }

        void BuildFocusVolume()
        {
            var go = new GameObject("InventoryFocus");
            go.transform.SetParent(transform, false);
            focus = go.AddComponent<Volume>();
            focus.isGlobal = true;
            focus.priority = 50f;
            focus.weight = 0f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var dof = profile.Add<DepthOfField>(false);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(0f);
            dof.gaussianEnd.Override(6f);
            dof.gaussianMaxRadius.Override(1.5f);
            dof.highQualitySampling.Override(true);
            var col = profile.Add<ColorAdjustments>(false);
            col.saturation.Override(-30f);
            col.postExposure.Override(0.35f);
            focus.sharedProfile = profile;
        }

        RectTransform Panel(string name, Vector2 size, string title, string micro, Color accent)
        {
            RectTransform p;
            if (Kit)
            {
                // field kit: solid ink case with a header strip, a signal dot and a coloured top edge
                p = FieldKit.Block(name, window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, FieldKit.Ink);
                UiKit.Fill("Rim", p, TacKit.SlotFrame, A(FieldKit.Paper, 0.16f));
                bool orange = accent == FieldKit.Signal;
                if (orange) UiKit.Img("Edge", p, UiKit.White, FieldKit.Signal, new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -3), new Vector2(size.x - 16, 7));
                var head = FieldKit.PaperBlock("Head", p, new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -12), new Vector2(size.x - 16, 54));
                var kt = FieldKit.Label("Title", head, title, 46, FieldKit.InkSolid, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, -2), new Vector2(size.x - 60, 50));
                kt.name = "Title";
                if (!string.IsNullOrEmpty(micro))
                {
                    FieldKit.Mono("Micro", head, micro, 12, FieldKit.InkSolid, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 9), TextAnchor.MiddleRight);
                    var bc = HoloKit.Tiled("Barcode", head, FieldKit.Barcode, FieldKit.InkSolid);
                    var brt = bc.rectTransform;
                    brt.anchorMin = brt.anchorMax = new Vector2(1, 0.5f); brt.pivot = new Vector2(1, 0.5f);
                    brt.anchoredPosition = new Vector2(-14, -11); brt.sizeDelta = new Vector2(84, 11);
                }
            }
            else
            {
                p = TacKit.GlassPlate(name, window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, accent, 0.32f);
                var hex = HoloKit.Tiled("Hex", p, HoloKit.HexTile, A(accent, 0.035f));
                hex.uvRect = new Rect(0, 0, size.x / 52f, size.y / 90f);
                var t = TacKit.Header(p, title, micro, accent, size.x);
                t.name = "Title";
            }
            var cg = p.gameObject.AddComponent<CanvasGroup>();
            var sweep = UiKit.Node("Sweep", p, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x - 8, 60));
            var sg = UiKit.Fill("Glow", sweep, TacKit.GradUp, A(accent, Kit ? 0f : 0.12f));
            sg.rectTransform.localScale = new Vector3(1, -1, 1);
            UiKit.Img("Line", sweep, UiKit.White, A(accent, Kit ? 0f : 0.7f), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x - 8, 1.5f));
            panels.Add(new PanelAnim { rt = p, g = cg, sweep = sweep, delay = panels.Count * 0.06f });
            return p;
        }

        SlotUi MakeSlot(RectTransform parent, Vector2 center, Vector2 size, bool wide)
        {
            var s = new SlotUi { wide = wide };
            s.rt = UiKit.Node("Slot", parent, new Vector2(0, 1), new Vector2(0.5f, 0.5f), center, size);
            s.bg = UiKit.Fill("BG", s.rt, TacKit.Slot, SlotBg);
            s.hatch = HoloKit.Tiled("Hatch", s.rt, TacKit.Hatch, new Color(1, 1, 1, 0.025f));
            s.hatch.uvRect = new Rect(0, 0, size.x / 16f, size.y / 16f);
            s.glow = UiKit.Fill("Glow", s.rt, TacKit.SlotGlow, Color.clear);
            s.halo = UiKit.Img("Halo", s.rt, HoloKit.SoftDot, Color.clear, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -4), size * 0.95f);
            s.frame = UiKit.Fill("Frame", s.rt, TacKit.SlotFrame, new Color(1, 1, 1, 0.12f));
            s.strip = UiKit.Img("Rarity", s.rt, TacKit.Tab, Color.clear, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0), wide ? new Vector2(26, 5) : new Vector2(20, 4));
            if (wide)
            {
                s.label = Txt("Name", s.rt, "", 16, Color.white, TextAnchor.UpperLeft, new Vector2(12, -9), new Vector2(170, 22), new Vector2(0, 1));
                s.icon = Img("Icon", s.rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -7), new Vector2(196, 58), Color.white);
                s.hotkey = Txt("Key", s.rt, "", 14, UiKit.TextDim, TextAnchor.UpperRight, new Vector2(-10, -9), new Vector2(40, 20), new Vector2(1, 1));
                s.sub = Txt("Sub", s.rt, "", 14, UiKit.TextDim, TextAnchor.LowerRight, new Vector2(-14, 6), new Vector2(120, 20), new Vector2(1, 0));
                var pill = UiKit.Node("Pill", s.rt, new Vector2(0, 0), new Vector2(0, 0), new Vector2(8, 7), new Vector2(104, 18));
                s.pill = UiKit.Fill("Fill", pill, UiKit.Skew, Amber);
                s.pillText = Txt("Text", pill, UiKit.Spaced("EQUIPPED"), 13, new Color(0.08f, 0.05f, 0.01f), TextAnchor.MiddleCenter, new Vector2(0, 1), new Vector2(120, 20), new Vector2(0.5f, 0.5f));
                s.pillText.GetComponent<Outline>().enabled = false;
            }
            else
            {
                s.icon = Img("Icon", s.rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -4), new Vector2(64, 64), Color.white);
                s.label = Txt("Name", s.rt, "", 12, UiKit.TextColor, TextAnchor.UpperLeft, new Vector2(8, -7), new Vector2(84, 16), new Vector2(0, 1));
            }
            s.icon.preserveAspect = true;
            s.count = Txt("Count", s.rt, "", 16, Color.white, TextAnchor.LowerRight, new Vector2(-9, 4), new Vector2(70, 24), new Vector2(1, 0));

            // crate scan overlay
            var sc = UiKit.Stretch("Scan", s.rt);
            s.scan = sc.gameObject;
            var sh = HoloKit.Tiled("Hatch", sc, TacKit.Hatch, A(Amber, 0.08f));
            sh.uvRect = new Rect(0, 0, size.x / 16f, size.y / 16f);
            s.scanBar = UiKit.Img("Bar", sc, UiKit.White, A(Amber, 0.8f), new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x - 10, 2));
            TacKit.MicroText("Txt", sc, "SCAN", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, TextAnchor.MiddleCenter, 11).color = A(Amber, 0.8f);
            if (Kit)
            {
                // sealed compartment: hazard tape with a SCAN plate
                sh.enabled = false;
                s.scanBar.color = Color.clear;
                sc.Find("Txt").gameObject.SetActive(false);
                UiKit.Fill("Seal", sc, TacKit.Slot, FieldKit.InkSolid, 3f);
                var stripes = FieldKit.Stripes("Tape", sc, FieldKit.Signal, size, 0.5f);
                stripes.rectTransform.offsetMin = new Vector2(6, 6); stripes.rectTransform.offsetMax = new Vector2(-6, -6);
                UiKit.Img("Plate", sc, UiKit.White, FieldKit.Signal, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x - 12, 30));
                FieldKit.Label("PlateT", sc, "SCAN", 26, FieldKit.InkSolid, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -1), new Vector2(size.x, 28));

                s.strip.enabled = false;
                s.sticker = UiKit.Img("Sticker", s.rt, UiKit.RoundFill, Color.clear, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-5, 5), new Vector2(36, 21));
                s.sticker.transform.SetSiblingIndex(s.count.transform.GetSiblingIndex());
                var crt = s.count.rectTransform;
                crt.anchoredPosition = new Vector2(-5, 4); crt.sizeDelta = new Vector2(36, 21);
                s.count.alignment = TextAnchor.MiddleCenter; s.count.font = FieldKit.Display; s.count.fontSize = 20;
                if (wide)
                {
                    s.label.font = FieldKit.Display; s.label.fontSize = 27;
                    s.label.rectTransform.anchoredPosition = new Vector2(46, -6);
                    s.hotkey.font = FieldKit.Display; s.hotkey.fontSize = 22; s.hotkey.alignment = TextAnchor.MiddleCenter;
                    var hk = s.hotkey.rectTransform;
                    hk.anchorMin = hk.anchorMax = hk.pivot = new Vector2(0, 1); hk.anchoredPosition = new Vector2(9, -8); hk.sizeDelta = new Vector2(28, 26);
                    var keyBox = UiKit.Img("KeyBox", s.rt, UiKit.RoundFill, A(FieldKit.Paper, 0.92f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(9, -8), new Vector2(28, 26));
                    keyBox.transform.SetSiblingIndex(s.hotkey.transform.GetSiblingIndex());
                    s.hotkey.color = FieldKit.InkSolid;
                    s.sub.fontSize = 17;
                    s.pillText.font = FieldKit.Caption;
                }
                else
                {
                    s.tag = UiKit.Img("Tag", s.rt, UiKit.RoundFill, Color.clear, new Vector2(0, 1), new Vector2(0, 1), new Vector2(5, -5), new Vector2(38, 18));
                    s.tagText = FieldKit.Label("TagT", s.tag.transform, "", 14, FieldKit.InkSolid, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(50, 18), FieldKit.Caption);
                    var lrt = s.label.rectTransform;
                    lrt.anchorMin = lrt.anchorMax = lrt.pivot = Vector2.zero; lrt.anchoredPosition = new Vector2(7, 5); lrt.sizeDelta = new Vector2(84, 18);
                    s.label.alignment = TextAnchor.LowerLeft; s.label.fontSize = 15;
                    s.icon.rectTransform.anchoredPosition = new Vector2(0, 2);
                }
            }
            s.scan.SetActive(false);
            return s;
        }

        Image Img(string name, RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.sprite = ProcTex.WhiteSprite;
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        Text Txt(string name, RectTransform parent, string text, int size, Color c, TextAnchor align, Vector2 pos, Vector2 box, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor; rt.anchoredPosition = pos; rt.sizeDelta = box;
            var t = go.AddComponent<Text>();
            t.font = font; t.text = text; t.fontSize = size; t.color = c; t.alignment = align;
            t.fontStyle = FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            var o = go.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, 0.7f);
            o.effectDistance = new Vector2(1.2f, -1.2f);
            if (Kit) { t.font = FieldKit.Caption; o.enabled = false; }
            else t.font = TacKit.Cap;
            return t;
        }

        void SetVisible(bool v)
        {
            dim.SetActive(v);
            window.gameObject.SetActive(v);
            if (!v) { tooltip.gameObject.SetActive(false); ghost.gameObject.SetActive(false); }
        }

        // ------------------------------------------------------------------ update

        void LateUpdate()
        {
            var player = PlayerController.I;
            inv = player ? player.inventory : null;
            bool open = inv && inv.PanelOpen && player.CanAct;
            float dt = Time.unscaledDeltaTime;
            if (focus) focus.weight = FocusBlur ? Mathf.MoveTowards(focus.weight, open ? 1f : 0f, dt * (open ? 5f : 7f)) : 0f;
            if (window.gameObject.activeSelf != open)
            {
                SetVisible(open);
                if (open)
                {
                    seenVersion = -1; seenContainerVersion = -1; openT = 0f;
                    foreach (var s in AllSlotsRaw()) s.fadeA = 0f;
                    selAlpha = 0f;
                    ProcAudio.Play("ui_open", 0.5f, 0f);
                }
                else { CancelDrag(); ProcAudio.Play("ui_close", 0.45f, 0f); }
                dim.transform.SetAsLastSibling();
                window.SetAsLastSibling();
                tooltip.SetAsLastSibling();
                ghost.SetAsLastSibling();
            }
            if (PreviewStage.I) PreviewStage.I.BodyViewActive = open;
            if (!open) return;

            openT += dt;
            Layout();
            Bind();
            Animate(dt);
            Refresh();
            HandleMouse();
        }

        void Layout()
        {
            bool hasContainer = inv.OpenContainer != null;
            bool stash = hasContainer && inv.OpenContainer.isStash;
            containerPanel.gameObject.SetActive(hasContainer && !stash);
            stashPanel.gameObject.SetActive(stash);
            float bpW = backpackPanel.sizeDelta.x, loW = loadoutPanel.sizeDelta.x, coW = (stash ? stashPanel : containerPanel).sizeDelta.x;
            float total = loW + 20 + bpW + (hasContainer ? 20 + coW : 0);
            float x = -total * 0.5f, y = 26f;
            panels[0].home = new Vector2(x + loW * 0.5f, y);
            x += loW + 20;
            panels[1].home = new Vector2(x + bpW * 0.5f, y);
            x += bpW + 20;
            panels[2].home = new Vector2(x + coW * 0.5f, y);
            panels[3].home = panels[2].home;
            hintBar.sizeDelta = new Vector2(total, 44);
            hintBar.anchoredPosition = new Vector2(0, y - PanelH * 0.5f - 14);
            titleRoot.anchoredPosition = new Vector2(-total * 0.5f + 4, y + PanelH * 0.5f + 46);
            string where = (GameManager.I ? GameManager.I.CurrentArea : "KEPLER-7").ToUpperInvariant() + (GameManager.InHub ? "  -  DROPSHIP KESTREL" : "  -  LUNAR MINING COLONY");
            if (titleSub.text != where) titleSub.text = where;
            if (stash)
            {
                int used = 0;
                for (int i = 0; i < inv.OpenContainer.Size; i++) if (!inv.OpenContainer[i].IsEmpty) used++;
                string st = Kit ? $"{used} / {inv.OpenContainer.Size}" : $"<size=13><color=#{Hex(UiKit.TextDim)}>SLOTS</color></size>  {used} / {inv.OpenContainer.Size}";
                if (stashMicro.text != st) stashMicro.text = st;
            }
            else if (hasContainer)
            {
                containerTitle.text = inv.OpenContainerName ?? "CONTAINER";
                bool any = false;
                int items = 0;
                for (int i = 0; i < inv.OpenContainer.Size; i++) if (!inv.OpenContainer[i].IsEmpty) { any = true; items++; }
                float p = any ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f) : 0.25f;
                takeAllFrame.color = A(Amber, Kit ? 0f : p);
                takeAllFill.color = Kit ? (any ? FieldKit.Signal : FieldKit.InkSoft)
                    : any ? new Color(0.22f, 0.14f, 0.03f, 0.9f) : new Color(0.05f, 0.05f, 0.05f, 0.6f);
                if (Kit) takeAllLabel.color = any ? FieldKit.InkSolid : A(FieldKit.Paper, 0.35f);
                bool scanning = scanStart >= 0f && Time.unscaledTime < scanStart + ScanLead + ScanStep * inv.OpenContainer.Size;
                containerMicro.text = scanning ? $"SCANNING{new string('.', 1 + (int)(Time.unscaledTime * 6f) % 3)}" : any ? $"{items} SIGNATURE{(items == 1 ? "" : "S")} FOUND" : "EMPTY";
                containerMicro.color = scanning ? A(Amber, 0.8f) : TacKit.Micro;
            }
        }

        void Bind()
        {
            for (int i = 0; i < loadoutSlots.Count; i++) { loadoutSlots[i].container = inv.WeaponSlots; loadoutSlots[i].index = i; }
            for (int i = 0; i < backpackSlots.Count; i++)
            {
                backpackSlots[i].container = i < inv.Backpack.Size ? inv.Backpack : null;
                backpackSlots[i].index = i;
                backpackSlots[i].rt.gameObject.SetActive(i < inv.Backpack.Size);
            }
            if (boundContainer != inv.OpenContainer)
            {
                boundContainer = inv.OpenContainer;
                seenContainerVersion = -1;
                // first look inside a crate: scan the items in one by one (the stash is your own: no scan)
                if (boundContainer != null && !boundContainer.isStash && scanned.Add(boundContainer))
                {
                    scanStart = Time.unscaledTime;
                    ProcAudio.Play("ui_scan", 0.45f, 0f);
                }
                else scanStart = -1f;
            }
            var active = boundContainer != null && boundContainer.isStash ? stashSlots : crateSlots;
            foreach (var s in containerSlots)
                if (!active.Contains(s)) { s.container = null; s.rt.gameObject.SetActive(false); }
            for (int i = 0; i < active.Count; i++)
            {
                bool on = boundContainer != null && i < boundContainer.Size;
                active[i].container = on ? boundContainer : null;
                active[i].index = i;
                active[i].rt.gameObject.SetActive(on);
            }
        }

        bool Unrevealed(SlotUi s, out float k)
        {
            k = 1f;
            if (s.container == null || s.container != boundContainer || scanStart < 0f) return false;
            float at = scanStart + ScanLead + ScanStep * s.index;
            k = Mathf.Clamp01((Time.unscaledTime - scanStart) / (ScanLead + ScanStep * s.index));
            return Time.unscaledTime < at;
        }

        /// <summary>Open fold-in, slot cascade, pedestal spin, scan overlays, pops.</summary>
        void Animate(float dt)
        {
            dimGroup.alpha = Mathf.Clamp01(openT / 0.18f);
            foreach (var p in panels)
            {
                float k = Mathf.Clamp01((openT - p.delay) / 0.24f);
                float e = Ease.OutCubic(k);
                // hologram: flickers on; field kit: the cases slide up and latch
                p.g.alpha = Kit ? Mathf.Clamp01(k * 3f) : k <= 0f ? 0f : k < 0.5f ? (Mathf.Sin(openT * 90f) > 0f ? 0.85f : 0.35f) * e * 2f : e;
                p.rt.anchoredPosition = p.home + new Vector2(0, Kit ? (1f - Ease.OutBack(k)) * -70f : (1f - e) * -26f);
                p.rt.localScale = new Vector3(1f, Kit ? 1f : Mathf.Lerp(0.94f, 1f, e), 1f);
                float sk = Mathf.Clamp01((openT - p.delay - 0.05f) / 0.45f);
                p.sweep.gameObject.SetActive(sk > 0f && sk < 1f);
                p.sweep.anchoredPosition = new Vector2(0, -Ease.OutCubic(sk) * PanelH);
            }

            void Cascade(List<SlotUi> list, float delay)
            {
                foreach (var s in list)
                {
                    if (!s.rt.gameObject.activeSelf) continue;
                    float k = Mathf.Clamp01((openT - delay - s.order * 0.03f) / 0.18f);
                    s.pop = Mathf.MoveTowards(s.pop, 0f, dt * 5f);
                    float sc = Mathf.Lerp(0.82f, 1f, Ease.OutBack(k)) + s.pop * 0.12f;
                    s.rt.localScale = new Vector3(sc, sc, 1f);
                    if (k < 1f || s.fadeA < 1f)
                    {
                        s.fadeA = k;
                        foreach (var g in s.rt.GetComponentsInChildren<Graphic>(true)) g.canvasRenderer.SetAlpha(k);
                    }

                    bool hidden = Unrevealed(s, out float sk);
                    if (s.scan.activeSelf != hidden)
                    {
                        s.scan.SetActive(hidden);
                        if (!hidden && s.container != null && !s.container[s.index].IsEmpty) { s.pop = 1f; ProcAudio.Play("ui_reveal", 0.4f, 0.02f, 1f + s.index * 0.03f); }
                    }
                    s.icon.gameObject.SetActive(!hidden);
                    s.count.gameObject.SetActive(!hidden);
                    s.label.gameObject.SetActive(!hidden);
                    if (s.tag) s.tag.gameObject.SetActive(!hidden);
                    if (s.sticker) s.sticker.gameObject.SetActive(!hidden);
                    if (hidden) s.scanBar.rectTransform.anchoredPosition = new Vector2(0, Mathf.PingPong(Time.unscaledTime * 160f, s.rt.sizeDelta.y - 4f) + 2f);
                }
            }
            Cascade(loadoutSlots, 0.05f);
            Cascade(backpackSlots, 0.1f);
            Cascade(containerSlots, 0.16f);

            pedestalA.localRotation = Quaternion.Euler(0, 0, Time.unscaledTime * 18f);
            pedestalB.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * 32f);
        }

        void Refresh()
        {
            int v = inv.Version;
            int cv = boundContainer != null ? boundContainer.Version : 0;
            var ph = PlayerController.I;
            var hp = ph ? ph.health : null;
            var gr = ph ? ph.grenades : null;
            if (hp)
            {
                hpFill.fillAmount = hp.Health01;
                shFill.fillAmount = hp.Shield01;
                hpText.text = $"{Mathf.CeilToInt(hp.Health)}<color=#{Hex(UiKit.TextDim)}><size=12> / {Mathf.RoundToInt(hp.maxHealth)}</size></color>";
                shText.text = $"{Mathf.CeilToInt(hp.Shield)}";
            }
            for (int i = 0; i < nadePips.Length; i++)
            {
                bool on = gr && i < gr.maxCharges;
                nadePips[i].enabled = on;
                if (on) nadePips[i].color = i < gr.Charges ? Amber : new Color(1, 1, 1, 0.15f);
            }

            int used = 0, value = 0;
            float kg = 0f;
            foreach (var st in inv.Backpack.slots)
            {
                if (st.IsEmpty) continue;
                used++;
                kg += st.Def.weight * st.count;
                value += st.Def.value * st.count;
            }
            foreach (var st in inv.WeaponSlots.slots) if (!st.IsEmpty) kg += st.Def.weight;
            // value counter rolls towards the real total
            shownValue = Mathf.Abs(shownValue - value) < 1f ? value : Mathf.Lerp(shownValue, value, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 9f));
            valueText.text = $"{Mathf.RoundToInt(shownValue):N0}<size=15> cr</size>";
            const float nominal = 40f;
            massFill.fillAmount = Mathf.Clamp01(kg / nominal);
            massFill.color = kg > nominal * 0.8f ? Amber : Cyan;
            carryText.text = $"{kg:0.0}<size=13><color=#{Hex(UiKit.TextDim)}> / {nominal:0} kg</color></size>";

            if (v == seenVersion && cv == seenContainerVersion) return;
            seenVersion = v;
            seenContainerVersion = cv;
            foreach (var s in loadoutSlots) Draw(s);
            foreach (var s in backpackSlots) if (s.container != null) Draw(s);
            foreach (var s in containerSlots) if (s.container != null) Draw(s);
            backpackCount.text = Kit ? $"SLOTS  {used} / {inv.Backpack.Size}"
                : $"<color=#{Hex(TacKit.Micro)}><size=12>SLOTS</size></color>  {used}<color=#{Hex(UiKit.TextDim)}> / {inv.Backpack.Size}</color>";

            for (int i = 0; i < leaders.Count && i < loadoutSlots.Count; i++)
            {
                var st = inv.WeaponSlots[i];
                bool eq = i == inv.EquippedIndex && !st.IsEmpty;
                Color c = st.IsEmpty ? new Color(1, 1, 1, 0.08f) : eq ? A(Amber, 0.85f) : A(st.Def.rarityColor, 0.45f);
                foreach (var img in leaders[i]) img.color = c;
            }
        }

        void Draw(SlotUi s)
        {
            var st = s.container[s.index];
            bool equipped = s.container == inv.WeaponSlots && s.index == inv.EquippedIndex && !st.IsEmpty;
            if (s.hotkey) s.hotkey.text = $"{s.index + 1}";
            if (s.pill) s.pill.transform.parent.gameObject.SetActive(equipped);
            // new or changed content pops
            if (!st.IsEmpty && (st.id != s.shownId || st.count > s.shownCount) && openT > 0.4f) s.pop = 1f;
            s.shownId = st.IsEmpty ? ItemId.None : st.id;
            s.shownCount = st.IsEmpty ? 0 : st.count;
            if (st.IsEmpty)
            {
                s.icon.enabled = false;
                s.count.text = "";
                if (s.sticker) s.sticker.color = Color.clear;
                if (s.tag) { s.tag.color = Color.clear; s.tagText.text = ""; }
                s.strip.color = Color.clear;
                s.glow.color = Color.clear;
                s.halo.color = Color.clear;
                s.hatch.enabled = true;
                s.frame.color = new Color(1, 1, 1, 0.1f);
                s.label.text = s.wide ? UiKit.Spaced("EMPTY SLOT") : "";
                s.label.color = new Color(1, 1, 1, 0.22f);
                if (s.sub) s.sub.text = "";
                return;
            }
            var d = st.Def;
            s.hatch.enabled = false;
            s.icon.enabled = true;
            s.icon.sprite = ItemIcons.Get(st.id);
            if (!s.wide)
            {
                // a weapon is a wide render: lay it diagonally across a square slot so it fills it
                bool gun = d.category == ItemCategory.Weapon;
                s.icon.rectTransform.sizeDelta = gun ? new Vector2(88, 44) : new Vector2(64, 64);
                s.icon.rectTransform.localRotation = Quaternion.Euler(0, 0, gun ? 30f : 0f);
            }
            s.count.text = st.count > 1 ? $"x{st.count}" : "";
            s.strip.color = d.rarityColor;
            s.glow.color = A(d.rarityColor, Kit ? 0.1f : equipped ? 0.45f : 0.28f);
            s.halo.color = A(d.color, Kit ? 0.07f : 0.16f);
            s.frame.color = equipped ? Amber : Kit ? A(FieldKit.Paper, 0.28f) : A(d.rarityColor, 0.55f);
            if (Kit)
            {
                s.count.color = FieldKit.InkSolid;
                s.sticker.color = st.count > 1 ? FieldKit.Paper : Color.clear;
                if (s.tag)
                {
                    s.tag.color = d.rarityColor;
                    s.tagText.text = d.rarity.Length <= 4 ? d.rarity : d.rarity.Substring(0, 3);
                }
            }
            s.label.text = d.name;
            s.label.color = s.wide ? UiKit.TextColor : new Color(0.85f, 0.9f, 0.95f, 0.85f);
            if (s.wide)
            {
                var w = WeaponDefs.Get(d.weapon);
                s.sub.text = w.beam ? $"{w.Dps:0} DPS BEAM" : w.pellets > 1 ? $"{w.damage:0}x{w.pellets} DMG" : $"{w.damage:0} DMG";
            }
        }

        // ------------------------------------------------------------------ mouse

        IEnumerable<SlotUi> AllSlotsRaw()
        {
            foreach (var s in loadoutSlots) yield return s;
            foreach (var s in backpackSlots) yield return s;
            foreach (var s in containerSlots) yield return s;
        }

        IEnumerable<SlotUi> AllSlots()
        {
            foreach (var s in loadoutSlots) yield return s;
            foreach (var s in backpackSlots) if (s.container != null) yield return s;
            foreach (var s in containerSlots) if (s.container != null) yield return s;
        }

        SlotUi SlotAt(Vector2 screen)
        {
            foreach (var s in AllSlots())
                if (s.rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(s.rt, screen, UiCam))
                    return Unrevealed(s, out _) ? null : s;
            return null;
        }

        bool OverWindow(Vector2 screen)
        {
            foreach (var p in new[] { loadoutPanel, backpackPanel, containerPanel, stashPanel })
                if (p.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(p, screen, UiCam)) return true;
            return false;
        }

        Vector2 ToLocal(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, UiCam, out var local);
            return local;
        }

        void HandleMouse()
        {
            var input = PlayerInputSource.Current;
            Vector2 mouse = input.pointer;
            var hover = SlotAt(mouse);

            // Hover highlight
            foreach (var s in AllSlots())
            {
                bool h = s == hover && !s.container[s.index].IsEmpty;
                s.bg.color = h ? new Color(0.06f, 0.15f, 0.19f, 0.95f) : SlotBg;
            }
            UpdateSelector(hover);

            if (input.click && hover == null && boundContainer != null && takeAllButton.gameObject.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(takeAllButton, mouse, UiCam))
            {
                inv.TakeAll();
                ProcAudio.Play("ui_drop", 0.6f, 0.03f, 1.15f);
            }

            if (input.click && hover == null && operativeView && RectTransformUtility.RectangleContainsScreenPoint(operativeView.rectTransform, mouse, UiCam))
            {
                rotating = true;
                lastPointer = mouse;
            }
            if (rotating)
            {
                if (PreviewStage.I) PreviewStage.I.RotateBody(-(mouse.x - lastPointer.x) * 0.45f);
                lastPointer = mouse;
                if (input.clickUp) rotating = false;
            }

            if (input.click && hover != null && !hover.container[hover.index].IsEmpty)
            {
                pressed = hover;
                pressPos = mouse;
                var st = hover.container[hover.index];
                dragAmount = input.shift && st.count > 1 ? Mathf.CeilToInt(st.count / 2f) : -1;
            }

            if (pressed != null && !dragging && (mouse - pressPos).sqrMagnitude > 36f)
            {
                dragging = true;
                dragFrom = pressed;
                ProcAudio.Play("ui_pick", 0.5f, 0.04f);
            }

            if (input.clickUp && pressed != null)
            {
                if (dragging) EndDrag(hover, mouse);
                else if (hover == pressed) ClickSlot(hover);
                CancelDrag();
            }

            if (input.rightClick && hover != null && !dragging) QuickMove(hover);

            UpdateGhost(mouse);
            UpdateTooltip(hover, mouse);
        }

        /// <summary>Brackets spring to the hovered slot; while dragging they show whether the drop is accepted.</summary>
        void UpdateSelector(SlotUi hover)
        {
            float dt = Time.unscaledDeltaTime;
            bool show = hover != null && (dragging || !hover.container[hover.index].IsEmpty);
            if (show && hover != lastHover) ProcAudio.Play("ui_hover", 0.35f, 0.03f);
            lastHover = show ? hover : null;
            Color c = Kit ? FieldKit.Signal : Cyan;
            if (show)
            {
                Vector3 wp = hover.rt.position;
                Vector2 target = window.InverseTransformPoint(wp);
                Vector2 size = hover.rt.sizeDelta + new Vector2(8, 8);
                if (selAlpha < 0.05f) { selPos = target; selSize = size * 1.25f; }
                selVel += ((target - selPos) * 420f - selVel * 34f) * dt;
                selPos += selVel * dt;
                selSizeVel += ((size - selSize) * 420f - selSizeVel * 30f) * dt;
                selSize += selSizeVel * dt;
                if (dragging && dragFrom != null)
                {
                    var st = dragFrom.container[dragFrom.index];
                    bool ok = hover == dragFrom || hover.container.Accepts(st.id);
                    c = ok ? Cyan : Warn;
                }
                else if (hover.container == boundContainer && boundContainer != null) c = Amber;
            }
            selAlpha = Mathf.MoveTowards(selAlpha, show ? 1f : 0f, dt * 10f);
            selector.gameObject.SetActive(selAlpha > 0.01f);
            selector.anchoredPosition = selPos;
            selector.sizeDelta = selSize;
            selector.SetAsLastSibling();
            float pulse = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 8f);
            foreach (var b in selBrackets) b.color = A(c, selAlpha * pulse);
            selector.Find("Glow").GetComponent<Image>().color = A(c, selAlpha * 0.25f);
        }

        void ClickSlot(SlotUi s)
        {
            if (s.container == inv.WeaponSlots && !s.container[s.index].IsEmpty) { inv.Equip(s.index); s.pop = 1f; ProcAudio.Play("ui_equip", 0.55f, 0.03f); }
        }

        void EndDrag(SlotUi target, Vector2 mouse)
        {
            var from = dragFrom;
            if (from == null || from.container[from.index].IsEmpty) return;
            if (target != null)
            {
                if (target == from) return;
                bool ok = ItemContainer.Move(from.container, from.index, target.container, target.index, dragAmount);
                ProcAudio.Play(ok ? (target.container == inv.WeaponSlots ? "ui_equip" : "ui_drop") : "ui_deny", 0.55f, 0.04f);
                if (ok) target.pop = 1f;
                if (ok && target.container == inv.WeaponSlots && from.container != inv.WeaponSlots && inv.EquippedIndex == target.index) inv.Equip(target.index);
            }
            else if (!OverWindow(mouse) && !GameManager.InHub)      // nothing gets left lying around on the ship
            {
                var stack = from.container.Take(from.index, dragAmount);
                inv.DropToWorld(stack);
            }
        }

        /// <summary>RMB: backpack weapon -> loadout, loadout -> backpack, crate -> inventory, backpack resource -> open crate.</summary>
        void QuickMove(SlotUi s)
        {
            var st = s.container[s.index];
            if (st.IsEmpty) return;
            bool ok = false;
            if (s.container == boundContainer && boundContainer != null)
            {
                int left = inv.AddItem(st);
                s.container.Set(s.index, new ItemStack(st.id, left));
                ok = left < st.count;
            }
            else if (s.container == inv.WeaponSlots)
            {
                int e = inv.Backpack.FirstEmpty();
                if (e >= 0) ok = ItemContainer.Move(inv.WeaponSlots, s.index, inv.Backpack, e);
            }
            else if (s.container == inv.Backpack)
            {
                if (boundContainer != null && boundContainer.isStash)
                {
                    int left = boundContainer.Add(st);
                    s.container.Set(s.index, new ItemStack(st.id, left));
                    ok = left < st.count;
                }
                else if (Items.IsWeapon(st.id))
                {
                    int e = inv.WeaponSlots.FirstEmpty();
                    ok = ItemContainer.Move(inv.Backpack, s.index, inv.WeaponSlots, e >= 0 ? e : inv.EquippedIndex);
                }
                else if (boundContainer != null)
                {
                    int left = boundContainer.Add(st);
                    s.container.Set(s.index, new ItemStack(st.id, left));
                    ok = left < st.count;
                }
            }
            ProcAudio.Play(ok ? "ui_drop" : "ui_deny", 0.55f, 0.04f);
        }

        void CancelDrag()
        {
            if (dragFrom != null) seenVersion = -1;      // redraw the source slot (clears its drag hatching)
            dragging = false;
            dragFrom = null;
            pressed = null;
            if (ghost) ghost.gameObject.SetActive(false);
        }

        void UpdateGhost(Vector2 mouse)
        {
            bool show = dragging && dragFrom != null && !dragFrom.container[dragFrom.index].IsEmpty;
            ghost.gameObject.SetActive(show);
            if (dragFrom != null && show) dragFrom.hatch.enabled = true;
            if (!show) return;
            var st = dragFrom.container[dragFrom.index];
            ghostIcon.sprite = ItemIcons.Get(st.id);
            int n = dragAmount < 0 ? st.count : Mathf.Min(dragAmount, st.count);
            ghostCount.text = n > 1 ? n.ToString() : "";
            bool outside = !OverWindow(mouse);
            dropLabel.enabled = outside;
            ghostIcon.color = outside ? new Color(1f, 0.6f, 0.55f, 0.85f) : new Color(1, 1, 1, 0.95f);
            ghostFrame.color = outside ? Warn : st.Def.rarityColor;
            ghost.anchoredPosition = ToLocal(mouse) + new Vector2(0, 4f + Mathf.Sin(Time.unscaledTime * 6f) * 2f);
            ghost.localScale = Vector3.one * 1.08f;
            ghost.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.unscaledTime * 3f) * 2f);
        }

        // ------------------------------------------------------------------ tooltip

        static float MaxStat(System.Func<WeaponDef, float> f)
        {
            float m = 0.001f;
            foreach (WeaponKind k in System.Enum.GetValues(typeof(WeaponKind))) m = Mathf.Max(m, f(WeaponDefs.Get(k)));
            return m;
        }

        void UpdateTooltip(SlotUi hover, Vector2 mouse)
        {
            bool show = hover != null && !dragging && !hover.container[hover.index].IsEmpty;
            tooltip.gameObject.SetActive(show);
            if (!show) { tipShown = ItemId.None; return; }
            var st = hover.container[hover.index];
            var d = st.Def;
            if (tipShown != st.id)
            {
                tipShown = st.id;
                tooltipName.GetComponent<DecodeText>().Set(UiKit.Spaced(d.name), 0.3f);
                tooltipName.GetComponent<DecodeText>().Replay();
            }
            tooltipIcon.sprite = ItemIcons.Get(st.id);
            tooltipIconFrame.color = d.rarityColor;
            tooltipHalo.color = A(d.color, 0.3f);
            tooltipPill.color = d.rarityColor;
            tooltipPillText.text = UiKit.Spaced(d.rarity);
            ((RectTransform)tooltipPill.transform.parent).sizeDelta = new Vector2(tooltipPillText.preferredWidth + 26, 20);
            tooltipPillText.rectTransform.sizeDelta = new Vector2(tooltipPillText.preferredWidth + 26, 20);
            tooltipDesc.text = d.description;

            // weapons get a wide render across the top of the card; other items a square icon on the left
            bool wpn = d.category == ItemCategory.Weapon;
            tooltipIconBox.sizeDelta = wpn ? new Vector2(438, 132) : new Vector2(112, 112);
            float tx = wpn ? 18f : 144f, drop = wpn ? -146f : 0f;
            tooltipName.rectTransform.anchoredPosition = new Vector2(tx, (Kit ? -8f : -14f) + drop);
            ((RectTransform)tooltipPill.transform.parent).anchoredPosition = new Vector2(tx, (Kit ? -56f : -48f) + drop);
            tooltipDesc.rectTransform.anchoredPosition = new Vector2(tx, (Kit ? -84f : -76f) + drop);
            tooltipDesc.rectTransform.sizeDelta = new Vector2(wpn ? 432f : 306f, 200f);
            float y = (Kit ? -84f : -76f) + drop - Mathf.Max(tooltipDesc.preferredHeight, 20f) - 12f;
            if (!wpn) y = Mathf.Min(y, -142f);
            int rows = 0;
            if (d.category == ItemCategory.Weapon)
            {
                var w = WeaponDefs.Get(d.weapon);
                WeaponDef cmp = null;
                var eqSt = inv.EquippedIndex >= 0 && inv.EquippedIndex < inv.WeaponSlots.Size ? inv.WeaponSlots[inv.EquippedIndex] : ItemStack.Empty;
                if (!eqSt.IsEmpty && eqSt.id != st.id) cmp = WeaponDefs.Get(eqSt.Def.weapon);
                void Row(string label, System.Func<WeaponDef, float> f, string fmt)
                {
                    var r = statRows[rows++];
                    r.rt.gameObject.SetActive(true);
                    r.rt.anchoredPosition = new Vector2(16, y - (rows - 1) * 24f);
                    float max = MaxStat(f), v = f(w);
                    r.label.text = label;
                    r.fill.fillAmount = v / max;
                    r.fill.color = Kit ? FieldKit.InkSolid : d.rarityColor;
                    r.value.text = v.ToString(fmt);
                    r.marker.enabled = cmp != null;
                    r.delta.text = "";
                    if (cmp != null)
                    {
                        float cv = f(cmp);
                        r.marker.rectTransform.anchoredPosition = new Vector2(210f * cv / max, 0);
                        float dv = v - cv;
                        if (Mathf.Abs(dv) > 0.05f)
                        {
                            r.delta.text = (dv > 0 ? "+" : "") + dv.ToString(fmt);
                            r.delta.color = Kit ? (dv > 0 ? new Color(0.08f, 0.45f, 0.14f) : new Color(0.75f, 0.1f, 0.08f))
                                : dv > 0 ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.45f, 0.4f);
                        }
                    }
                }
                Row("DAMAGE", x => x.damage * x.pellets, "0");
                Row(w.beam ? "TICK RATE" : "FIRE RATE", x => x.beam ? 1f / x.tickInterval : x.fireRate, "0.#");
                Row("DPS", x => x.Dps, "0");
                Row("RANGE", x => x.range, "0");
                Row("CELL", x => x.cellSize, "0");
            }
            for (int i = rows; i < statRows.Length; i++) statRows[i].rt.gameObject.SetActive(false);
            if (rows > 0) y -= rows * 24f + 6f;

            var sb = new System.Text.StringBuilder();
            string dimHex = Kit ? "4a4a46" : Hex(UiKit.TextDim);
            sb.Append($"<color=#{dimHex}>MASS</color>  <b>{d.weight * st.count:0.0} kg</b>     <color=#{dimHex}>VALUE</color>  <color=#{Hex(Amber)}><b>{d.value * st.count:N0} cr</b></color>");
            if (st.count > 1 || d.maxStack > 1) sb.Append($"     <color=#{dimHex}>STACK</color>  <b>{st.count}/{d.maxStack}</b>");
            sb.Append("\n<size=12><color=#6f8090>");
            sb.Append(hover.container == boundContainer ? "RMB  take" : hover.container == inv.Backpack && boundContainer != null && boundContainer.isStash ? "RMB  store in stash" : d.category == ItemCategory.Weapon && hover.container == inv.Backpack ? "RMB  equip to loadout" :
                hover.container == inv.WeaponSlots ? "RMB  move to backpack     CLICK  equip" : boundContainer != null ? (boundContainer.isStash ? "RMB  store in stash" : "RMB  store in crate") : GameManager.InHub ? "" : "drag outside to drop");
            if (st.count > 1) sb.Append("     SHIFT+DRAG  split");
            sb.Append("</color></size>");
            tooltipFoot.text = sb.ToString();
            tooltipFoot.rectTransform.anchoredPosition = new Vector2(16, y - 4f);
            float h = -y + 4f + tooltipFoot.preferredHeight + 16f;
            tooltip.sizeDelta = new Vector2(470, h);

            Vector2 p = ToLocal(mouse) + new Vector2(24, -24);
            Vector2 half = root.rect.size * 0.5f;
            p.x = Mathf.Min(p.x, half.x - tooltip.sizeDelta.x - 12);
            p.y = Mathf.Max(p.y, -half.y + tooltip.sizeDelta.y + 12);
            tooltip.anchoredPosition = p;
        }

        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        // ------------------------------------------------------------------ test helpers

        /// <summary>Screen position of a slot's centre (used by the autopilot smoke test).</summary>
        public Vector2 SlotScreenPos(ItemContainer c, int index)
        {
            foreach (var s in AllSlots())
                if (s.container == c && s.index == index) return RectTransformUtility.WorldToScreenPoint(UiCam, s.rt.TransformPoint(Vector3.zero));
            return Vector2.zero;
        }

        /// <summary>A screen point outside the window (to drop items).</summary>
        public Vector2 OutsideScreenPos()
        {
            var corners = new Vector3[4];
            backpackPanel.GetWorldCorners(corners);
            Vector2 bl = RectTransformUtility.WorldToScreenPoint(UiCam, corners[0]);
            return new Vector2(bl.x + 100f, Mathf.Max(5f, bl.y - 90f));
        }
    }
}
