using UnityEngine;
using UnityEngine.UI;

namespace Zcom
{
    /// <summary>
    /// Raid-level UI on top of the HUD: evac marker + extraction countdown, pause menu (Esc), and the end-of-raid
    /// summary (extracted or KIA) with time, kills, camps and the value of what you carried out.
    /// </summary>
    public class RaidUI : MonoBehaviour
    {
        public static RaidUI I { get; private set; }

        static readonly Color Cyan = new Color(0.36f, 0.9f, 1f);
        static readonly Color Glass = new Color(0.02f, 0.045f, 0.07f, 0.8f);
        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        RectTransform root, marker, extractBox;
        Text markerDist, extractText;
        Image extractFill, markerIcon;
        CanvasGroup pauseGroup, summaryGroup;
        Text summaryKicker, summaryTitle, summaryStats, summaryLoot;
        bool summaryShown;

        public void Init(RectTransform canvasRoot)
        {
            I = this;
            root = canvasRoot;

            // evac marker (clamped to the screen edge when off-screen)
            marker = UiKit.Node("EvacMarker", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 60));
            markerIcon = UiKit.Img("Icon", marker, UiKit.Art("icon_extract"), Cyan, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(30, 30));
            markerIcon.preserveAspect = true;
            UiKit.Label("Label", marker, UiKit.Spaced("EVAC"), 13, Cyan, TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0, -8), new Vector2(120, 16), false);
            markerDist = UiKit.Label("Dist", marker, "", 13, A(Cyan, 0.75f), TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0, -24), new Vector2(120, 16), false);

            // extraction countdown (top centre, under the area name)
            extractBox = UiKit.Node("Extract", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -120), new Vector2(420, 54));
            UiKit.Fill("Fill", extractBox, UiKit.PanelFill, Glass);
            UiKit.Fill("Frame", extractBox, UiKit.PanelFrame, A(Cyan, 0.6f));
            extractText = UiKit.Label("Text", extractBox, "", 18, Cyan, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -6), new Vector2(400, 26), false);
            var bar = UiKit.Node("Bar", extractBox, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 10), new Vector2(380, 6));
            UiKit.Fill("Bg", bar, UiKit.White, new Color(1, 1, 1, 0.1f));
            extractFill = UiKit.Fill("Fill", bar, UiKit.White, Cyan);
            extractFill.type = Image.Type.Filled; extractFill.fillMethod = Image.FillMethod.Horizontal;
            extractBox.gameObject.SetActive(false);

            // pause menu
            var pause = UiKit.Stretch("Pause", root);
            pauseGroup = pause.gameObject.AddComponent<CanvasGroup>();
            UiKit.Fill("Dim", pause, UiKit.White, new Color(0f, 0.01f, 0.025f, 0.72f));
            var pc = Card(pause, new Vector2(460, 330));
            UiKit.Label("Kicker", pc, UiKit.Spaced("RAID SUSPENDED"), 14, Cyan, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -28), new Vector2(420, 20), false);
            UiKit.Label("Title", pc, UiKit.Spaced("PAUSED"), 52, UiKit.TextColor, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -70), new Vector2(420, 60), false);
            var rows = new[] { ("ESC", "Resume"), ("R", "Restart raid"), ("H", "Controls"), ("Q", "Quit to desktop") };
            for (int i = 0; i < rows.Length; i++)
            {
                UiKit.KeyCap("Key", pc, rows[i].Item1, new Vector2(0.5f, 1f), new Vector2(0f, 0.5f), new Vector2(-120, -150 - i * 40), 15);
                UiKit.Label("Action", pc, rows[i].Item2, 20, UiKit.TextColor, TextAnchor.MiddleLeft, new Vector2(0.5f, 1f), new Vector2(0f, 0.5f), new Vector2(-40, -150 - i * 40), new Vector2(240, 30), false);
            }
            pause.gameObject.SetActive(false);

            // end-of-raid summary
            var sum = UiKit.Stretch("Summary", root);
            summaryGroup = sum.gameObject.AddComponent<CanvasGroup>();
            UiKit.Fill("Dim", sum, UiKit.White, new Color(0f, 0.01f, 0.025f, 0.78f));
            var sc = Card(sum, new Vector2(640, 400));
            summaryKicker = UiKit.Label("Kicker", sc, "", 15, Cyan, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -28), new Vector2(600, 20), false);
            summaryTitle = UiKit.Label("Title", sc, "", 56, UiKit.TextColor, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -72), new Vector2(620, 64), false);
            UiKit.Img("Rule", sc, UiKit.FadeLine, A(Cyan, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, 0.5f), new Vector2(0, -146), new Vector2(260, 1.5f));
            UiKit.Img("RuleL", sc, UiKit.FadeLine, A(Cyan, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, 0.5f), new Vector2(0, -146), new Vector2(260, 1.5f)).rectTransform.localScale = new Vector3(-1, 1, 1);
            summaryStats = UiKit.Label("Stats", sc, "", 20, UiKit.TextColor, TextAnchor.UpperLeft, new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(-240, -170), new Vector2(240, 160), false);
            summaryLoot = UiKit.Label("Loot", sc, "", 20, UiKit.TextColor, TextAnchor.UpperLeft, new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(20, -170), new Vector2(240, 160), false);
            summaryStats.lineSpacing = summaryLoot.lineSpacing = 1.3f;
            UiKit.KeyCap("Key", sc, "R", new Vector2(0.5f, 0f), new Vector2(1f, 0.5f), new Vector2(-50, 40), 16);
            UiKit.Label("Again", sc, UiKit.Spaced("NEW RAID"), 18, Cyan, TextAnchor.MiddleLeft, new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), new Vector2(-38, 40), new Vector2(200, 26), false);
            sum.gameObject.SetActive(false);
        }

        RectTransform Card(Transform parent, Vector2 size)
        {
            var p = UiKit.Node("Card", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            UiKit.Img("Shadow", p, UiKit.Glow, new Color(0, 0, 0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -8), size + new Vector2(60, 60));
            UiKit.Fill("Fill", p, UiKit.PanelFill, Glass);
            UiKit.Fill("Frame", p, UiKit.PanelFrame, A(Cyan, 0.5f));
            return p;
        }

        bool Paused => GameFeel.Paused;

        void SetPaused(bool on)
        {
            GameFeel.Paused = on;
            pauseGroup.gameObject.SetActive(on);
            pauseGroup.transform.SetAsLastSibling();
            Cursor.visible = on;
            ProcAudio.Play("equip", 0.35f, 0f, on ? 0.8f : 1.1f);
        }

        void Update()
        {
            var input = PlayerInputSource.Current;
            var gm = GameManager.I;
            var p = PlayerController.I;
            bool ended = gm && (gm.IsGameOver || gm.IsExtracted);
            bool invOpen = p && p.inventory && p.inventory.PanelOpen;

            if (!ended && !invOpen && input.cancel) SetPaused(!Paused);
            if (Paused)
            {
                if (input.restart && gm) gm.Restart();
                else if (input.scan)
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                }
            }
            if (summaryShown) summaryGroup.alpha = Mathf.MoveTowards(summaryGroup.alpha, 1f, Time.unscaledDeltaTime * 1.5f);
        }

        void LateUpdate()
        {
            var gm = GameManager.I;
            var ez = gm ? gm.Extraction : null;
            var cam = CameraRig.I ? CameraRig.I.Cam : Camera.main;
            bool show = ez && cam && !summaryShown && !Paused && !(PlayerController.I && PlayerController.I.inventory && PlayerController.I.inventory.PanelOpen);
            marker.gameObject.SetActive(show);
            extractBox.gameObject.SetActive(show && ez.Progress01 > 0f);
            if (!show) return;

            // marker over the beacon, clamped inside the screen
            Vector3 sp = cam.WorldToScreenPoint(ez.transform.position + Vector3.up * 3.5f);
            bool behind = sp.z < 0f;
            if (behind) sp = -sp;
            float m = 70f;
            Vector2 clamped = new Vector2(Mathf.Clamp(sp.x, m, Screen.width - m), Mathf.Clamp(sp.y, m + 160f, Screen.height - m));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, clamped, null, out var local);
            marker.anchoredPosition = local;
            markerDist.text = $"{ez.Distance:0}m";
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f);
            markerIcon.color = A(Cyan, pulse);
            marker.gameObject.SetActive(ez.Distance > ez.radius + 1f);

            if (ez.Progress01 > 0f)
            {
                float left = ez.holdTime * (1f - ez.Progress01);
                extractText.text = ez.PlayerInside ? $"{UiKit.Spaced("EXTRACTING")}   {left:0.0}s" : UiKit.Spaced("RETURN TO THE EVAC POINT");
                extractText.color = ez.PlayerInside ? Cyan : UiKit.Amber;
                extractFill.fillAmount = ez.Progress01;
                extractFill.color = ez.PlayerInside ? Cyan : UiKit.Amber;
            }
        }

        public void ShowSummary(bool extracted)
        {
            if (summaryShown) return;
            summaryShown = true;
            var gm = GameManager.I;
            var p = PlayerController.I;
            int value = 0, items = 0;
            if (p && p.inventory)
            {
                foreach (var st in p.inventory.Backpack.slots) if (!st.IsEmpty) { value += st.Def.value * st.count; items += st.count; }
                foreach (var st in p.inventory.WeaponSlots.slots) if (!st.IsEmpty) { value += st.Def.value; items++; }
            }
            float t = gm ? gm.RaidTime : 0f;
            string hex(Color c) => UiKit.Hex(c);
            summaryKicker.text = UiKit.Spaced(extracted ? "EVAC SUCCESSFUL" : "OPERATIVE DOWN");
            summaryKicker.color = extracted ? Cyan : UiKit.Danger;
            summaryTitle.text = UiKit.Spaced(extracted ? "EXTRACTED" : "KIA");
            summaryStats.text =
                $"<color=#{hex(UiKit.TextDim)}>RAID TIME</color>\n<size=26>{(int)(t / 60f):00}:{(int)(t % 60f):00}</size>\n" +
                $"<color=#{hex(UiKit.TextDim)}>ELIMINATIONS</color>   {(gm ? gm.Kills : 0)}\n" +
                $"<color=#{hex(UiKit.TextDim)}>CAMPS CLEARED</color>   {(gm ? gm.CampsCleared : 0)} / {(gm ? gm.CampsTotal : 0)}";
            summaryLoot.text = extracted
                ? $"<color=#{hex(UiKit.TextDim)}>LOOT SECURED</color>\n<size=26><color=#{hex(UiKit.Amber)}>{value:N0} cr</color></size>\n<color=#{hex(UiKit.TextDim)}>ITEMS</color>   {items}"
                : $"<color=#{hex(UiKit.TextDim)}>LOOT LOST</color>\n<size=26><color=#{hex(UiKit.Danger)}>{value:N0} cr</color></size>\n<color=#{hex(UiKit.TextDim)}>ITEMS</color>   {items}";
            summaryGroup.alpha = 0f;
            summaryGroup.gameObject.SetActive(true);
            summaryGroup.transform.SetAsLastSibling();
            Cursor.visible = true;
        }
    }
}
