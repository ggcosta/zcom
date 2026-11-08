using UnityEngine;

namespace Zcom
{
    /// <summary>Slow glowing acid glob. Dodgeable by moving or dashing (i-frames).</summary>
    public class AlienProjectile : MonoBehaviour
    {
        Vector3 velocity;
        float damage;
        float life = 4f;
        Transform glow;
        float trailT;
        static readonly Color Acid = new Color(0.6f, 1f, 0.2f);

        public static void Spawn(Vector3 pos, Vector3 dir, float speed, float damage)
        {
            var go = new GameObject("AcidGlob");
            go.transform.position = pos;
            var p = go.AddComponent<AlienProjectile>();
            p.velocity = dir * speed;
            p.damage = damage;

            var core = Mats.Cached("AcidCore", () => Mats.Lit(Color.black, 0f, 0.9f, Mats.HDR(Acid, 5f)));
            Prims.Part("Core", PrimitiveType.Sphere, go.transform, Vector3.zero, Vector3.one * 0.28f, core, default, false);
            var glowMat = Mats.Cached("AcidGlow", () => Mats.Additive(ProcTex.Glow, Mats.HDR(Acid, 2.5f), false));
            p.glow = Prims.Part("Glow", PrimitiveType.Quad, go.transform, Vector3.zero, Vector3.one * 1.3f, glowMat, default, false).transform;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Acid;
            light.range = 3.5f;
            light.intensity = 3f;
            light.shadows = LightShadows.None;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;
            Vector3 next = pos + velocity * dt;

            var player = PlayerController.I;
            if (player && player.CanAct)
            {
                Vector3 pp = player.transform.position + Vector3.up * 1.1f;
                if (DistanceToSegment(pp, pos, next) < 0.7f && !player.IsDashing)
                {
                    player.health.TakeDamage(new DamageInfo { amount = damage, point = pp, direction = velocity.normalized, knockback = 4f });
                    Splat(pp);
                    return;
                }
            }

            if (Physics.Linecast(pos, next, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !hit.collider.GetComponentInParent<Alien>() && !hit.collider.GetComponentInParent<PlayerController>())
            {
                Splat(hit.point);
                return;
            }

            transform.position = next;
            var cam = CameraRig.I ? CameraRig.I.Cam : Camera.main;
            if (glow && cam) glow.rotation = cam.transform.rotation;

            trailT -= dt;
            if (trailT <= 0f)
            {
                trailT = 0.02f;
                Vfx.Dots(pos + Random.insideUnitSphere * 0.1f, Random.insideUnitSphere * 0.4f, Acid, Random.Range(0.15f, 0.3f), 0.35f);
            }

            life -= dt;
            if (life <= 0f) Destroy(gameObject);
        }

        void Splat(Vector3 at)
        {
            Vfx.Goo(at, Vector3.up, Acid, 12);
            Vfx.Glow(at, Mats.HDR(Acid, 2f), 2f, 0.12f);
            Vfx.Splat(new Vector3(at.x, 0f, at.z), Random.Range(0.8f, 1.2f), new Color(0.45f, 0.75f, 0.1f, 0.8f));
            ProcAudio.Play("spit", 0.35f, 0.15f, 0.7f);
            Destroy(gameObject);
        }

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }
    }
}
