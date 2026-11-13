using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Zcom
{
    /// <summary>
    /// Field-kit HUD clusters (see FieldKit; target art Art/Reference/Codex/hud_b_fieldkit_target.png). Everything the
    /// operative needs sits in one belt at the bottom centre, close to the character: skill tiles, suit integrity +
    /// shield, weapon cell. The corners carry the paperwork: a printed work order (objective checklist), raid clock and
    /// cargo value, an area tag and a hazard band for threats. Blocks slide in mechanically, counters tick like flap
    /// displays, hits rattle the belt, stripes march when something is wrong.
    /// </summary>
    public class FieldHud
    {
        static Color Ink => FieldKit.Ink;
        static Color Black => FieldKit.InkSolid;
        static Color Paper => FieldKit.Paper;
        static Color Signal => FieldKit.Signal;
        static Color Ice => FieldKit.Ice;
        static Color A(Color c, float a) => FieldKit.A(c, a);
        static readonly Vector2 BL = Vector2.zero, TL = new Vector2(0, 1), TR = new Vector2(1, 1), BR = new Vector2(1, 0);
        static readonly Vector2 ML = new Vector2(0, 0.5f), MR = new Vector2(1, 0.5f), MC = new Vector2(0.5f, 0.5f);

        RectTransform root;
        CanvasGroup group;
        float t;

        class Slide { public RectTransform rt; public Vector2 home, from; public float delay; }
        readonly List<Slide> slides = new List<Slide>();

        // belt
        RectTransform belt, vitals;
        float rattle, hitFlash;

        class Skill { public RectTransform rt; public Image bg, fill, icon, line; public Text key; public Image[] pips; public float punch; public bool wasReady = true; }
        Skill grenade, dash, scan;

        TickText hpNum, ammoNum;
        Text hpText, shText, stateText, ammoText, ammoMax;
        Image hpFill, hpChip, shFill, hpFlash;
        RawImage hpStripes, shStripes;
        float chip = 1f, chipDelay, shieldDownT;

        FieldKit.Tape weaponTape;
        RectTransform strip;
        readonly List<Image> segs = new List<Image>();
        float[] segFlash = new float[0];
        RawImage reloadStripes;
        Image reloadFill;
        class Chip { public Image bg, frame; public Text num; public RectTransform rt; }
        readonly Chip[] chips = new Chip[3];
        int lastInv = -1, lastAmmo = -1;
        WeaponKind? shownWeapon;
        bool weaponInit;

        // corners
        Text orderText, orderCount, clockText, cargoText;
        Image[] boxes;
        FieldKit.Tape areaTape;
        RectTransform areaRoot, threat;
        CanvasGroup areaGroup;
        Text threatText;
        RawImage[] threatStripes;
        string lastArea;
        float areaT = -1f, cargoFlash, threatK, threatHold;
        int lastCargo = -1;

        // ================================================================== build

        public void Build(RectTransform canvasRoot)
        {
            root = UiKit.Stretch("FieldHud", canvasRoot);
            group = root.gameObject.AddComponent<CanvasGroup>();
            BuildBelt();
            BuildOrder();
            BuildStatus();
            BuildArea();
        }

        void AddSlide(RectTransform rt, Vector2 offset, float delay)
        {
            slides.Add(new Slide { rt = rt, home = rt.anchoredPosition, from = rt.anchoredPosition + offset, delay = delay });
            rt.anchoredPosition += offset;
        }

        const float BeltH = 104f, BeltY = 30f, SkillW = 88f, Gap = 6f, VitalsW = 520f, WeaponW = 366f;

        void BuildBelt()
        {
            float w = SkillW * 3 + Gap * 2 + Gap + VitalsW + Gap + WeaponW;
            belt = UiKit.Node("Belt", root, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, BeltY), new Vector2(w, BeltH));

            string[] icons = { "icon_grenade", "icon_dash", "icon_scan" };
            string[] keys = { "G", "SPACE", "Q" };
            var sk = new Skill[3];
            for (int i = 0; i < 3; i++)
            {
                sk[i] = MakeSkill(i * (SkillW + Gap), icons[i], keys[i], i == 0 ? 2 : 0);
                AddSlide(sk[i].rt, new Vector2(0, -180), 0.35f + i * 0.05f);
            }
            grenade = sk[0]; dash = sk[1]; scan = sk[2];

            float x = SkillW * 3 + Gap * 3;
            BuildVitals(x);
            AddSlide(vitals, new Vector2(0, -180), 0.5f);
            x += VitalsW + Gap;
            var wb = BuildWeapon(x);
            AddSlide(wb, new Vector2(0, -180), 0.6f);
        }

        Skill MakeSkill(float x, string icon, string key, int pips)
        {
            var s = new Skill();
            s.rt = FieldKit.Block("Skill", belt, BL, BL, new Vector2(x, 0), new Vector2(SkillW, BeltH), Ink);
            s.rt.pivot = MC;
            s.rt.anchoredPosition = new Vector2(x + SkillW * 0.5f, BeltH * 0.5f);
            s.bg = s.rt.Find("Fill").GetComponent<Image>();
            s.fill = UiKit.Fill("Cool", s.rt, UiKit.White, A(Paper, 0.16f));
            s.fill.type = Image.Type.Filled; s.fill.fillMethod = Image.FillMethod.Vertical; s.fill.fillOrigin = 0;
            s.icon = UiKit.Img("Icon", s.rt, UiKit.Art(icon), Paper, new Vector2(0.5f, 1), MC, new Vector2(0, -38), new Vector2(46, 46));
            s.icon.preserveAspect = true;
            s.key = FieldKit.Label("Key", s.rt, key, 19, Paper, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), MC, new Vector2(0, 22), new Vector2(SkillW, 22), FieldKit.Caption);
            s.line = UiKit.Img("Line", s.rt, UiKit.White, Signal, BL, BL, Vector2.zero, new Vector2(SkillW - 16, 4));
            s.pips = new Image[pips];
            for (int i = 0; i < pips; i++)
                s.pips[i] = UiKit.Img("Pip" + i, s.rt, UiKit.White, Signal, TR, TR, new Vector2(-8 - i * 13, -8), new Vector2(9, 9));
            return s;
        }

        void BuildVitals(float x)
        {
            vitals = FieldKit.Block("Vitals", belt, BL, BL, new Vector2(x, 0), new Vector2(VitalsW, BeltH), Ink);
            hpFlash = UiKit.Fill("Flash", vitals, TacKit.Slot, A(FieldKit.Danger, 0f));
            UiKit.Img("Brand", vitals, UiKit.White, Signal, BL, BL, Vector2.zero, new Vector2(10, BeltH));

            hpText = FieldKit.Label("HP", vitals, "100", 92, Paper, TextAnchor.MiddleLeft, BL, ML, new Vector2(26, 60), new Vector2(150, 90));
            hpNum = hpText.gameObject.AddComponent<TickText>();
            FieldKit.Micro("Lbl", vitals, "SUIT INTEGRITY", BL, ML, new Vector2(27, 17), TextAnchor.MiddleLeft, 16);

            const float bx = 158f, bw = 340f;
            // shield: thin bar on top, its number at the end
            var sb = UiKit.Node("Shield", vitals, BL, ML, new Vector2(bx, 82), new Vector2(bw - 50, 10));
            UiKit.Fill("Bg", sb, UiKit.White, A(Paper, 0.14f));
            shStripes = FieldKit.Stripes("Off", sb, A(FieldKit.Danger, 0.7f), new Vector2(bw - 50, 10), 0.35f);
            shFill = UiKit.Fill("Fill", sb, UiKit.White, Ice);
            shFill.type = Image.Type.Filled; shFill.fillMethod = Image.FillMethod.Horizontal;
            shText = FieldKit.Label("ShVal", vitals, "50", 26, Ice, TextAnchor.MiddleRight, BL, MR, new Vector2(bx + bw, 82), new Vector2(50, 26));

            // health: thick paper bar with quarter ticks, chip damage in signal orange, danger stripes when critical
            var hb = UiKit.Node("Health", vitals, BL, ML, new Vector2(bx, 52), new Vector2(bw, 30));
            UiKit.Fill("Bg", hb, UiKit.White, A(Paper, 0.14f));
            hpStripes = FieldKit.Stripes("Crit", hb, A(FieldKit.Danger, 0.6f), new Vector2(bw, 30), 0.5f);
            hpChip = UiKit.Fill("Chip", hb, UiKit.White, Signal);
            hpChip.type = Image.Type.Filled; hpChip.fillMethod = Image.FillMethod.Horizontal;
            hpFill = UiKit.Fill("Fill", hb, UiKit.White, Paper);
            hpFill.type = Image.Type.Filled; hpFill.fillMethod = Image.FillMethod.Horizontal;
            for (int i = 1; i < 4; i++)
                UiKit.Img("Tick" + i, hb, UiKit.White, Black, new Vector2(i / 4f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(2.5f, 15));

            stateText = FieldKit.Label("State", vitals, "", 16, A(Paper, 0.75f), TextAnchor.MiddleRight, BL, MR, new Vector2(bx + bw, 17), new Vector2(240, 20), FieldKit.Caption);
        }

        RectTransform BuildWeapon(float x)
        {
            var wb = FieldKit.Block("Weapon", belt, BL, BL, new Vector2(x, 0), new Vector2(WeaponW, BeltH), Ink);
            weaponTape = new FieldKit.Tape("Name", wb, TL, TL, new Vector2(14, -14), 30, 25, Paper, Black, 11f);
            ammoText = FieldKit.Label("Ammo", wb, "", 92, Paper, TextAnchor.MiddleRight, BR, MR, new Vector2(-92, 60), new Vector2(170, 90));
            ammoNum = ammoText.gameObject.AddComponent<TickText>();
            ammoMax = FieldKit.Label("Max", wb, "", 36, A(Paper, 0.85f), TextAnchor.LowerLeft, BR, BL, new Vector2(-88, 30), new Vector2(86, 40));

            strip = UiKit.Node("Strip", wb, BL, ML, new Vector2(14, 16), new Vector2(WeaponW - 28, 10));
            var rl = UiKit.Node("Reload", strip, ML, ML, Vector2.zero, new Vector2(WeaponW - 28, 10));
            reloadFill = UiKit.Fill("Fill", rl, UiKit.White, Signal);
            reloadFill.type = Image.Type.Filled; reloadFill.fillMethod = Image.FillMethod.Horizontal;
            reloadFill.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            reloadStripes = FieldKit.Stripes("Stripes", reloadFill.transform, A(Black, 0.5f), new Vector2(WeaponW - 28, 10), 0.35f);
            rl.gameObject.SetActive(false);

            // loadout chips ride on top of the block: outlined ink boxes, the equipped one filled orange
            for (int i = 0; i < 3; i++)
            {
                var c = new Chip();
                c.rt = UiKit.Node("Chip" + i, wb, TR, BR, new Vector2(-(2 - i) * 56, Gap), new Vector2(52, 32));
                c.frame = UiKit.Fill("Frame", c.rt, UiKit.White, A(Paper, 0.85f));
                c.bg = UiKit.Fill("Bg", c.rt, UiKit.White, Black, 2f);
                c.num = FieldKit.Label("Num", c.rt, (i + 1).ToString(), 24, Paper, TextAnchor.MiddleCenter, MC, MC, new Vector2(0, -1), new Vector2(52, 30));
                chips[i] = c;
            }
            return wb;
        }

        void RebuildSegs(int count)
        {
            foreach (var s in segs) Object.Destroy(s.gameObject);
            segs.Clear();
            segFlash = new float[count];
            if (count <= 0) return;
            float w = strip.sizeDelta.x, gap = count > 24 ? 2.5f : 4f;
            float sw = (w - gap * (count - 1)) / count;
            for (int i = 0; i < count; i++)
                segs.Add(UiKit.Img("S" + i, strip, UiKit.White, Paper, ML, ML, new Vector2(i * (sw + gap), 0), new Vector2(sw, 10)));
            strip.Find("Reload").SetAsLastSibling();
        }

        /// <summary>Top-left: the objective as a printed work order with tick boxes, a barcode and small print.</summary>
        void BuildOrder()
        {
            const float w = 412f, h = 150f;
            var p = FieldKit.PaperBlock("WorkOrder", root, TL, TL, new Vector2(40, -36), new Vector2(w, h));
            var head = UiKit.Node("Head", p, TL, TL, Vector2.zero, new Vector2(w, 32));
            UiKit.Fill("Bg", head, UiKit.White, Black);
            UiKit.Img("Dot", head, UiKit.White, Signal, ML, ML, new Vector2(14, 0), new Vector2(12, 12));
            FieldKit.Micro("T", head, "WORK ORDER", ML, ML, new Vector2(38, 0), TextAnchor.MiddleLeft, 18).color = Paper;
            UiKit.Img("Div", head, UiKit.White, A(Paper, 0.5f), MR, MC, new Vector2(-106, 0), new Vector2(1.5f, 16));
            FieldKit.Micro("N", head, "KMC-07", MR, MR, new Vector2(-14, 0), TextAnchor.MiddleRight, 18).color = Paper;

            orderText = FieldKit.Label("Text", p, "", 42, Black, TextAnchor.MiddleLeft, TL, ML, new Vector2(16, -62), new Vector2(w - 30, 46));
            UiKit.Img("Rule1", p, UiKit.White, A(Black, 0.75f), TL, ML, new Vector2(16, -88), new Vector2(w - 32, 1.5f));
            boxes = new Image[6];
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = UiKit.Node("Box" + i, p, TL, ML, new Vector2(16 + i * 30, -107), new Vector2(22, 22));
                UiKit.Fill("Frame", b, UiKit.White, Black);
                boxes[i] = UiKit.Fill("In", b, UiKit.White, Paper, 3f);
            }
            var bc = HoloKit.Tiled("Barcode", p, FieldKit.Barcode, Black);
            var brt = bc.rectTransform;
            brt.anchorMin = brt.anchorMax = TL; brt.pivot = ML;
            brt.anchoredPosition = new Vector2(206, -103); brt.sizeDelta = new Vector2(104, 15);
            FieldKit.Mono("Serial", p, "WO-883771-4A", 9, A(Black, 0.8f), TL, ML, new Vector2(214, -116));
            orderCount = FieldKit.Label("Count", p, "", 34, Black, TextAnchor.MiddleRight, TL, MR, new Vector2(w - 18, -107), new Vector2(90, 34));
            UiKit.Img("Rule2", p, UiKit.White, A(Black, 0.75f), TL, ML, new Vector2(16, -126), new Vector2(w - 32, 1.5f));
            FieldKit.Mono("Foot", p, "COLONY MINING CO.  //  FIELD OPS", 9, A(Black, 0.8f), TL, ML, new Vector2(16, -137));
            FieldKit.Mono("Rev", p, "REV. B", 9, A(Black, 0.8f), TL, MR, new Vector2(w - 30, -137), TextAnchor.MiddleRight);
            AddSlide(p, new Vector2(-520, 0), 0.75f);
        }

        /// <summary>Top-right: raid clock and the value of what is in the backpack (what dying would cost).</summary>
        void BuildStatus()
        {
            const float w = 300f, rowH = 58f;
            var b = FieldKit.Block("Status", root, TR, TR, new Vector2(-40, -36), new Vector2(w, rowH * 2), Ink);
            UiKit.Img("RowDiv", b, UiKit.White, A(Paper, 0.3f), new Vector2(0.5f, 0.5f), MC, Vector2.zero, new Vector2(w - 28, 1.5f));
            Text Row(string label, float y)
            {
                FieldKit.Micro("Lbl", b, label, TL, ML, new Vector2(18, y), TextAnchor.MiddleLeft, 20).color = Paper;
                UiKit.Img("Div", b, UiKit.White, A(Paper, 0.4f), TL, MC, new Vector2(120, y), new Vector2(1.5f, 30));
                return FieldKit.Label("Val", b, "", 46, Paper, TextAnchor.MiddleRight, TR, MR, new Vector2(-20, y - 1), new Vector2(180, 50));
            }
            clockText = Row("RAID", -rowH * 0.5f);
            cargoText = Row("CARGO", -rowH * 1.5f);
            AddSlide(b, new Vector2(400, 0), 0.8f);
        }

        void BuildArea()
        {
            areaRoot = UiKit.Node("Area", root, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(10, 40));
            areaGroup = areaRoot.gameObject.AddComponent<CanvasGroup>();
            areaGroup.alpha = 0f;
            areaTape = new FieldKit.Tape("Tape", areaRoot, MC, MC, Vector2.zero, 40, 28, Ink, Paper, 34f);
            UiKit.Img("Dot", areaTape.rt, UiKit.White, Signal, ML, ML, new Vector2(14, 0), new Vector2(12, 12));
            areaTape.text.rectTransform.anchoredPosition = new Vector2(10, -1);

            // hazard band: safety orange, ink stripes at both ends, ink type
            const float tw = 430f, th = 38f, ends = 62f;
            threat = UiKit.Node("Threat", root, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -78), new Vector2(tw, th));
            var sh = UiKit.Fill("Shadow", threat, UiKit.Glow, new Color(0, 0, 0, 0.4f));
            sh.rectTransform.offsetMin = new Vector2(-16, -18); sh.rectTransform.offsetMax = new Vector2(16, 12);
            UiKit.Fill("Bg", threat, UiKit.White, Signal);
            threatStripes = new RawImage[2];
            for (int i = 0; i < 2; i++)
            {
                var e = UiKit.Node("End" + i, threat, new Vector2(i, 0.5f), new Vector2(i, 0.5f), new Vector2(i == 0 ? 8 : -8, 0), new Vector2(ends, th - 12));
                threatStripes[i] = FieldKit.Stripes("Stripes", e, Black, new Vector2(ends, th - 12), 0.42f);
            }
            threatText = FieldKit.Label("Text", threat, "", 27, Black, TextAnchor.MiddleCenter, MC, MC, new Vector2(0, -1), new Vector2(tw - ends * 2, 30));
            threat.localScale = new Vector3(0, 1, 1);
        }

        // ================================================================== events

        public void OnDamaged(float amount)
        {
            rattle = Mathf.Max(rattle, Mathf.Clamp(0.4f + amount / 40f, 0.4f, 1f));
            hitFlash = 1f;
            chipDelay = 0.45f;
        }

        public void ShieldBroken() { shieldDownT = 2f; rattle = 1f; }

        // ================================================================== update

        public void Tick(PlayerController player, float dt, bool uiOpen)
        {
            t += dt;
            group.alpha = Mathf.MoveTowards(group.alpha, uiOpen ? 0f : 1f, dt * 7f);

            foreach (var s in slides)
            {
                float k = Mathf.Clamp01((t - s.delay) / 0.38f);
                s.rt.anchoredPosition = Vector2.LerpUnclamped(s.from, s.home, Ease.OutBack(k));
            }
            // hits rattle the belt
            rattle = Mathf.MoveTowards(rattle, 0f, dt * 3.2f);
            hitFlash = Mathf.MoveTowards(hitFlash, 0f, dt * 3f);
            belt.anchoredPosition = new Vector2(0, BeltY) + (rattle > 0f ? new Vector2(Mathf.Sin(t * 95f), Mathf.Cos(t * 77f)) * rattle * rattle * 7f : Vector2.zero);

            if (player)
            {
                TickVitals(player, dt);
                TickSkills(player, dt);
                TickWeapon(player, dt);
            }
            TickCorners(player, dt, uiOpen);
        }

        void TickVitals(PlayerController player, float dt)
        {
            var h = player.health;
            if (!h) return;
            float v = h.Health01, s = h.Shield01;
            chipDelay -= dt;
            if (chip < v) chip = v;
            else if (chipDelay <= 0f) chip = Mathf.MoveTowards(chip, v, dt * 0.7f);
            bool crit = v < 0.3f && h.IsAlive;
            float pulse = Mathf.PingPong(Time.unscaledTime * 3f, 1f);

            hpFill.fillAmount = v;
            hpChip.fillAmount = chip;
            hpFill.color = crit ? Color.Lerp(FieldKit.Danger, Paper, pulse * 0.35f) : Color.Lerp(Paper, Color.white, hitFlash);
            hpStripes.enabled = crit;
            if (crit) { var uv = hpStripes.uvRect; uv.x = -Time.unscaledTime * 1.6f; hpStripes.uvRect = uv; }
            hpNum.Set(Mathf.CeilToInt(h.Health).ToString());
            hpText.color = crit ? Color.Lerp(FieldKit.Danger, Paper, pulse * 0.4f) : Paper;
            hpFlash.color = A(FieldKit.Danger, hitFlash * 0.35f);

            shFill.fillAmount = s;
            bool down = s <= 0f;
            shFill.color = Color.Lerp(Ice, Color.white, s > 0f && s < 1f ? 0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 9f) : 0f);
            shStripes.enabled = down;
            if (down) { var uv = shStripes.uvRect; uv.x = -Time.unscaledTime * 1.6f; shStripes.uvRect = uv; }
            string sv = Mathf.CeilToInt(h.Shield).ToString();
            if (shText.text != sv) shText.text = sv;
            shText.color = down ? FieldKit.Danger : Ice;

            shieldDownT = Mathf.Max(0f, shieldDownT - dt);
            string st = UiKit.Spaced(crit ? "CRITICAL" : down ? "SHIELD OFFLINE" : s < 1f ? "SHIELD RECHARGING" : "NOMINAL");
            if (stateText.text != st) stateText.text = st;
            bool blink = crit || shieldDownT > 0f;
            stateText.color = crit || down ? A(FieldKit.Danger, blink && Mathf.Sin(Time.unscaledTime * 14f) < 0f ? 0.45f : 1f) : s < 1f ? A(Ice, 0.95f) : A(Paper, 0.75f);
        }

        void TickSkills(PlayerController player, float dt)
        {
            void Apply(Skill s, float ready01, bool ready, bool active)
            {
                if (ready && !s.wasReady) s.punch = 1f;
                s.wasReady = ready;
                // ready = paper icon on ink with the signal line; active = inverted signal tile; cooling = dim, filling up
                s.bg.color = active ? Signal : Color.Lerp(Ink, Paper, s.punch * 0.6f);
                s.icon.color = active ? Black : ready ? Paper : A(Paper, 0.3f);
                s.key.color = active ? Black : A(Paper, ready ? 1f : 0.4f);
                s.fill.enabled = !ready && !active;
                s.fill.fillAmount = ready01;
                s.line.enabled = ready && !active;
                s.punch = Mathf.MoveTowards(s.punch, 0f, dt * 4.5f);
                s.rt.localScale = Vector3.one * (1f + s.punch * 0.06f);
            }
            var gr = player.grenades;
            if (gr)
            {
                float r = gr.Charges >= gr.maxCharges ? 1f : gr.RechargeProgress;
                Apply(grenade, gr.Charges > 0 ? 1f : r, gr.Charges > 0, gr.IsAiming);
                for (int i = 0; i < grenade.pips.Length; i++)
                    grenade.pips[i].color = i < gr.Charges ? (gr.IsAiming ? Black : Signal) : A(Paper, 0.2f);
            }
            Apply(dash, 1f - player.DashCooldown01, player.DashCooldown01 <= 0f, player.IsDashing);
            var sp = player.GetComponent<ScanPulse>();
            if (sp) Apply(scan, 1f - sp.Cooldown01, sp.Ready, sp.Cooldown01 > 0.95f);
        }

        void TickWeapon(PlayerController player, float dt)
        {
            var g = player.gun;
            var def = g ? g.Current : null;
            if (def == null)
            {
                if (shownWeapon != null || !weaponInit)
                {
                    weaponInit = true;
                    shownWeapon = null;
                    weaponTape.Set("UNARMED", A(Paper, 0.3f), Paper);
                    ammoNum.Set("--");
                    ammoMax.text = "";
                    RebuildSegs(0);
                }
            }
            else
            {
                if (shownWeapon != def.kind)
                {
                    shownWeapon = def.kind;
                    weaponInit = true;
                    lastAmmo = -1;
                    RebuildSegs(Mathf.Min(def.cellSize, 40));
                }
                int mag = Mathf.Max(1, g.MagazineSize);
                float frac = g.Ammo / (float)mag;
                bool reloading = g.IsReloading, empty = g.Ammo == 0;
                Color ac = empty ? FieldKit.Danger : frac < 0.3f ? Signal : Paper;
                if (g.Ammo != lastAmmo)
                {
                    if (lastAmmo >= 0 && g.Ammo < lastAmmo)
                    {
                        int n = segs.Count;
                        int from = Mathf.CeilToInt(frac * n), to = Mathf.CeilToInt(lastAmmo / (float)mag * n);
                        for (int i = from; i < to && i < n; i++) segFlash[i] = 1f;
                    }
                    lastAmmo = g.Ammo;
                }
                ammoNum.Set(g.Ammo.ToString());
                ammoText.color = ac;
                string max = $"/{g.MagazineSize}";
                if (ammoMax.text != max) ammoMax.text = max;

                for (int i = 0; i < segs.Count; i++)
                {
                    segFlash[i] = Mathf.MoveTowards(segFlash[i], 0f, dt * 6f);
                    bool on = (i + 0.5f) / segs.Count <= frac;
                    Color c = on ? ac : A(Paper, 0.16f);
                    if (segFlash[i] > 0f) c = Color.Lerp(c, Signal, segFlash[i]);
                    if (segs[i].color != c) segs[i].color = c;
                    segs[i].enabled = !reloading;
                }
                var rl = reloadFill.transform.parent.gameObject;
                if (rl.activeSelf != reloading) rl.SetActive(reloading);
                if (reloading)
                {
                    reloadFill.fillAmount = g.ReloadProgress;
                    var uv = reloadStripes.uvRect; uv.x = -Time.unscaledTime * 2.2f; reloadStripes.uvRect = uv;
                }

                // the name tape doubles as the status line
                if (reloading) weaponTape.Set("RECHARGING", Signal, Black);
                else if (empty) weaponTape.Set("CELL EMPTY - R", Mathf.Sin(Time.unscaledTime * 12f) > 0f ? FieldKit.Danger : A(FieldKit.Danger, 0.5f), Black);
                else weaponTape.Set(def.displayName.ToUpperInvariant(), g.IsBeaming ? def.color : Paper, Black);
            }

            var inv = player.inventory;
            if (inv && inv.Version != lastInv)
            {
                lastInv = inv.Version;
                for (int i = 0; i < chips.Length; i++)
                {
                    var st = i < inv.WeaponSlots.Size ? inv.WeaponSlots[i] : ItemStack.Empty;
                    bool on = i == inv.EquippedIndex && !st.IsEmpty;
                    chips[i].bg.color = on ? Signal : Black;
                    chips[i].frame.color = on ? Signal : A(Paper, st.IsEmpty ? 0.3f : 0.85f);
                    chips[i].num.color = on ? Black : st.IsEmpty ? A(Paper, 0.3f) : Paper;
                }
            }
        }

        void TickCorners(PlayerController player, float dt, bool uiOpen)
        {
            var gm = GameManager.I;
            if (!gm) return;

            // work order
            int total = gm.CampsTotal, cleared = gm.CampsCleared;
            string order = gm.PreRaid ? "EXIT VIA REAR DOOR" : total > 0 && cleared >= total ? "RETURN TO THE KESTREL" : "CLEAR XENO CAMPS";
            if (orderText.text != order) orderText.text = order;
            string cnt = gm.PreRaid ? "" : $"{cleared}/{total}";
            if (orderCount.text != cnt) orderCount.text = cnt;
            for (int i = 0; i < boxes.Length; i++)
            {
                boxes[i].transform.parent.gameObject.SetActive(i < total);
                boxes[i].color = i < cleared ? Signal : Paper;
            }

            // clock + cargo
            if (Time.frameCount % 10 == 0)
            {
                float rt = gm.RaidTime;
                clockText.text = $"{(int)(rt / 60f):00}:{(int)(rt % 60f):00}";
                int value = 0;
                if (player && player.inventory)
                    foreach (var st in player.inventory.Backpack.slots) if (!st.IsEmpty) value += st.Def.value * st.count;
                if (value != lastCargo)
                {
                    if (lastCargo >= 0 && value > lastCargo) cargoFlash = 1f;
                    lastCargo = value;
                    cargoText.text = $"{value:N0}<size=28> CR</size>";
                }
            }
            cargoFlash = Mathf.MoveTowards(cargoFlash, 0f, dt * 1.2f);
            cargoText.color = Color.Lerp(Paper, Signal, cargoFlash);
            cargoText.rectTransform.localScale = Vector3.one * (1f + cargoFlash * 0.12f);

            // area tag drops in when the area changes, then sits back
            string area = gm.CurrentArea;
            if (area != lastArea) { lastArea = area; areaTape.Set(UiKit.Spaced(area.ToUpperInvariant())); areaT = 0f; }
            if (areaT >= 0f) areaT += dt;
            float drop = areaT < 0f ? 0f : Ease.OutBack(Mathf.Clamp01(areaT / 0.35f));
            areaRoot.anchoredPosition = new Vector2(0, Mathf.LerpUnclamped(70f, -30f, drop));
            areaGroup.alpha = areaT < 0f || t < 0.9f ? 0f : areaT < 4f ? 1f : Mathf.Max(0.8f, 1f - (areaT - 4f));

            // hazard band (held a moment after the last alerted alien so it never flickers)
            int engaged = gm.AlertedHostiles;
            threatHold = engaged > 0 ? 0.8f : Mathf.Max(0f, threatHold - dt);
            threatK = Mathf.MoveTowards(threatK, threatHold > 0f ? 1f : 0f, dt * 9f);
            threat.localScale = new Vector3(Ease.OutCubic(threatK), 1f, 1f);
            if (engaged > 0)
            {
                string tt = $"{engaged} HOSTILE{(engaged > 1 ? "S" : "")} ENGAGED";
                if (threatText.text != tt) threatText.text = tt;
            }
            if (threatK > 0f)
                foreach (var s in threatStripes) { var uv = s.uvRect; uv.x = -Time.unscaledTime * 0.9f; s.uvRect = uv; }
        }
    }
}
