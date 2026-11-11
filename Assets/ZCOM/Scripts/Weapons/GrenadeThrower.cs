using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Grenade skill. Hold G / right mouse to aim (shows a ballistic arc + blast radius), release to throw.
    /// Charges recharge over time.
    /// </summary>
    public class GrenadeThrower : MonoBehaviour
    {
        public Transform hand;
        public int maxCharges = 2;
        public float rechargeTime = 6f;
        public float maxRange = 14f;
        public float minRange = 2f;
        public float fuseAfterLanding = 0.3f;
        public float explosionRadius = 5.5f;

        PlayerController owner;
        int charges;
        float recharge;
        bool aiming;
        bool wasHeld;
        LineRenderer arc, ring;
        const int ArcPoints = 32;
        const int RingPoints = 56;
        readonly Vector3[] arcBuf = new Vector3[ArcPoints];
        readonly Vector3[] ringBuf = new Vector3[RingPoints];

        public int Charges => charges;
        public int BaseMaxCharges { get; private set; } = 2;
        public bool IsAiming => aiming;
        /// <summary>0..1 progress of the next charge (0 when full).</summary>
        public float RechargeProgress => charges >= maxCharges ? 0f : recharge / rechargeTime;

        void Awake()
        {
            owner = GetComponent<PlayerController>();
            BaseMaxCharges = maxCharges;
            maxCharges += Profile.GrenadeBonus;      // grenade rig upgrade bought at the base
            charges = maxCharges;
            var arcMat = Mats.Cached("GrenadeArc", () => Mats.Additive(null, Mats.HDR(new Color(1f, 0.4f, 0.1f), 2f)));
            var ringMat = Mats.Cached("GrenadeRing", () => Mats.Additive(null, Mats.HDR(new Color(1f, 0.3f, 0.08f), 1.6f)));
            arc = MakeLine("GrenadeArc", arcMat, 0.07f, ArcPoints);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.15f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(1f, 1f) });
            arc.colorGradient = g;
            ring = MakeLine("GrenadeRadius", ringMat, 0.08f, RingPoints);
            ring.loop = true;
            SetVisible(false);
        }

        LineRenderer MakeLine(string name, Material mat, float width, int count)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.useWorldSpace = true;
            lr.positionCount = count;
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        void SetVisible(bool v)
        {
            if (arc) arc.enabled = v;
            if (ring) ring.enabled = v;
        }

        public void CancelAim()
        {
            aiming = false;
            SetVisible(false);
        }

        public void Refill()
        {
            if (charges < maxCharges) ProcAudio.Play("ready", 0.4f, 0f);
            charges = maxCharges;
            recharge = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (charges < maxCharges)
            {
                recharge += dt;
                if (recharge >= rechargeTime)
                {
                    recharge = 0f;
                    charges++;
                    ProcAudio.Play("ready", 0.35f, 0f);
                }
            }

            var input = PlayerInputSource.Current;
            if (!owner || !owner.CanAct || (owner.inventory && owner.inventory.PanelOpen)) { CancelAim(); wasHeld = false; return; }

            bool held = input.grenade;
            if (held && !wasHeld && charges == 0) ProcAudio.Play("empty", 0.5f);

            if (held && charges > 0)
            {
                aiming = true;
                Solve(out Vector3 start, out Vector3 vel, out float flight, out Vector3 target);
                DrawArc(start, vel, flight, target);
                SetVisible(true);
            }
            else if (aiming && !held)
            {
                CancelAim();
                Throw();
            }
            wasHeld = held;
        }

        Vector3 HandPos => hand ? hand.position : transform.position + Vector3.up * 1.5f + transform.forward * 0.5f;

        void Solve(out Vector3 start, out Vector3 vel, out float flight, out Vector3 target)
        {
            start = HandPos;
            Vector3 flat = owner.AimPoint - transform.position;
            flat.y = 0f;
            float dist = Mathf.Clamp(flat.magnitude, minRange, maxRange);
            Vector3 dir = flat.sqrMagnitude > 0.01f ? flat.normalized : transform.forward;
            target = transform.position + dir * dist;
            target.y = 0.15f;
            flight = Mathf.Lerp(0.45f, 1.0f, dist / maxRange);
            Vector3 g = Physics.gravity;
            vel = (target - start - 0.5f * g * flight * flight) / flight;
        }

        void DrawArc(Vector3 start, Vector3 vel, float flight, Vector3 target)
        {
            Vector3 g = Physics.gravity;
            for (int i = 0; i < ArcPoints; i++)
            {
                float t = flight * i / (ArcPoints - 1);
                arcBuf[i] = start + vel * t + 0.5f * g * t * t;
            }
            arc.SetPositions(arcBuf);

            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 10f) * 0.03f;
            for (int i = 0; i < RingPoints; i++)
            {
                float a = i * Mathf.PI * 2f / RingPoints;
                ringBuf[i] = new Vector3(target.x + Mathf.Cos(a) * explosionRadius * pulse, 0.06f, target.z + Mathf.Sin(a) * explosionRadius * pulse);
            }
            ring.SetPositions(ringBuf);
        }

        void Throw()
        {
            if (charges <= 0) return;
            Solve(out Vector3 start, out Vector3 vel, out float flight, out _);
            var g = Grenade.Spawn(start, vel, flight + fuseAfterLanding, explosionRadius);
            var cc = GetComponent<CharacterController>();
            var gc = g.GetComponent<Collider>();
            if (cc && gc) Physics.IgnoreCollision(cc, gc);
            if (charges == maxCharges) recharge = 0f;
            charges--;
            owner.PlayThrow();
            ProcAudio.Play("throw", 0.55f, 0.1f);
        }
    }
}
