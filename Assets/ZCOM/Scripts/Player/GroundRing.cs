using UnityEngine;

namespace Zcom
{
    /// <summary>Soft cyan selection ring under the operative's feet (helps reading position from the iso camera).</summary>
    public class GroundRing : MonoBehaviour
    {
        public float radius = 0.85f;
        public Color color = new Color(0.25f, 0.88f, 0.94f);
        Transform ring, tick;
        MeshRenderer ringR, tickR;
        MaterialPropertyBlock mpb;
        PlayerController pc;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            pc = GetComponent<PlayerController>();
            mpb = new MaterialPropertyBlock();
            var mat = Mats.Cached("GroundRing", () => Mats.Additive(ProcTex.RingThin, Color.white, false));
            var glow = Mats.Cached("GroundRingGlow", () => Mats.Additive(ProcTex.Glow, Color.white, false));
            ring = Prims.Part("GroundRing", PrimitiveType.Quad, null, Vector3.zero, Vector3.one * radius * 2f, mat, new Vector3(90, 0, 0), false).transform;
            tick = Prims.Part("GroundRingAim", PrimitiveType.Quad, null, Vector3.zero, new Vector3(0.22f, 0.5f, 1f), glow, new Vector3(90, 0, 0), false).transform;
            ringR = ring.GetComponent<MeshRenderer>();
            tickR = tick.GetComponent<MeshRenderer>();
        }

        void OnDestroy()
        {
            if (ring) Destroy(ring.gameObject);
            if (tick) Destroy(tick.gameObject);
        }

        void LateUpdate()
        {
            bool alive = !pc || pc.CanAct;
            ring.gameObject.SetActive(alive);
            tick.gameObject.SetActive(alive);
            if (!alive) return;
            Vector3 p = transform.position + Vector3.up * 0.04f;
            float pulse = 1f + Mathf.Sin(Time.time * 3f) * 0.03f;
            ring.SetPositionAndRotation(p, Quaternion.Euler(90f, Time.time * 20f, 0f));
            ring.localScale = Vector3.one * radius * 2f * pulse;
            Vector3 aim = pc ? pc.AimDir : transform.forward;
            tick.SetPositionAndRotation(p + aim * (radius + 0.12f), Quaternion.LookRotation(Vector3.down, aim));
            mpb.SetColor(ColorId, Mats.HDR(color, 1.4f) * 0.8f);
            ringR.SetPropertyBlock(mpb);
            mpb.SetColor(ColorId, Mats.HDR(color, 2f));
            tickR.SetPropertyBlock(mpb);
        }
    }
}
