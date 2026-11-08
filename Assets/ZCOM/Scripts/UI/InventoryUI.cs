using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Zcom
{
    /// <summary>
    /// Duckov-style inventory window: loadout (weapon slots), backpack grid and an optional container (crate).
    /// LMB drag: move / merge / swap. Shift+drag: split. RMB: quick move. Drag outside the window: drop on the ground.
    /// Click a weapon slot: equip. F: take all from the crate. Tab / Esc: close.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        public static InventoryUI I { get; private set; }

        const float Slot = 96f, Pitch = 104f, PanelH = 620f, Header = 70f;
        static readonly Color PanelBg = UiKit.PanelBg;
        static readonly Color SlotBg = UiKit.SlotBg;
        static readonly Color Cyan = UiKit.Accent;
        static readonly Color Amber = UiKit.Amber;
        static readonly Color Warn = new Color(1f, 0.35f, 0.25f);

        class SlotUi
        {
            public RectTransform rt;
            public Image border, bg, icon, strip, pill;
            public Text count, label, sub, hotkey, pillText;
            public ItemContainer container;
            public int index;
            public bool wide;
        }

        Font font;
        RectTransform root, window;
        GameObject dim;
        RectTransform loadoutPanel, backpackPanel, containerPanel, hintBar, takeAllButton;
        Text containerTitle, backpackCount, vitalsText, grenadeText, carryText, hintText;
        Image takeAllFrame, takeAllFill;
        readonly List<SlotUi> loadoutSlots = new List<SlotUi>();
        readonly List<SlotUi> backpackSlots = new List<SlotUi>();
        readonly List<SlotUi> containerSlots = new List<SlotUi>();
        RawImage operativeView;
        bool rotating;
        Vector2 lastPointer;
        RectTransform tooltip;
        Image tooltipIcon, tooltipIconFrame, tooltipPill;
        Text tooltipName, tooltipPillText, tooltipText;
        RectTransform ghost;
        Image ghostIcon;
        Text ghostCount, dropLabel;

        Inventory inv;
        ItemContainer boundContainer;
        int seenVersion = -1, seenContainerVersion = -1;

        SlotUi dragFrom, pressed;
        int dragAmount;
        Vector2 pressPos;
        bool dragging;

        Camera UiCam => Hud.I && Hud.I.Canvas.renderMode != RenderMode.ScreenSpaceOverlay ? Hud.I.Canvas.worldCamera : null;

        public bool IsOpen => inv && inv.PanelOpen;

        // ------------------------------------------------------------------ build

        public void Init(RectTransform canvasRoot, Font f)
        {
            I = this;
            root = canvasRoot;
            font = f;

            dim = Img("InventoryDim", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0.01f, 0.03f, 0.7f)).gameObject;

            window = new GameObject("InventoryWindow", typeof(RectTransform)).GetComponent<RectTransform>();
            window.SetParent(root, false);
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.sizeDelta = Vector2.zero;

            BuildLoadout();

            // Backpack 6x4
            backpackPanel = Panel("Backpack", new Vector2(6 * Pitch + 36, PanelH), "BACKPACK");
            backpackCount = Txt("Count", backpackPanel, "", 20, UiKit.TextColor, TextAnchor.MiddleRight, new Vector2(-24, -34), new Vector2(160, 30), new Vector2(1, 1));
            backpackCount.rectTransform.pivot = new Vector2(1, 0.5f);
            for (int i = 0; i < 24; i++)
            {
                int cx = i % 6, cy = i / 6;
                backpackSlots.Add(MakeSlot(backpackPanel, TopLeft(18 + cx * Pitch, Header + 8 + cy * Pitch), new Vector2(Slot, Slot), false));
            }
            var cap = Txt("Capacity", backpackPanel, "", 18, UiKit.TextDim, TextAnchor.UpperLeft, new Vector2(24, -(Header + 4 * Pitch + 30)), new Vector2(560, 60), new Vector2(0, 1));
            carryText = cap;
            carryText.supportRichText = true;

            // Container 3x3
            containerPanel = Panel("Container", new Vector2(3 * Pitch + 36, PanelH), "SUPPLY CRATE");
            containerTitle = containerPanel.Find("Title").GetComponent<Text>();
            for (int i = 0; i < 9; i++)
            {
                int cx = i % 3, cy = i / 3;
                containerSlots.Add(MakeSlot(containerPanel, TopLeft(18 + cx * Pitch, Header + 8 + cy * Pitch), new Vector2(Slot, Slot), false));
            }
            takeAllButton = UiKit.Node("TakeAll", containerPanel, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -(Header + 3 * Pitch + 30)), new Vector2(3 * Pitch - 8, 54));
            takeAllFill = UiKit.Fill("Fill", takeAllButton, UiKit.PanelFill, new Color(0.22f, 0.14f, 0.03f, 0.9f));
            takeAllFrame = UiKit.Fill("Frame", takeAllButton, UiKit.PanelFrame, Amber);
            Txt("Label", takeAllButton, $"<color=#{Hex(Amber)}>[F]</color>  {UiKit.Spaced("TAKE ALL")}", 22, UiKit.TextColor, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(300, 40), new Vector2(0.5f, 0.5f)).supportRichText = true;

            // Bottom hint bar
            hintBar = UiKit.Node("HintBar", window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1), new Vector2(0, -PanelH * 0.5f - 22), new Vector2(1400, 50));
            UiKit.Fill("Fill", hintBar, UiKit.PanelFill, PanelBg);
            UiKit.Fill("Frame", hintBar, UiKit.PanelFrame, UiKit.AccentDim);
            string K(string k) => $"<color=#{Hex(Amber)}>[{k}]</color>";
            string sep = "<color=#3c5566>     |     </color>";
            hintText = Txt("Hints", hintBar,
                $"{K("LMB")}  DRAG{sep}{K("SHIFT")}  SPLIT{sep}{K("RMB")}  QUICK MOVE{sep}{K("DRAG OUT")}  DROP{sep}{K("CLICK")}  EQUIP{sep}{K("TAB")}  CLOSE",
                18, UiKit.TextColor, TextAnchor.MiddleCenter, new Vector2(0, 1), new Vector2(1400, 40), new Vector2(0.5f, 0.5f));
            hintText.supportRichText = true;

            BuildTooltip();

            // Drag ghost
            ghost = new GameObject("DragGhost", typeof(RectTransform)).GetComponent<RectTransform>();
            ghost.SetParent(root, false);
            ghost.anchorMin = ghost.anchorMax = new Vector2(0.5f, 0.5f);
            ghost.sizeDelta = new Vector2(Slot, Slot);
            var gf = UiKit.Fill("Frame", ghost, UiKit.PanelFrame, Cyan);
            ghostIcon = Img("Icon", ghost, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-14, -14), Color.white);
            ghostIcon.preserveAspect = true;
            ghostCount = Txt("Count", ghost, "", 18, Color.white, TextAnchor.LowerRight, new Vector2(-6, 2), new Vector2(80, 24), new Vector2(1, 0));
            dropLabel = Txt("Drop", ghost, UiKit.Spaced("DROP"), 18, Warn, TextAnchor.UpperCenter, new Vector2(0, 26), new Vector2(120, 24), new Vector2(0.5f, 1));

            SetVisible(false);
        }

        static Vector2 TopLeft(float x, float y) => new Vector2(x, -y);

        void BuildLoadout()
        {
            loadoutPanel = Panel("Loadout", new Vector2(560, PanelH), "LOADOUT");
            // Operative render on a lit pad
            UiKit.Img("PadGlow", loadoutPanel, HoloKit.SoftDot, new Color(Cyan.r, Cyan.g, Cyan.b, 0.12f), new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(150, -490), new Vector2(300, 70));
            UiKit.Img("Pad", loadoutPanel, UiKit.Ring, new Color(Cyan.r, Cyan.g, Cyan.b, 0.3f), new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(150, -488), new Vector2(200, 34));
            // live 3D operative (the in-game model on the PreviewStage), drag to rotate
            var stage = PreviewStage.Ensure();
            var go = new GameObject("Operative", typeof(RectTransform));
            go.transform.SetParent(loadoutPanel, false);
            operativeView = go.AddComponent<RawImage>();
            operativeView.texture = stage.Body;
            operativeView.raycastTarget = false;
            var ort = operativeView.rectTransform;
            ort.anchorMin = ort.anchorMax = new Vector2(0, 1);
            ort.pivot = new Vector2(0.5f, 1);
            ort.anchoredPosition = new Vector2(150, -Header + 6);
            ort.sizeDelta = new Vector2(292, 438);
            Txt("RotateHint", loadoutPanel, "DRAG TO ROTATE", 12, new Color(1, 1, 1, 0.35f), TextAnchor.MiddleCenter, new Vector2(150, -(PanelH - 104)), new Vector2(260, 16), new Vector2(0, 1)).rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Txt("Class", loadoutPanel, $"<color=#{Hex(Cyan)}>{UiKit.Spaced("ORBITAL MARSHAL")}</color>\n<size=14><color=#{Hex(UiKit.TextDim)}>VANGUARD  ·  SHIELD  ·  SCAN PULSE</color></size>", 18, UiKit.TextColor, TextAnchor.UpperCenter,
                new Vector2(150, -(PanelH - 92)), new Vector2(290, 60), new Vector2(0, 1)).supportRichText = true;
            ((RectTransform)loadoutPanel.Find("Class")).pivot = new Vector2(0.5f, 1);

            for (int i = 0; i < 3; i++)
                loadoutSlots.Add(MakeSlot(loadoutPanel, TopLeft(306, Header + 6 + i * 124), new Vector2(236, 114), true));

            vitalsText = StatBox("Vitals", TopLeft(306, Header + 6 + 3 * 124), "icon_shield", "VITALS");
            grenadeText = StatBox("Grenades", TopLeft(306, Header + 6 + 3 * 124 + 70), "icon_grenade", "GRENADES");
        }

        Text StatBox(string name, Vector2 pos, string icon, string title)
        {
            var b = UiKit.Node(name, loadoutPanel, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(236, 62));
            UiKit.Fill("Fill", b, UiKit.PanelFill, SlotBg);
            UiKit.Fill("Frame", b, UiKit.PanelFrame, new Color(1, 1, 1, 0.14f));
            UiKit.Img("Icon", b, UiKit.Art(icon), UiKit.TextColor, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(38, 38)).preserveAspect = true;
            Txt("Title", b, UiKit.Spaced(title), 15, UiKit.TextDim, TextAnchor.UpperLeft, new Vector2(64, -8), new Vector2(160, 18), new Vector2(0, 1));
            var v = Txt("Value", b, "", 24, UiKit.TextColor, TextAnchor.UpperLeft, new Vector2(64, -26), new Vector2(160, 30), new Vector2(0, 1));
            v.supportRichText = true;
            return v;
        }

        void BuildTooltip()
        {
            var tip = Img("Tooltip", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460, 170), new Color(0.012f, 0.03f, 0.05f, 0.97f));
            tip.sprite = UiKit.PanelFill; tip.type = Image.Type.Sliced;
            UiKit.Fill("Frame", tip.rectTransform, UiKit.PanelFrame, new Color(Cyan.r, Cyan.g, Cyan.b, 0.75f));
            tooltip = tip.rectTransform;
            tooltip.pivot = new Vector2(0, 1);
            var iconBox = UiKit.Node("IconBox", tooltip, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -14), new Vector2(110, 110));
            UiKit.Fill("Fill", iconBox, UiKit.PanelFill, SlotBg);
            tooltipIconFrame = UiKit.Fill("Frame", iconBox, UiKit.PanelFrame, Cyan);
            tooltipIcon = Img("Icon", iconBox, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-16, -16), Color.white);
            tooltipIcon.preserveAspect = true;
            tooltipName = Txt("Name", tooltip, "", 26, UiKit.TextColor, TextAnchor.UpperLeft, new Vector2(140, -10), new Vector2(300, 30), new Vector2(0, 1));
            var pill = UiKit.Node("Pill", tooltip, new Vector2(0, 1), new Vector2(0, 1), new Vector2(140, -44), new Vector2(96, 20));
            tooltipPill = UiKit.Fill("Fill", pill, UiKit.Skew, Cyan);
            tooltipPillText = Txt("Text", pill, "", 14, new Color(0.02f, 0.04f, 0.06f), TextAnchor.MiddleCenter, new Vector2(0, 1), new Vector2(120, 20), new Vector2(0.5f, 0.5f));
            tooltipPillText.GetComponent<Outline>().enabled = false;
            tooltipText = Txt("Text", tooltip, "", 16, Color.white, TextAnchor.UpperLeft, new Vector2(140, -74), new Vector2(304, 300), new Vector2(0, 1));
            tooltipText.supportRichText = true;
            tooltipText.verticalOverflow = VerticalWrapMode.Overflow;
            tooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        RectTransform Panel(string name, Vector2 size, string title)
        {
            var p = UiKit.Panel(name, window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            UiKit.Img("Mark", p, UiKit.Diamond, Amber, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(32, -34), new Vector2(16, 16));
            var t = Txt("Title", p, UiKit.Spaced(title), 30, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(52, -34), new Vector2(size.x - 220, 36), new Vector2(0, 1));
            t.rectTransform.pivot = new Vector2(0, 0.5f);
            t.name = "Title";
            UiKit.Img("TitleLine", p, UiKit.FadeLine, new Color(1, 1, 1, 0.1f), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(20, -62), new Vector2(size.x - 40, 2));
            UiKit.Img("TitleAccent", p, UiKit.White, Amber, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(20, -62), new Vector2(110, 3));
            return p;
        }

        SlotUi MakeSlot(RectTransform parent, Vector2 pos, Vector2 size, bool wide)
        {
            var s = new SlotUi { wide = wide };
            s.border = Img("Slot", parent, new Vector2(0, 1), new Vector2(0, 1), pos, size, new Color(1, 1, 1, 0.12f));
            s.rt = s.border.rectTransform;
            s.rt.pivot = new Vector2(0, 1);
            s.border.sprite = UiKit.PanelFill; s.border.type = Image.Type.Sliced;
            s.bg = Img("BG", s.rt, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-4, -4), SlotBg);
            s.bg.sprite = UiKit.PanelFill; s.bg.type = Image.Type.Sliced;
            s.strip = Img("Rarity", s.rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(2, -2), wide ? new Vector2(4, 34) : new Vector2(3, 22), Color.clear);
            s.strip.rectTransform.pivot = new Vector2(0, 1);
            if (wide)
            {
                s.label = Txt("Name", s.rt, "", 17, Color.white, TextAnchor.UpperLeft, new Vector2(14, -8), new Vector2(170, 22), new Vector2(0, 1));
                s.icon = Img("Icon", s.rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -4), new Vector2(190, 62), Color.white);
                s.hotkey = Txt("Key", s.rt, "", 15, UiKit.TextDim, TextAnchor.UpperRight, new Vector2(-10, -8), new Vector2(40, 20), new Vector2(1, 1));
                s.sub = Txt("Sub", s.rt, "", 15, UiKit.TextDim, TextAnchor.LowerRight, new Vector2(-10, 6), new Vector2(120, 20), new Vector2(1, 0));
                s.sub.supportRichText = true;
                var pill = UiKit.Node("Pill", s.rt, new Vector2(0, 0), new Vector2(0, 0), new Vector2(8, 7), new Vector2(108, 20));
                s.pill = UiKit.Fill("Fill", pill, UiKit.Skew, Amber);
                s.pillText = Txt("Text", pill, UiKit.Spaced("EQUIPPED"), 14, new Color(0.08f, 0.05f, 0.01f), TextAnchor.MiddleCenter, new Vector2(0, 1), new Vector2(120, 20), new Vector2(0.5f, 0.5f));
                s.pillText.GetComponent<Outline>().enabled = false;
            }
            else
            {
                s.icon = Img("Icon", s.rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(66, 66), Color.white);
                s.label = Txt("Name", s.rt, "", 14, UiKit.TextColor, TextAnchor.UpperLeft, new Vector2(8, -5), new Vector2(84, 16), new Vector2(0, 1));
            }
            s.icon.preserveAspect = true;
            s.count = Txt("Count", s.rt, "", 17, Color.white, TextAnchor.LowerRight, new Vector2(-7, 4), new Vector2(70, 24), new Vector2(1, 0));
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
            var o = go.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, 0.7f);
            o.effectDistance = new Vector2(1.2f, -1.2f);
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
            if (window.gameObject.activeSelf != open)
            {
                SetVisible(open);
                if (open) { seenVersion = -1; seenContainerVersion = -1; }
                else CancelDrag();
                dim.transform.SetAsLastSibling();
                window.SetAsLastSibling();
                tooltip.SetAsLastSibling();
                ghost.SetAsLastSibling();
            }
            if (PreviewStage.I) PreviewStage.I.BodyViewActive = open;
            if (!open) return;

            Layout();
            Bind();
            Refresh();
            HandleMouse();
        }

        void Layout()
        {
            bool hasContainer = inv.OpenContainer != null;
            containerPanel.gameObject.SetActive(hasContainer);
            float bpW = backpackPanel.sizeDelta.x, loW = loadoutPanel.sizeDelta.x, coW = containerPanel.sizeDelta.x;
            float total = loW + 18 + bpW + (hasContainer ? 18 + coW : 0);
            float x = -total * 0.5f, y = 30f;
            loadoutPanel.anchoredPosition = new Vector2(x + loW * 0.5f, y);
            x += loW + 18;
            backpackPanel.anchoredPosition = new Vector2(x + bpW * 0.5f, y);
            x += bpW + 18;
            containerPanel.anchoredPosition = new Vector2(x + coW * 0.5f, y);
            hintBar.sizeDelta = new Vector2(total, 50);
            hintBar.anchoredPosition = new Vector2(0, y - PanelH * 0.5f - 16);
            if (hasContainer) containerTitle.text = UiKit.Spaced(inv.OpenContainerName ?? "CONTAINER");
            if (hasContainer)
            {
                bool any = false;
                for (int i = 0; i < inv.OpenContainer.Size; i++) if (!inv.OpenContainer[i].IsEmpty) { any = true; break; }
                float p = any ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f) : 0.25f;
                takeAllFrame.color = new Color(Amber.r, Amber.g, Amber.b, p);
                takeAllFill.color = any ? new Color(0.22f, 0.14f, 0.03f, 0.9f) : new Color(0.05f, 0.05f, 0.05f, 0.6f);
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
            if (boundContainer != inv.OpenContainer) { boundContainer = inv.OpenContainer; seenContainerVersion = -1; }
            for (int i = 0; i < containerSlots.Count; i++)
            {
                bool on = boundContainer != null && i < boundContainer.Size;
                containerSlots[i].container = on ? boundContainer : null;
                containerSlots[i].index = i;
                containerSlots[i].rt.gameObject.SetActive(on);
            }
        }

        void Refresh()
        {
            int v = inv.Version;
            int cv = boundContainer != null ? boundContainer.Version : 0;
            var ph = PlayerController.I;
            var hp = ph ? ph.health : null;
            var gr = ph ? ph.grenades : null;
            vitalsText.text = $"<color=#{Hex(Amber)}>/</color>  {(hp ? Mathf.CeilToInt(hp.Health) : 0)}<size=17><color=#{Hex(UiKit.TextDim)}> / {(hp ? Mathf.RoundToInt(hp.maxHealth) : 0)}</color></size>";
            grenadeText.text = $"<color=#{Hex(Amber)}>/</color>  {(gr ? gr.Charges : 0)}<size=17><color=#{Hex(UiKit.TextDim)}> / {(gr ? gr.maxCharges : 0)}</color></size>";
            if (v == seenVersion && cv == seenContainerVersion) return;
            seenVersion = v;
            seenContainerVersion = cv;
            foreach (var s in loadoutSlots) Draw(s);
            foreach (var s in backpackSlots) if (s.container != null) Draw(s);
            foreach (var s in containerSlots) if (s.container != null) Draw(s);

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
            backpackCount.text = $"<color=#{Hex(Amber)}>ITEMS</color>  {used} <color=#{Hex(UiKit.TextDim)}>/ {inv.Backpack.Size}</color>";
            backpackCount.supportRichText = true;
            carryText.text = $"CARRY  <color=#{Hex(UiKit.TextColor)}>{kg:0.0} kg</color>          BACKPACK VALUE  <color=#{Hex(Amber)}>{value:N0} cr</color>";
        }

        void Draw(SlotUi s)
        {
            var st = s.container[s.index];
            bool equipped = s.container == inv.WeaponSlots && s.index == inv.EquippedIndex && !st.IsEmpty;
            if (s.hotkey) s.hotkey.text = $"[{s.index + 1}]";
            if (s.pill) s.pill.transform.parent.gameObject.SetActive(equipped);
            if (st.IsEmpty)
            {
                s.icon.enabled = false;
                s.count.text = "";
                s.strip.color = Color.clear;
                s.border.color = new Color(1, 1, 1, 0.1f);
                s.label.text = s.wide ? UiKit.Spaced("EMPTY SLOT") : "";
                s.label.color = new Color(1, 1, 1, 0.25f);
                if (s.sub) s.sub.text = "";
                return;
            }
            var d = st.Def;
            s.icon.enabled = true;
            s.icon.sprite = ItemIcons.Get(st.id);
            s.count.text = st.count > 1 ? $"x{st.count}" : "";
            s.strip.color = d.rarityColor;
            s.border.color = equipped ? Amber : new Color(d.rarityColor.r, d.rarityColor.g, d.rarityColor.b, 0.45f);
            s.label.text = d.name;
            s.label.color = s.wide ? UiKit.TextColor : new Color(0.85f, 0.9f, 0.95f, 0.85f);
            if (s.wide)
            {
                var w = WeaponDefs.Get(d.weapon);
                s.sub.text = w.beam ? $"{w.Dps:0} DPS BEAM" : w.pellets > 1 ? $"{w.damage:0}x{w.pellets} DMG" : $"{w.damage:0} DMG";
            }
        }

        // ------------------------------------------------------------------ mouse

        IEnumerable<SlotUi> AllSlots()
        {
            foreach (var s in loadoutSlots) yield return s;
            foreach (var s in backpackSlots) if (s.container != null) yield return s;
            foreach (var s in containerSlots) if (s.container != null) yield return s;
        }

        SlotUi SlotAt(Vector2 screen)
        {
            foreach (var s in AllSlots())
                if (s.rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(s.rt, screen, UiCam)) return s;
            return null;
        }

        bool OverWindow(Vector2 screen)
        {
            foreach (var p in new[] { loadoutPanel, backpackPanel, containerPanel })
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
                s.bg.color = h ? new Color(0.07f, 0.16f, 0.2f, 1f) : SlotBg;
            }

            if (input.click && hover == null && boundContainer != null && takeAllButton.gameObject.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(takeAllButton, mouse, UiCam))
            {
                inv.TakeAll();
                ProcAudio.Play("equip", 0.4f, 0.05f, 1.15f);
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
                ProcAudio.Play("equip", 0.25f, 0.05f, 1.4f);
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

        void ClickSlot(SlotUi s)
        {
            if (s.container == inv.WeaponSlots && !s.container[s.index].IsEmpty) inv.Equip(s.index);
        }

        void EndDrag(SlotUi target, Vector2 mouse)
        {
            var from = dragFrom;
            if (from == null || from.container[from.index].IsEmpty) return;
            if (target != null)
            {
                if (target == from) return;
                bool ok = ItemContainer.Move(from.container, from.index, target.container, target.index, dragAmount);
                ProcAudio.Play(ok ? "equip" : "empty", 0.4f, 0.05f, ok ? 1.15f : 1f);
                if (ok && target.container == inv.WeaponSlots && from.container != inv.WeaponSlots && inv.EquippedIndex == target.index) inv.Equip(target.index);
            }
            else if (!OverWindow(mouse))
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
                if (Items.IsWeapon(st.id))
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
            ProcAudio.Play(ok ? "equip" : "empty", 0.4f, 0.05f, ok ? 1.15f : 1f);
        }

        void CancelDrag()
        {
            dragging = false;
            dragFrom = null;
            pressed = null;
            if (ghost) ghost.gameObject.SetActive(false);
        }

        void UpdateGhost(Vector2 mouse)
        {
            bool show = dragging && dragFrom != null && !dragFrom.container[dragFrom.index].IsEmpty;
            ghost.gameObject.SetActive(show);
            if (!show) return;
            var st = dragFrom.container[dragFrom.index];
            ghostIcon.sprite = ItemIcons.Get(st.id);
            int n = dragAmount < 0 ? st.count : Mathf.Min(dragAmount, st.count);
            ghostCount.text = n > 1 ? n.ToString() : "";
            bool outside = !OverWindow(mouse);
            dropLabel.enabled = outside;
            ghostIcon.color = outside ? new Color(1f, 0.6f, 0.55f, 0.85f) : new Color(1, 1, 1, 0.9f);
            ghost.anchoredPosition = ToLocal(mouse);
            ghost.localScale = Vector3.one * 1.1f;
        }

        void UpdateTooltip(SlotUi hover, Vector2 mouse)
        {
            bool show = hover != null && !dragging && !hover.container[hover.index].IsEmpty;
            tooltip.gameObject.SetActive(show);
            if (!show) return;
            var st = hover.container[hover.index];
            var d = st.Def;
            tooltipIcon.sprite = ItemIcons.Get(st.id);
            tooltipIconFrame.color = d.rarityColor;
            tooltipName.text = UiKit.Spaced(d.name);
            tooltipPill.color = d.rarityColor;
            tooltipPillText.text = UiKit.Spaced(d.rarity);
            ((RectTransform)tooltipPill.transform.parent).sizeDelta = new Vector2(tooltipPillText.preferredWidth + 26, 20);
            tooltipPillText.rectTransform.sizeDelta = new Vector2(tooltipPillText.preferredWidth + 26, 20);

            var sb = new System.Text.StringBuilder();
            sb.Append($"<color=#b8c4cf>{d.description}</color>\n");
            if (d.category == ItemCategory.Weapon)
            {
                var w = WeaponDefs.Get(d.weapon);
                sb.Append("\n");
                if (w.beam) sb.Append($"DPS  <b>{w.Dps:0}</b>     BEAM TIME  <b>{w.ShotsPerCell:0.0}s</b>\nPIERCE  <b>{w.pierce + 1} targets</b>     RANGE  <b>{w.range:0}m</b>\n");
                else
                {
                    string dmg = w.pellets > 1 ? $"{w.damage:0} x {w.pellets}" : $"{w.damage:0}";
                    sb.Append($"DAMAGE  <b>{dmg}</b>     RATE  <b>{w.fireRate:0.#}/s</b>\nCELL  <b>{w.cellSize}</b>     DPS  <b>{w.Dps:0}</b>     RANGE  <b>{w.range:0}m</b>\n");
                }
            }
            sb.Append("\n");
            string dimHex = Hex(UiKit.TextDim);
            sb.Append($"<color=#{dimHex}>WEIGHT</color>  <b>{d.weight * st.count:0.0} kg</b>     <color=#{dimHex}>VALUE</color>  <color=#{Hex(Amber)}><b>{d.value * st.count:N0}</b></color>");
            if (st.count > 1) sb.Append($"     <color=#{dimHex}>STACK</color>  <b>{st.count}/{d.maxStack}</b>");
            sb.Append("\n<size=13><color=#6f8090>");
            sb.Append(hover.container == boundContainer ? "RMB  take" : d.category == ItemCategory.Weapon && hover.container == inv.Backpack ? "RMB  equip to loadout" :
                hover.container == inv.WeaponSlots ? "RMB  move to backpack     CLICK  equip" : boundContainer != null ? "RMB  store in crate" : "drag outside to drop");
            if (st.count > 1) sb.Append("     SHIFT+DRAG  split");
            sb.Append("</color></size>");
            tooltipText.text = sb.ToString();
            tooltip.sizeDelta = new Vector2(460, Mathf.Max(140f, 84f + tooltipText.preferredHeight));

            Vector2 p = ToLocal(mouse) + new Vector2(22, -22);
            Vector2 half = root.rect.size * 0.5f;
            p.x = Mathf.Min(p.x, half.x - tooltip.sizeDelta.x - 8);
            p.y = Mathf.Max(p.y, -half.y + tooltip.sizeDelta.y + 8);
            tooltip.anchoredPosition = p;
        }

        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        // ------------------------------------------------------------------ test helpers

        /// <summary>Screen position of a slot's centre (used by the autopilot smoke test).</summary>
        public Vector2 SlotScreenPos(ItemContainer c, int index)
        {
            foreach (var s in AllSlots())
                if (s.container == c && s.index == index) return RectTransformUtility.WorldToScreenPoint(UiCam, s.rt.position);
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
