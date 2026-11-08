using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Orbital Marshal ability (Q): a recon pulse sweeps outward and reveals every alien and unopened
    /// supply crate within range for a few seconds (the HUD marks them, even behind walls).
    /// </summary>
    public class ScanPulse : MonoBehaviour
    {
        public float radius = 30f;
        public float revealTime = 7f;
        public float cooldown = 12f;

        PlayerController owner;
        float readyAt;
        float sweepT = -1f;
        Transform ringA, ringB;
        MeshRenderer ringAR, ringBR;
        MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Color Cyan = new Color(0.25f, 0.88f, 0.94f);

        public float Cooldown01 => Mathf.Clamp01((readyAt - Time.time) / cooldown);
        public bool Ready => Time.time >= readyAt;
        /// <summary>Crates revealed by the last pulse stay highlighted until this time.</summary>
        public float CratesRevealedUntil { get; private set; } = -100f;

        void Awake()
        {
            owner = GetComponent<PlayerController>();
            mpb = new MaterialPropertyBlock();
            var mat = Mats.Cached("ScanRing", () => Mats.Additive(ProcTex.Ring, Color.white, false));
            ringA = MakeRing("ScanRingA", mat, out ringAR);
            ringB = MakeRing("ScanRingB", mat, out ringBR);
        }

        Transform MakeRing(string name, Material mat, out MeshRenderer mr)
        {
            var go = Prims.Part(name, PrimitiveType.Quad, null, Vector3.zero, Vector3.one, mat, new Vector3(90, 0, 0), false);
            mr = go.GetComponent<MeshRenderer>();
            go.SetActive(false);
            return go.transform;
        }

        void OnDestroy()
        {
            if (ringA) Destroy(ringA.gameObject);
            if (ringB) Destroy(ringB.gameObject);
        }

        void Update()
        {
            var input = PlayerInputSource.Current;
            bool uiOpen = owner && owner.inventory && owner.inventory.PanelOpen;
            if (input.scan && owner && owner.CanAct && !uiOpen)
            {
                if (Ready) Pulse();
                else ProcAudio.Play("empty", 0.4f);
            }
            UpdateSweep(Time.deltaTime);
        }

        void Pulse()
        {
            readyAt = Time.time + cooldown;
            sweepT = 0f;
            Vector3 p = transform.position;
            ProcAudio.Play("portal", 0.5f, 0f, 1.6f);
            ProcAudio.Play("ready", 0.4f, 0f, 0.8f);
            GameFeel.Shake(0.08f);
            Vfx.Burst(p + Vector3.up, Cyan, 18);
            int found = 0;
            foreach (var a in Alien.All)
            {
                if (!a || !a.IsAlive) continue;
                if ((a.transform.position - p).sqrMagnitude > radius * radius) continue;
                a.RevealedUntil = Time.time + revealTime;
                found++;
            }
            CratesRevealedUntil = Time.time + revealTime;
            if (Hud.I) Hud.I.WorldText(p + Vector3.up * 2.4f, found > 0 ? $"{found} CONTACT{(found > 1 ? "S" : "")}" : "AREA CLEAR", found > 0 ? new Color(1f, 0.35f, 0.3f) : Cyan, 26);
        }

        void UpdateSweep(float dt)
        {
            if (sweepT < 0f) return;
            sweepT += dt;
            float k = sweepT / 0.9f;
            bool on = k < 1f;
            ringA.gameObject.SetActive(on);
            ringB.gameObject.SetActive(on && k > 0.12f);
            if (!on) { sweepT = -1f; return; }
            Vector3 p = transform.position + Vector3.up * 0.08f;
            float r = Ease.OutCubic(k) * radius;
            ringA.position = p;
            ringA.localScale = Vector3.one * r * 2f;
            mpb.SetColor(ColorId, Mats.HDR(Cyan, 3f) * (1f - k));
            ringAR.SetPropertyBlock(mpb);
            float r2 = Ease.OutCubic(Mathf.Max(0f, k - 0.12f) / 0.88f) * radius * 0.92f;
            ringB.position = p;
            ringB.localScale = Vector3.one * Mathf.Max(0.01f, r2 * 2f);
            mpb.SetColor(ColorId, Mats.HDR(Cyan, 1.5f) * (1f - k));
            ringBR.SetPropertyBlock(mpb);
        }
    }
}
