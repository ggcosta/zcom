using UnityEngine;
using UnityEngine.UI;

namespace Zcom
{
    /// <summary>
    /// Diegetic vitals projected on the ground around the operative: a segmented shield arc (cyan) over a health arc
    /// (amber) on the side facing the camera, grenade pips and a dash-ready tick. Dims when everything is full and calm,
    /// lights up in combat, when hurt or regenerating, and flashes white on hits.
    /// </summary>
    public class PlayerRing : MonoBehaviour
    {
        const int Segs = 14;
        const float Span = 150f;           // degrees covered by the arcs, centred on the camera side
        const float Px = 100f;             // canvas units per metre

        Image[] shield, health;
        Image[] grenadePips;
        Image dashTick, under;
        CanvasGroup group;
        float alpha, hitFlash, lastShield = -1f, lastHealth = -1f;
        float hotUntil;

        public static PlayerRing Create()
        {
            var go = new GameObject("PlayerRing", typeof(RectTransform));
            return go.AddComponent<PlayerRing>();
        }

        void Awake()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 5;
            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(300, 300);
            rt.localScale = Vector3.one / Px;
            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            under = UiKit.Img("Under", rt, HoloKit.SoftDot, TacKit.A(TacKit.Cyan, 0.05f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220, 220));
            UiKit.Img("Ticks", rt, HoloKit.RingTicks, TacKit.A(TacKit.Cyan, 0.1f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(176, 176));
            shield = Arc("Shield", 200f, 2.5f);
            health = Arc("Health", 186f, 2.5f);

            grenadePips = new Image[4];
            for (int i = 0; i < grenadePips.Length; i++)
            {
                float ang = (180f + Span * 0.5f + 8f + i * 8f) * Mathf.Deg2Rad;    // just past the left end of the arcs (clockwise from up)
                var p = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * 98f;
                grenadePips[i] = UiKit.Img("Nade" + i, rt, TacKit.Pip, UiKit.Amber, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), p, new Vector2(9, 9));
            }
            dashTick = UiKit.Img("Dash", rt, TacKit.Chevron, TacKit.Cyan, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -114), new Vector2(14, 11));
            dashTick.rectTransform.localRotation = Quaternion.Euler(0, 0, 180f);
        }

        /// <summary>Segmented arc centred on -Y (the camera side once the canvas is laid flat and yawed to the camera).</summary>
        Image[] Arc(string name, float diameter, float gapDeg)
        {
            var arr = new Image[Segs];
            float seg = Span / Segs;
            for (int i = 0; i < Segs; i++)
            {
                // start angle measured clockwise from up; the arc runs from 180 - Span/2 to 180 + Span/2 (bottom)
                float start = 180f + Span * 0.5f - (i + 1) * seg + gapDeg * 0.5f;
                var img = HoloKit.ArcImage($"{name}{i}", transform, diameter, Color.white, start);
                img.sprite = TacKit.ThinArc;
                img.fillAmount = (seg - gapDeg) / 360f;
                arr[i] = img;
            }
            return arr;
        }

        public void Flash() { hitFlash = 1f; hotUntil = Time.time + 3f; }

        void LateUpdate()
        {
            var p = PlayerController.I;
            var cam = CameraRig.I ? CameraRig.I.Cam : Camera.main;
            if (!p || !cam || !p.health) { group.alpha = 0f; return; }
            transform.position = p.transform.position + Vector3.up * 0.12f;
            transform.rotation = Quaternion.Euler(90f, cam.transform.eulerAngles.y, 0f);

            var h = p.health;
            float s01 = h.Shield01, h01 = h.Health01;
            if (lastShield >= 0f && (s01 < lastShield - 0.001f || h01 < lastHealth - 0.001f)) Flash();
            lastShield = s01; lastHealth = h01;
            float dt = Time.unscaledDeltaTime;
            hitFlash = Mathf.MoveTowards(hitFlash, 0f, dt * 3.5f);

            bool combat = GameManager.I && GameManager.I.AlertedHostiles > 0;
            bool hurt = s01 < 0.999f || h01 < 0.999f;
            bool alive = h.IsAlive && p.CanAct;
            bool uiOpen = p.inventory && p.inventory.PanelOpen;
            float target = !alive || uiOpen ? 0f : combat || Time.time < hotUntil ? 0.8f : hurt ? 0.55f : 0.14f;
            alpha = Mathf.MoveTowards(alpha, target, dt * 2.5f);
            group.alpha = alpha;

            Color sc = Color.Lerp(TacKit.Cyan, Color.white, hitFlash * 0.8f);
            Color hc = h01 < 0.3f ? Color.Lerp(UiKit.Danger, Color.white, Mathf.PingPong(Time.time * 3f, 0.5f)) : Color.Lerp(new Color(1f, 0.72f, 0.26f), Color.white, hitFlash * 0.5f);
            Fill(shield, s01, sc, s01 > 0f && s01 < 1f ? Time.time * 1.4f : -1f);
            Fill(health, h01, hc, -1f);
            under.color = TacKit.A(h01 < 0.3f ? UiKit.Danger : TacKit.Cyan, 0.06f + hitFlash * 0.12f);

            var gr = p.grenades;
            for (int i = 0; i < grenadePips.Length; i++)
            {
                bool on = gr && i < gr.maxCharges;
                grenadePips[i].enabled = on;
                if (on) grenadePips[i].color = i < gr.Charges ? UiKit.Amber : new Color(1, 1, 1, 0.15f);
            }
            dashTick.color = p.DashCooldown01 <= 0f ? TacKit.A(TacKit.Cyan, 0.9f) : new Color(1, 1, 1, 0.12f);
        }

        static void Fill(Image[] segs, float v, Color full, float shimmer)
        {
            int n = segs.Length;
            float sh = shimmer >= 0f ? (shimmer % 1.5f) * n : -10f;
            for (int i = 0; i < n; i++)
            {
                float a = (i + 1f) / n, b = i / (float)n;
                Color c = v >= a ? full : v > b ? Color.Lerp(new Color(1, 1, 1, 0.08f), full, (v - b) * n) : new Color(1, 1, 1, 0.07f);
                float glint = Mathf.Clamp01(1f - Mathf.Abs(i - sh) / 1.5f);
                if (glint > 0f && v > b) c = Color.Lerp(c, Color.white, glint * 0.6f);
                segs[i].color = c;
            }
        }
    }
}
