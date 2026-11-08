using UnityEngine;

namespace Zcom
{
    /// <summary>Bio-gel dropped by aliens. Pops out, bobs, gets magnetised to the player when hurt.</summary>
    public class HealthOrb : MonoBehaviour
    {
        public float heal = 20f;
        public float lifetime = 14f;
        public float magnetRange = 4.5f;

        Vector3 velocity;
        float t;
        Transform glow;
        bool grounded;
        static readonly Color Green = new Color(0.3f, 1f, 0.5f);

        public static void Spawn(Vector3 pos)
        {
            var go = new GameObject("HealthOrb");
            go.transform.position = pos;
            var orb = go.AddComponent<HealthOrb>();
            Vector2 r = Random.insideUnitCircle * 2f;
            orb.velocity = new Vector3(r.x, Random.Range(4f, 6f), r.y);

            var core = Mats.Cached("OrbCore", () => Mats.Lit(Color.black, 0f, 0.9f, Mats.HDR(Green, 4f)));
            Prims.Part("Core", PrimitiveType.Sphere, go.transform, Vector3.zero, Vector3.one * 0.3f, core, default, false);
            var cross = Mats.Cached("OrbCross", () => Mats.Lit(Color.white, 0f, 0.9f, Mats.HDR(Color.white, 2f)));
            Prims.Part("CrossA", PrimitiveType.Cube, go.transform, Vector3.zero, new Vector3(0.36f, 0.09f, 0.09f), cross, default, false);
            Prims.Part("CrossB", PrimitiveType.Cube, go.transform, Vector3.zero, new Vector3(0.09f, 0.36f, 0.09f), cross, default, false);
            var glowMat = Mats.Cached("OrbGlow", () => Mats.Additive(ProcTex.Glow, Mats.HDR(Green, 1.5f), false));
            orb.glow = Prims.Part("Glow", PrimitiveType.Quad, go.transform, Vector3.zero, Vector3.one * 1.4f, glowMat, default, false).transform;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Green;
            light.range = 3f;
            light.intensity = 2f;
            light.shadows = LightShadows.None;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            var player = PlayerController.I;
            Vector3 pos = transform.position;

            bool wants = player && player.CanAct && player.health.Health < player.health.maxHealth;
            Vector3 target = player ? player.transform.position + Vector3.up * 1f : pos;
            float dist = Vector3.Distance(target, pos);

            if (wants && dist < magnetRange && t > 0.4f)
            {
                velocity = Vector3.Lerp(velocity, (target - pos).normalized * 14f, 1f - Mathf.Exp(-8f * dt));
                pos += velocity * dt;
                if (dist < 0.8f)
                {
                    if (player.health.Heal(heal))
                    {
                        ProcAudio.Play("pickup", 0.5f, 0f);
                        Vfx.Burst(pos, Green, 16);
                        Destroy(gameObject);
                        return;
                    }
                }
            }
            else if (!grounded)
            {
                velocity += Physics.gravity * dt;
                pos += velocity * dt;
                if (pos.y < 0.5f) { pos.y = 0.5f; velocity *= 0f; grounded = true; }
            }
            else
            {
                pos.y = 0.6f + Mathf.Sin(t * 3f) * 0.12f;
            }

            transform.position = pos;
            transform.rotation = Quaternion.Euler(0f, t * 120f, 0f);
            var cam = CameraRig.I ? CameraRig.I.Cam : Camera.main;
            if (glow && cam) glow.rotation = cam.transform.rotation;

            if (t > lifetime - 2f) transform.localScale = Vector3.one * Mathf.Clamp01((lifetime - t) / 2f);
            if (t > lifetime) Destroy(gameObject);
        }
    }
}
