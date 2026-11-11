using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Zcom
{
    /// <summary>
    /// Forward base between raids (opens from the end-of-raid summary): the loadout you will deploy with, the stash of
    /// everything you extracted, and the quartermaster - sell loot, buy weapons, buy suit upgrades. DEPLOY starts the
    /// next raid. LMB a weapon: equip / unequip. RMB a stash item: sell it. Progress lives in <see cref="Profile"/>.
    /// </summary>
    public class BaseUI : MonoBehaviour
    {
        public static BaseUI I { get; private set; }
        public bool IsOpen => rootGo && rootGo.activeSelf;

        static Color Cyan => TacKit.Cyan;
        static Color Amber => UiKit.Amber;
        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);
        const float Slot = 92f, Pitch = 100f, PanelH = 640f;

        class SlotUi { public RectTransform rt; public Image bg, frame, glow, icon; public Text label, count, hotkey; public ItemContainer c; public int i; public bool wide; }
        class Button { public RectTransform rt; public Image fill, frame; public Text label, price; public System.Action onClick; public System.Func<bool> enabled; public Color accent; }

        RectTransform root;
        GameObject rootGo;
        CanvasGroup group;
        RectTransform window;
        readonly List<SlotUi> loadoutSlots = new List<SlotUi>(), stashSlots = new List<SlotUi>();
        readonly List<Button> buttons = new List<Button>();
        readonly List<(Image[] pips, Text price, Profile.Upgrade u)> upgradeRows = new List<(Image[], Text, Profile.Upgrade)>();
        readonly List<(Text price, WeaponKind k)> weaponRows = new List<(Text, WeaponKind)>();
        Text creditsText, statsText, lastRaidText, statusText, suitText, hintText;
        Button deployButton;
        float openT, statusT, shownCredits;
        int seenVersion = -1;
        object lastHover;

        Camera UiCam => Hud.I && Hud.I.Canvas.renderMode != RenderMode.ScreenSpaceOverlay ? Hud.I.Canvas.worldCamera : null;

        // ------------------------------------------------------------------ build

        public void Init(RectTransform canvasRoot)
        {
            I = this;
            var r = UiKit.Stretch("ForwardBase", canvasRoot);
            root = r;
            rootGo = r.gameObject;
            group = rootGo.AddComponent<CanvasGroup>();
            UiKit.Fill("Backdrop", r, UiKit.White, new Color(0.006f, 0.014f, 0.024f, 1f));
            UiKit.Img("Haze", r, HoloKit.SoftDot, A(Cyan, 0.06f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(2200, 1100));
            var hex = HoloKit.Tiled("Hex", r, HoloKit.HexTile, A(Cyan, 0.03f));
            hex.uvRect = new Rect(0, 0, 1920f / 52f, 1080f / 90f);
            UiKit.Fill("Vignette", r, ProcTex.VignetteSprite, new Color(0f, 0f, 0f, 0.6f));
            UiKit.Fill("VisorFrame", r, HoloKit.Visor, A(Cyan, 0.08f), 6f);

            window = UiKit.Node("Window", r, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), Vector2.zero);
            const float loW = 400f, stW = 6 * Pitch + 44f, qmW = 520f, gap = 20f;
            float total = loW + stW + qmW + gap * 2f, x = -total * 0.5f, y = 0f;

            // header
            var head = UiKit.Node("Header", window, new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(x + 4, y + PanelH * 0.5f + 62), new Vector2(total, 70));
            UiKit.Img("Bar", head, UiKit.White, Cyan, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(6, 48));
            var title = Lbl("Title", head, "FORWARD BASE", 52, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(24, 2), TacKit.Num);
            Lbl("Sub", head, "ORBITAL STATION  KEPLER-7   //   BETWEEN RAIDS", 18, new Color(0.75f, 0.83f, 0.9f, 0.85f), TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(24 + title.preferredWidth + 30, 0), TacKit.Cap);
            var cico = UiKit.Art("a_icon_credits");
            creditsText = Lbl("Credits", head, "", 44, Amber, TextAnchor.MiddleRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8, 8), TacKit.Num);
            if (cico) UiKit.Img("CrIcon", head, cico, Amber, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-250, 8), new Vector2(34, 34));
            statsText = Lbl("Stats", head, "", 14, TacKit.Micro, TextAnchor.MiddleRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8, -26), TacKit.Cap);
            lastRaidText = Lbl("LastRaid", head, "", 17, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, -36), TacKit.Cap);
            lastRaidText.supportRichText = true;

            // loadout
            var lo = Panel("Loadout", "LOADOUT", "DEPLOYS WITH YOU", new Vector2(x + loW * 0.5f, y), new Vector2(loW, PanelH), Cyan);
            for (int i = 0; i < Profile.LoadoutSize; i++)
                loadoutSlots.Add(MakeSlot(lo, new Vector2(loW * 0.5f, -(134 + i * 124)), new Vector2(loW - 44, 112), true));
            UiKit.Img("SuitRule", lo, UiKit.FadeLine, A(Cyan, 0.35f), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(22, -520), new Vector2(loW - 44, 1.5f));
            TacKit.MicroText("SuitLbl", lo, "SUIT  //  WITH UPGRADES", new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(22, -538), TextAnchor.MiddleLeft, 12);
            suitText = Lbl("Suit", lo, "", 20, UiKit.TextColor, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -556), TacKit.Cap);
            suitText.supportRichText = true;
            suitText.lineSpacing = 1.15f;
            x += loW + gap;

            // stash
            var st = Panel("Stash", "STASH", "SECURED ON STATION", new Vector2(x + stW * 0.5f, y), new Vector2(stW, PanelH), Cyan);
            for (int i = 0; i < Profile.StashSize; i++)
            {
                int cx = i % 6, cy = i / 6;
                stashSlots.Add(MakeSlot(st, new Vector2(22 + cx * Pitch + Slot * 0.5f, -(80 + cy * Pitch + Slot * 0.5f)), new Vector2(Slot, Slot), false));
            }
            var sell = MakeButton(st, "SELL ALL RESOURCES", new Vector2(stW * 0.5f, -(80 + 5 * Pitch + 26)), new Vector2(stW - 44, 50), Amber, SellAll);
            sell.enabled = () => ResourceValue() > 0;
            x += stW + gap;

            // quartermaster
            var qm = Panel("Quartermaster", "QUARTERMASTER", "TRADE  //  REFIT", new Vector2(x + qmW * 0.5f, y), new Vector2(qmW, PanelH), Amber);
            TacKit.MicroText("WLbl", qm, "WEAPONS", new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(24, -76), TextAnchor.MiddleLeft, 13);
            int row = 0;
            foreach (WeaponKind k in System.Enum.GetValues(typeof(WeaponKind)))
            {
                var kk = k;
                var def = WeaponDefs.Get(k);
                var art = UiKit.Art(Items.Get(Items.ForWeapon(k)).icon);
                var box = UiKit.Node("W" + row, qm, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -92 - row * 78), new Vector2(qmW - 44, 70));
                UiKit.Fill("Bg", box, TacKit.Slot, new Color(0.025f, 0.055f, 0.08f, 0.9f));
                UiKit.Fill("Edge", box, TacKit.SlotGlow, A(def.rarityColor, 0.18f));
                if (art) UiKit.Img("Art", box, art, Color.white, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(80, 0), new Vector2(140, 52)).preserveAspect = true;
                Lbl("Name", box, def.displayName, 20, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(166, 10), TacKit.Cap);
                Lbl("Rarity", box, def.rarity, 12, def.rarityColor, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(167, -14), TacKit.Cap);
                var b = MakeButton(box, "BUY", new Vector2(qmW - 44 - 70, 0), new Vector2(120, 44), Amber, () => Buy(kk));
                b.rt.anchorMin = b.rt.anchorMax = new Vector2(0, 0.5f);
                b.enabled = () => Profile.Credits >= Profile.BuyPrice(kk);
                weaponRows.Add((b.price, kk));
                row++;
            }
            float uy = -92 - row * 78 - 22;
            TacKit.MicroText("ULbl", qm, "SUIT UPGRADES", new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(24, uy), TextAnchor.MiddleLeft, 13);
            int urow = 0;
            foreach (var u in Profile.Upgrades)
            {
                var uu = u;
                float ry = uy - 16 - urow * 70;
                var box = UiKit.Node("U" + urow, qm, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, ry), new Vector2(qmW - 44, 62));
                box.pivot = new Vector2(0, 1);
                UiKit.Fill("Bg", box, TacKit.Slot, new Color(0.025f, 0.055f, 0.08f, 0.9f));
                Lbl("Name", box, u.name, 19, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 10), TacKit.Cap);
                Lbl("Desc", box, u.desc, 13, new Color(0.72f, 0.8f, 0.86f, 0.9f), TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, -13), TacKit.Cap);
                var pips = new Image[u.MaxLevel];
                for (int i = 0; i < pips.Length; i++)
                    pips[i] = UiKit.Img("Pip" + i, box, UiKit.Skew, Cyan, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(236 + i * 22, 0), new Vector2(18, 10));
                var b = MakeButton(box, "BUY", new Vector2(qmW - 44 - 70, 0), new Vector2(120, 44), Cyan, () => BuyUpgrade(uu));
                b.rt.anchorMin = b.rt.anchorMax = new Vector2(0, 0.5f);
                b.enabled = () => { int p = Profile.UpgradePrice(uu); return p >= 0 && Profile.Credits >= p; };
                upgradeRows.Add((pips, b.price, uu));
                urow++;
            }

            // deploy + status line
            deployButton = MakeButton(window, "DEPLOY TO KEPLER-7", new Vector2(0, -PanelH * 0.5f - 52), new Vector2(520, 64), Cyan, Deploy);
            deployButton.rt.anchorMin = deployButton.rt.anchorMax = new Vector2(0.5f, 0.5f);
            deployButton.label.fontSize = 28;
            deployButton.price.text = "R";
            statusText = Lbl("Status", window, "", 18, Amber, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -PanelH * 0.5f - 14), TacKit.Cap);
            hintText = Lbl("Hint", window, "LMB  WEAPON: EQUIP / UNEQUIP      RMB  STASH ITEM: SELL      R  DEPLOY", 14, TacKit.Micro, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -PanelH * 0.5f - 104), TacKit.Cap);

            rootGo.SetActive(false);
        }

        Text Lbl(string name, Transform parent, string text, int size, Color c, TextAnchor align, Vector2 anchor, Vector2 pivot, Vector2 pos, Font font)
        {
            var t = UiKit.Label(name, parent, text, size, c, align, anchor, pivot, pos, new Vector2(700, size + 12), true);
            t.font = font;
            return t;
        }

        RectTransform Panel(string name, string title, string micro, Vector2 pos, Vector2 size, Color accent)
        {
            var p = TacKit.GlassPlate(name, window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size, accent, 0.32f);
            var hx = HoloKit.Tiled("Hex", p, HoloKit.HexTile, A(accent, 0.03f));
            hx.uvRect = new Rect(0, 0, size.x / 52f, size.y / 90f);
            TacKit.Header(p, title, micro, accent, size.x);
            return p;
        }

        SlotUi MakeSlot(RectTransform parent, Vector2 center, Vector2 size, bool wide)
        {
            var s = new SlotUi { wide = wide };
            s.rt = UiKit.Node("Slot", parent, new Vector2(wide ? 0.5f : 0f, 1), new Vector2(0.5f, 0.5f), wide ? new Vector2(0, center.y) : center, size);
            s.bg = UiKit.Fill("Bg", s.rt, TacKit.Slot, new Color(0.025f, 0.055f, 0.08f, 0.92f));
            var h = HoloKit.Tiled("Hatch", s.rt, TacKit.Hatch, new Color(1, 1, 1, 0.025f));
            h.uvRect = new Rect(0, 0, size.x / 16f, size.y / 16f);
            s.glow = UiKit.Fill("Glow", s.rt, TacKit.SlotGlow, Color.clear);
            s.frame = UiKit.Fill("Frame", s.rt, TacKit.SlotFrame, new Color(1, 1, 1, 0.12f));
            s.icon = UiKit.Img("Icon", s.rt, null, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, wide ? -4 : -4), wide ? new Vector2(260, 70) : new Vector2(64, 64));
            s.icon.preserveAspect = true;
            s.label = UiKit.Label("Name", s.rt, "", wide ? 17 : 12, UiKit.TextColor, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), wide ? new Vector2(14, -8) : new Vector2(8, -6), new Vector2(240, 20), true);
            s.label.font = TacKit.Cap;
            s.count = UiKit.Label("Count", s.rt, "", 16, Color.white, TextAnchor.LowerRight, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-8, 4), new Vector2(80, 22), true);
            s.count.font = TacKit.Num;
            if (wide)
            {
                s.hotkey = UiKit.Label("Key", s.rt, "", 15, UiKit.TextDim, TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -8), new Vector2(40, 20), true);
                s.hotkey.font = TacKit.Num;
            }
            return s;
        }

        Button MakeButton(RectTransform parent, string label, Vector2 pos, Vector2 size, Color accent, System.Action onClick)
        {
            var b = new Button { onClick = onClick, accent = accent };
            b.rt = UiKit.Node("Button", parent, new Vector2(0, 1), new Vector2(0.5f, 0.5f), pos, size);
            b.fill = UiKit.Fill("Fill", b.rt, TacKit.Slot, A(accent, 0.18f));
            b.frame = UiKit.Fill("Frame", b.rt, TacKit.SlotFrame, A(accent, 0.9f));
            b.label = UiKit.Label("Label", b.rt, label, 20, UiKit.TextColor, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 7), new Vector2(size.x, 26), true);
            b.label.font = TacKit.Num;
            b.price = UiKit.Label("Price", b.rt, "", 14, accent, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -12), new Vector2(size.x, 18), true);
            b.price.font = TacKit.Cap;
            buttons.Add(b);
            return b;
        }

        // ------------------------------------------------------------------ open / actions

        public void Open()
        {
            if (IsOpen) return;
            rootGo.SetActive(true);
            root.SetAsLastSibling();
            openT = 0f;
            seenVersion = -1;
            shownCredits = Profile.Credits;
            group.alpha = 0f;
            Cursor.visible = true;
            ProcAudio.Play("ui_open", 0.5f, 0f);
            if (RaidUI.I) RaidUI.I.HideSummary();
            int res = Profile.LastResult;
            lastRaidText.text = res == 1 ? $"LAST RAID   <color=#{UiKit.Hex(Cyan)}>EXTRACTED</color>   loot worth <color=#{UiKit.Hex(Amber)}>{Profile.LastValue:N0} cr</color> moved to the stash"
                : res == 2 ? $"LAST RAID   <color=#{UiKit.Hex(UiKit.Danger)}>KIA</color>   loadout and backpack lost (<color=#{UiKit.Hex(UiKit.Danger)}>{Profile.LastValue:N0} cr</color>) - a standard-issue carbine is waiting"
                : "";
        }

        void Status(string s, bool good)
        {
            statusText.text = s;
            statusText.color = good ? Amber : UiKit.Danger;
            statusT = 2.5f;
        }

        void SellAll()
        {
            int v = Profile.SellAllResources();
            if (v > 0) { ProcAudio.Play("pickup_res", 0.6f, 0f, 1.1f); Status($"SOLD RESOURCES  +{v:N0} CR", true); }
        }

        void Buy(WeaponKind k)
        {
            if (Profile.BuyWeapon(k)) { ProcAudio.Play("ui_equip", 0.6f, 0f); Status($"{WeaponDefs.Get(k).displayName} ADDED TO THE STASH", true); }
            else { ProcAudio.Play("ui_deny", 0.5f, 0f); Status(Profile.Stash.FirstEmpty() < 0 ? "STASH FULL" : "NOT ENOUGH CREDITS", false); }
        }

        void BuyUpgrade(Profile.Upgrade u)
        {
            if (Profile.BuyUpgrade(u)) { ProcAudio.Play("ready", 0.5f, 0f, 1.1f); Status($"{u.name} LEVEL {Profile.Level(u.id)} INSTALLED", true); }
            else { ProcAudio.Play("ui_deny", 0.5f, 0f); Status(Profile.UpgradePrice(u) < 0 ? "ALREADY AT MAX LEVEL" : "NOT ENOUGH CREDITS", false); }
        }

        void Deploy()
        {
            ProcAudio.Play("ready", 0.6f, 0f, 0.9f);
            if (GameManager.I) GameManager.I.Restart();
        }

        int ResourceValue()
        {
            int v = 0;
            foreach (var s in Profile.Stash.slots) if (!s.IsEmpty && !Items.IsWeapon(s.id)) v += Profile.SellPrice(s);
            return v;
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            if (!IsOpen) return;
            float dt = Time.unscaledDeltaTime;
            openT += dt;
            group.alpha = Mathf.Clamp01(openT / 0.25f);
            window.anchoredPosition = new Vector2(0, -10 - (1f - Ease.OutCubic(Mathf.Clamp01(openT / 0.35f))) * 30f);
            Cursor.visible = true;

            for (int i = 0; i < loadoutSlots.Count; i++) { loadoutSlots[i].c = Profile.Loadout; loadoutSlots[i].i = i; }
            for (int i = 0; i < stashSlots.Count; i++) { stashSlots[i].c = Profile.Stash; stashSlots[i].i = i; }
            if (seenVersion != Profile.Version) { seenVersion = Profile.Version; Refresh(); }

            shownCredits = Mathf.Abs(shownCredits - Profile.Credits) < 1f ? Profile.Credits : Mathf.Lerp(shownCredits, Profile.Credits, 1f - Mathf.Exp(-dt * 9f));
            creditsText.text = $"{Mathf.RoundToInt(shownCredits):N0}<size=24> CR</size>";
            creditsText.supportRichText = true;
            statusT = Mathf.Max(0f, statusT - dt);
            statusText.color = A(statusText.color, Mathf.Clamp01(statusT));

            var input = PlayerInputSource.Current;
            Vector2 m = input.pointer;
            object hover = null;
            foreach (var s in AllSlots())
            {
                bool h = !s.c[s.i].IsEmpty && Contains(s.rt, m);
                s.bg.color = h ? new Color(0.06f, 0.15f, 0.19f, 0.95f) : new Color(0.025f, 0.055f, 0.08f, 0.92f);
                if (h) hover = s;
            }
            foreach (var b in buttons)
            {
                bool on = b.enabled == null || b.enabled();
                bool h = on && Contains(b.rt, m);
                b.fill.color = A(b.accent, !on ? 0.05f : h ? 0.42f : 0.18f);
                b.frame.color = A(b.accent, on ? (h ? 1f : 0.75f) : 0.2f);
                b.label.color = on ? UiKit.TextColor : new Color(1, 1, 1, 0.3f);
                if (h) hover = b;
            }
            if (hover != null && hover != lastHover) ProcAudio.Play("ui_hover", 0.35f, 0.03f);
            lastHover = hover;

            if (openT < 0.4f) return;      // the key that opened the base must not also press something here
            if (input.click)
            {
                if (hover is Button hb) hb.onClick();
                else if (hover is SlotUi hs && Items.IsWeapon(hs.c[hs.i].id))
                {
                    bool ok = Profile.ToggleEquip(hs.c, hs.i);
                    ProcAudio.Play(ok ? "ui_equip" : "ui_deny", 0.55f, 0.03f);
                    if (!ok) Status("NO ROOM", false);
                }
            }
            else if (input.rightClick && hover is SlotUi rs && rs.c == Profile.Stash)
            {
                int v = Profile.Sell(rs.c, rs.i);
                if (v > 0) { ProcAudio.Play("pickup_res", 0.55f, 0f, 1.1f); Status($"SOLD  +{v:N0} CR", true); }
            }
            else if (input.restart) Deploy();
        }

        IEnumerable<SlotUi> AllSlots()
        {
            foreach (var s in loadoutSlots) yield return s;
            foreach (var s in stashSlots) yield return s;
        }

        bool Contains(RectTransform rt, Vector2 screen) => rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, UiCam);

        void Refresh()
        {
            foreach (var s in AllSlots()) Draw(s);
            // what the next raid's suit will have: the operative's base stats plus the upgrades owned now
            var ph = PlayerController.I;
            float hp = (ph && ph.health ? ph.health.BaseMaxHealth : 100f) + Profile.HealthBonus;
            float sh = (ph && ph.health ? ph.health.BaseMaxShield : 50f) + Profile.ShieldBonus;
            int gr = (ph && ph.grenades ? ph.grenades.BaseMaxCharges : 2) + Profile.GrenadeBonus;
            suitText.text = $"HEALTH  <color=#{UiKit.Hex(UiKit.Amber)}>{hp:0}</color>      SHIELD  <color=#{UiKit.Hex(Cyan)}>{sh:0}</color>      GRENADES  <color=#{UiKit.Hex(UiKit.Amber)}>{gr}</color>";
            statsText.text = UiKit.Spaced($"RAIDS {Profile.Raids}   EXTRACTED {Profile.Extractions}   KIA {Profile.Deaths}");
            foreach (var (price, k) in weaponRows) price.text = $"{Profile.BuyPrice(k):N0} CR";
            foreach (var (pips, price, u) in upgradeRows)
            {
                int l = Profile.Level(u.id);
                for (int i = 0; i < pips.Length; i++) pips[i].color = i < l ? Cyan : new Color(1, 1, 1, 0.12f);
                int p = Profile.UpgradePrice(u);
                price.text = p < 0 ? "MAX" : $"{p:N0} CR";
            }
            var sellBtn = buttons.Find(b => b.label.text == "SELL ALL RESOURCES");
            if (sellBtn != null) sellBtn.price.text = $"+{ResourceValue():N0} CR";
        }

        void Draw(SlotUi s)
        {
            var st = s.c[s.i];
            if (s.hotkey) s.hotkey.text = (s.i + 1).ToString();
            if (st.IsEmpty)
            {
                s.icon.enabled = false;
                s.count.text = "";
                s.glow.color = Color.clear;
                s.frame.color = new Color(1, 1, 1, 0.1f);
                s.label.text = s.wide ? (s.i == 0 && LoadoutEmpty() ? "STANDARD ISSUE CARBINE" : "EMPTY SLOT") : "";
                s.label.color = new Color(1, 1, 1, 0.3f);
                return;
            }
            var d = st.Def;
            s.icon.enabled = true;
            s.icon.sprite = ItemIcons.Get(st.id);
            if (!s.wide)
            {
                bool gun = d.category == ItemCategory.Weapon;
                s.icon.rectTransform.sizeDelta = gun ? new Vector2(88, 44) : new Vector2(64, 64);
                s.icon.rectTransform.localRotation = Quaternion.Euler(0, 0, gun ? 30f : 0f);
            }
            s.count.text = st.count > 1 ? $"x{st.count}" : "";
            s.glow.color = A(d.rarityColor, 0.28f);
            s.frame.color = A(d.rarityColor, 0.6f);
            s.label.text = d.name;
            s.label.color = UiKit.TextColor;
        }

        static bool LoadoutEmpty()
        {
            foreach (var s in Profile.Loadout.slots) if (!s.IsEmpty) return false;
            return true;
        }
    }
}
