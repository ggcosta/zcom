using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>Plasma grenade: bounces, blinks faster as the fuse runs out, then a big radial blast.</summary>
    public class Grenade : MonoBehaviour
    {
        public float fuse = 1.3f;
        public float radius = 5.5f;
        public float maxDamage = 150f;
        public float minDamage = 40f;
        public float knockback = 14f;
        public float selfDamageScale = 0.25f;

        Rigidbody rb;
        Light blink;
        MeshRenderer glow;
        MaterialPropertyBlock mpb;
        float t;
        bool lastOn;
        bool exploded;
        static PhysicsMaterial bouncy;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public static Grenade Spawn(Vector3 pos, Vector3 velocity, float fuse, float radius)
        {
            var go = new GameObject("Grenade");
            go.layer = 2; // Ignore Raycast: bullets pass through it
            go.transform.position = pos;
            var shell = Mats.Cached("GrenadeShell", () => Mats.Lit(new Color(0.15f, 0.16f, 0.18f), 0.8f, 0.6f));
            var band = Mats.Cached("GrenadeBand", () => Mats.Lit(Color.black, 0f, 0.5f, Mats.HDR(new Color(1f, 0.3f, 0.1f), 2f)));
            Prims.Part("Shell", PrimitiveType.Sphere, go.transform, Vector3.zero, Vector3.one * 0.26f, shell);
            var bandGo = Prims.Part("Band", PrimitiveType.Cylinder, go.transform, Vector3.zero, new Vector3(0.28f, 0.03f, 0.28f), band);

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.13f;
            if (bouncy == null)
                bouncy = new PhysicsMaterial("GrenadeBounce") { bounciness = 0.35f, dynamicFriction = 0.6f, staticFriction = 0.6f, bounceCombine = PhysicsMaterialCombine.Maximum };
            col.sharedMaterial = bouncy;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = velocity;
            rb.angularVelocity = Random.insideUnitSphere * 14f;

            var lightGo = new GameObject("Blink");
            lightGo.transform.SetParent(go.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.35f, 0.15f);
            light.range = 3f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;

            var g = go.AddComponent<Grenade>();
            g.fuse = fuse;
            g.radius = radius;
            g.rb = rb;
            g.blink = light;
            g.glow = bandGo.GetComponent<MeshRenderer>();
            return g;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { bouncy = null; }

        void Awake() { mpb = new MaterialPropertyBlock(); }

        void Update()
        {
            t += Time.deltaTime;
            float remaining = fuse - t;
            float freq = remaining < 0.4f ? 14f : remaining < 0.9f ? 7f : 3.5f;
            bool on = (t * freq) % 1f < 0.5f;
            if (on && !lastOn) ProcAudio.Play("beep", 0.25f, 0f, remaining < 0.4f ? 1.25f : 1f);
            lastOn = on;
            if (blink) blink.intensity = on ? 5f : 0.4f;
            if (glow)
            {
                mpb.SetColor(EmissionId, Mats.HDR(new Color(1f, 0.3f, 0.1f), on ? 8f : 1f));
                glow.SetPropertyBlock(mpb);
            }
            if (on) Vfx.Dots(transform.position, Random.insideUnitSphere * 0.3f, new Color(1f, 0.45f, 0.2f), 0.18f, 0.25f);

            if (t >= fuse) Explode();
        }

        void OnCollisionEnter(Collision c)
        {
            if (c.relativeVelocity.magnitude > 2f) ProcAudio.Play("clink", 0.35f, 0.15f);
            if (rb) rb.linearDamping = 1.2f;
        }

        void Explode()
        {
            if (exploded) return;
            exploded = true;
            Vector3 p = transform.position;
            Vfx.Explosion(p, radius);
            ProcAudio.Play("explosion", 1f, 0.06f);

            var hits = Physics.OverlapSphere(p, radius, ~0, QueryTriggerInteraction.Ignore);
            var done = new HashSet<IDamageable>();
            bool hitAny = false, killedAny = false;
            foreach (var h in hits)
            {
                var d = h.GetComponentInParent<IDamageable>();
                if (d == null || !d.IsAlive || !done.Add(d)) continue;
                Vector3 cp = h.ClosestPoint(p);
                float k = 1f - Mathf.Clamp01(Vector3.Distance(p, cp) / radius);
                float amount = Mathf.Lerp(minDamage, maxDamage, k);
                bool isPlayer = d is PlayerHealth;
                if (isPlayer) amount *= selfDamageScale;
                Vector3 dir = h.transform.position - p;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = Random.insideUnitSphere;
                dir.y = 0f;
                dir.Normalize();
                d.TakeDamage(new DamageInfo
                {
                    amount = amount,
                    point = cp + Vector3.up * 0.8f,
                    direction = dir,
                    knockback = knockback * (0.4f + k),
                    explosive = true,
                    crit = k > 0.7f,
                });
                if (!isPlayer)
                {
                    hitAny = true;
                    if (!d.IsAlive) killedAny = true;
                }
            }

            foreach (var h in Physics.OverlapSphere(p, radius * 1.6f, ~0, QueryTriggerInteraction.Ignore))
            {
                var body = h.attachedRigidbody;
                if (body && body != rb && !body.isKinematic) body.AddExplosionForce(6f, p, radius * 1.6f, 1.5f, ForceMode.Impulse);
            }

            float distToPlayer = PlayerController.I ? Vector3.Distance(PlayerController.I.transform.position, p) : 10f;
            GameFeel.Shake(Mathf.Lerp(0.85f, 0.35f, distToPlayer / 20f));
            GameFeel.HitStop(killedAny ? 0.09f : 0.05f, 0.04f);
            if (hitAny && Hud.I) Hud.I.Hitmarker(killedAny, false);
            Destroy(gameObject);
        }
    }
}
