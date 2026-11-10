using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// What a rigged alien leaves behind when gunfire kills it: the same model, turned into a physics ragdoll.
    /// The carcass is thrown by the killing shot, tumbles with its legs flailing on joints, comes to rest, curls its
    /// legs in like a dead insect while its glow dies, then melts into the ground after a while.
    /// Explosive kills never get here - those still blow the alien to pieces (Vfx.AlienDeath).
    /// Corpses never block anything: they are on the Ignore Raycast layer (bullets and line-of-sight pass through),
    /// they ignore the Marshal and every living alien, and once at rest they lose their physics altogether.
    /// </summary>
    public class AlienCorpse : MonoBehaviour
    {
        public const int MaxCorpses = 10;
        const int CorpseLayer = 2; // Ignore Raycast
        const float MaxTumbleTime = 4f;

        /// <summary>How many ragdolls have been made this session (read by the smoke test).</summary>
        public static int Spawned { get; private set; }
        /// <summary>Logs where and how each carcass came to rest (smoke test).</summary>
        public static bool LogSettle;
        static readonly List<AlienCorpse> Live = new List<AlienCorpse>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Live.Clear(); Spawned = 0; }

        struct Spec
        {
            public float bodyR, rearR, headR, limbR;   // collider radii, metres
            public float mass, limbMass;
            public float push, lift, spin;             // launch
            public float rest;                         // seconds the carcass lies there before melting
        }

        static Spec SpecFor(AlienType t)
        {
            switch (t)
            {
                case AlienType.Skitter:
                    return new Spec { bodyR = 0.17f, rearR = 0.17f, headR = 0.12f, limbR = 0.05f, mass = 14f, limbMass = 0.6f, push = 3.1f, lift = 3.3f, spin = 7.5f, rest = 9f };
                case AlienType.Spitter:
                    return new Spec { bodyR = 0.26f, rearR = 0.26f, headR = 0.15f, limbR = 0.065f, mass = 22f, limbMass = 0.9f, push = 2.5f, lift = 2.7f, spin = 5.5f, rest = 10f };
                default:
                    return new Spec { bodyR = 0.5f, rearR = 0.46f, headR = 0.27f, limbR = 0.15f, mass = 90f, limbMass = 4f, push = 1.5f, lift = 1.5f, spin = 2.4f, rest = 14f };
            }
        }

        class Limb
        {
            public Transform knee;       // the lower segment, folded once the body is at rest
            public Quaternion from, to;
            public float delay, dur, seed;
            public bool curls;
        }

        enum Phase { Tumble, Rest, Melt }

        Phase phase;
        float age, calm, restT, meltT, restLife;
        float size = 1f;
        Color gooColor;
        Transform core, visual;
        Vector3 startPos;
        Quaternion startRot;
        static PhysicsMaterial limbMat;
        Rigidbody coreRb;
        Vector3 meltFrom, visualScale;
        readonly List<Rigidbody> bodies = new List<Rigidbody>();
        readonly List<Collider> cols = new List<Collider>();
        readonly List<Joint> joints = new List<Joint>();
        readonly List<Limb> limbs = new List<Limb>();

        // The glow is a shared material, so each corpse dims its own copy through a per-slot property block.
        Renderer[] glowRends;
        int[] glowSlots;
        Color glowBase;
        float glow = 1f;
        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        /// <summary>
        /// Turns a freshly killed alien into a ragdoll. Returns false (and changes nothing) when the alien has no
        /// rigged model to work with; the caller then removes it the old way.
        /// </summary>
        public static bool TryCreate(Alien a, DamageInfo info, Vector3 carriedVelocity)
        {
            if (!a || !a.body) return false;
            var ik = a.body.GetComponentInChildren<InsectLegs>();
            if (!ik) return false;
            var core = CharacterModels.Find(ik.transform, "Body");
            if (!core) return false;

            // The newest carcass replaces the oldest one.
            while (Live.Count >= MaxCorpses)
            {
                var old = Live[0];
                Live.RemoveAt(0);
                if (old) old.BeginMelt();
            }

            ik.enabled = false;
            Destroy(ik);

            var c = a.gameObject.AddComponent<AlienCorpse>();
            c.Build(a, ik.transform, core, info, carriedVelocity);
            Live.Add(c);
            Spawned++;
            return true;
        }

        /// <summary>Keeps a newly spawned alien from shoving the carcasses around.</summary>
        public static void IgnoreAll(Collider other)
        {
            if (!other) return;
            foreach (var c in Live)
            {
                if (!c || c.phase != Phase.Tumble) continue;
                foreach (var col in c.cols) if (col && col.enabled) Physics.IgnoreCollision(col, other, true);
            }
        }

        void Build(Alien a, Transform model, Transform coreBone, DamageInfo info, Vector3 carried)
        {
            var spec = SpecFor(a.type);
            size = a.deathSize;
            gooColor = a.gooColor;
            restLife = spec.rest;
            core = coreBone;
            visual = a.body;
            gameObject.name += "Corpse";

            // Whatever squash, spawn-pop or hit flash was in progress stops here.
            visual.localScale = Vector3.one;
            visualScale = Vector3.one;
            mpb = new MaterialPropertyBlock();
            var rends = visual.GetComponentsInChildren<Renderer>();
            var gr = new List<Renderer>();
            var gs = new List<int>();
            foreach (var r in rends)
            {
                r.SetPropertyBlock(null);
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (a.glowMaterial && mats[i] == a.glowMaterial) { gr.Add(r); gs.Add(i); }
            }
            glowRends = gr.ToArray();
            glowSlots = gs.ToArray();
            if (a.glowMaterial && a.glowMaterial.HasProperty(EmissionId)) glowBase = a.glowMaterial.GetColor(EmissionId);

            // Body: one rigid lump (thorax, abdomen, head) that everything else hangs from.
            coreRb = AddBody(core, spec.mass);
            AddSphere(core, core.position, spec.bodyR);
            var abdomen = CharacterModels.Find(core, "Abdomen");
            if (abdomen) AddSphere(core, core.position + (abdomen.position - core.position) * 2f, spec.rearR);
            var head = CharacterModels.Find(core, "Head");
            if (head)
            {
                var mouth = CharacterModels.Find(head, "Mouth");
                AddSphere(core, mouth ? Vector3.Lerp(head.position, mouth.position, 0.4f) : head.position, spec.headR);
            }

            // Legs and claws: two jointed segments each.
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                bool leg = t.name.StartsWith("Leg_"), claw = t.name.StartsWith("Claw_");
                if ((!leg && !claw) || !t.name.EndsWith("_A")) continue;
                string stem = t.name.Substring(0, t.name.Length - 2);
                var lower = CharacterModels.Find(t, stem + "_B");
                if (!lower) continue;
                var tip = lower.childCount > 0 ? lower.GetChild(0) : null;

                Vector3 hinge = tip ? Vector3.Cross(lower.position - t.position, tip.position - lower.position) : Vector3.zero;
                if (hinge.sqrMagnitude < 1e-8f) hinge = Vector3.Cross(lower.position - t.position, Vector3.up);
                if (hinge.sqrMagnitude < 1e-8f) hinge = Vector3.right;
                hinge.Normalize();

                var upper = AddBody(t, spec.limbMass);
                AddCapsule(t, lower.position, spec.limbR);
                AddJoint(upper, coreRb, hinge, lower.position - t.position, 85f, 50f);

                var low = AddBody(lower, spec.limbMass);
                Vector3 end = tip ? tip.position : lower.position + (lower.position - t.position) * 0.8f;
                AddCapsule(lower, end, spec.limbR * (claw ? 1.1f : 0.85f));
                AddJoint(low, upper, hinge, end - lower.position, 95f, 14f);

                limbs.Add(new Limb
                {
                    knee = lower, curls = leg && tip,
                    delay = Random.Range(0f, 0.45f), dur = Random.Range(0.7f, 1.3f), seed = Random.Range(0f, 6.28f),
                });
            }

            // A carcass only ever touches the world, loose debris and other carcasses.
            for (int i = 0; i < cols.Count; i++)
                for (int j = i + 1; j < cols.Count; j++)
                    Physics.IgnoreCollision(cols[i], cols[j], true);
            var player = PlayerController.I ? PlayerController.I.GetComponent<Collider>() : null;
            if (player) foreach (var col in cols) Physics.IgnoreCollision(col, player, true);
            foreach (var other in Alien.All)
            {
                var oc = other ? other.BodyCollider : null;
                if (!oc || !oc.enabled) continue;
                foreach (var col in cols) Physics.IgnoreCollision(col, oc, true);
            }

            // Thrown by the killing shot, tumbling over backwards.
            Vector3 dir = info.direction; dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = -transform.forward;
            dir.Normalize();
            carried.y = 0f;
            float power = info.crit ? 1.3f : info.quiet ? 0.6f : 1f;
            Vector3 v0 = dir * (spec.push * power + Mathf.Min(info.knockback, 8f) * 0.35f) + Vector3.ClampMagnitude(carried, 6f) * 0.6f;
            v0 = Vector3.ClampMagnitude(v0, 6f) + Vector3.up * (spec.lift * Mathf.Lerp(0.7f, 1.15f, Random.value));
            Vector3 w = Vector3.Cross(Vector3.up, dir) * (spec.spin * Random.Range(0.7f, 1.3f) * power) + Random.insideUnitSphere * (spec.spin * 0.35f);
            Vector3 pivot = core.position;
            startPos = pivot;
            startRot = core.rotation;
            coreRb.angularDamping = 0.5f;
            foreach (var rb in bodies)
            {
                rb.linearVelocity = v0 + Vector3.Cross(w, rb.position - pivot);
                rb.angularVelocity = w;
            }
        }

        Rigidbody AddBody(Transform t, float mass)
        {
            t.gameObject.layer = CorpseLayer;
            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 1.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.maxDepenetrationVelocity = 3f;
            rb.maxAngularVelocity = 20f;
            rb.solverIterations = 12;
            rb.solverVelocityIterations = 4;
            bodies.Add(rb);
            return rb;
        }

        static float ScaleOf(Transform t)
        {
            Vector3 s = t.lossyScale;
            return Mathf.Max(1e-4f, Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))));
        }

        void AddSphere(Transform t, Vector3 worldCenter, float worldRadius)
        {
            var s = t.gameObject.AddComponent<SphereCollider>();
            s.center = t.InverseTransformPoint(worldCenter);
            s.radius = worldRadius / ScaleOf(t);
            cols.Add(s);
        }

        /// <summary>A capsule from the bone's origin to <paramref name="worldEnd"/>, along the bone's nearest local axis.</summary>
        void AddCapsule(Transform bone, Vector3 worldEnd, float worldRadius)
        {
            Vector3 p = bone.InverseTransformPoint(worldEnd);
            float ax = Mathf.Abs(p.x), ay = Mathf.Abs(p.y), az = Mathf.Abs(p.z);
            var c = bone.gameObject.AddComponent<CapsuleCollider>();
            c.direction = ax > ay ? (ax > az ? 0 : 2) : (ay > az ? 1 : 2);
            c.center = p * 0.5f;
            c.radius = worldRadius / ScaleOf(bone);
            c.height = Mathf.Max(p.magnitude, c.radius * 2f);
            // Dead legs are limp: they slide along the ground instead of propping the body up.
            if (!limbMat)
                limbMat = new PhysicsMaterial("CorpseLimb")
                {
                    dynamicFriction = 0.15f, staticFriction = 0.15f, bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum,
                };
            c.sharedMaterial = limbMat;
            cols.Add(c);
        }

        /// <summary>Mostly a hinge about <paramref name="worldHinge"/> (the leg's bending plane), with a little slack.</summary>
        void AddJoint(Rigidbody rb, Rigidbody parent, Vector3 worldHinge, Vector3 worldAlong, float bend, float slack)
        {
            var j = rb.gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = parent;
            j.anchor = Vector3.zero;
            j.axis = rb.transform.InverseTransformDirection(worldHinge).normalized;
            j.swingAxis = rb.transform.InverseTransformDirection(worldAlong).normalized;
            j.lowTwistLimit = new SoftJointLimit { limit = -bend };
            j.highTwistLimit = new SoftJointLimit { limit = bend };
            j.swing1Limit = new SoftJointLimit { limit = slack };
            j.swing2Limit = new SoftJointLimit { limit = slack };
            j.enableProjection = true;
            j.enablePreprocessing = false;
            joints.Add(j);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;

            // The glow goes out over the first couple of seconds.
            if (glow > 0.06f && glowRends != null && glowRends.Length > 0)
            {
                glow = Mathf.Max(0.06f, 1f - Ease.OutCubic(Mathf.Clamp01(age / 2.2f)) * 0.94f);
                mpb.SetColor(EmissionId, glowBase * glow);
                for (int i = 0; i < glowRends.Length; i++)
                    if (glowRends[i]) glowRends[i].SetPropertyBlock(mpb, glowSlots[i]);
            }

            switch (phase)
            {
                case Phase.Tumble:
                    if (!core || core.position.y < -40f) { Destroy(gameObject); return; }
                    // Limp legs slide easily, so the ground takes the speed off instead (keeps the carcass near the kill).
                    float drag = Mathf.Lerp(0.05f, 3.2f, Mathf.Clamp01((age - 0.35f) / 0.9f));
                    foreach (var rb in bodies) if (rb) rb.linearDamping = drag;
                    bool slow = coreRb.angularVelocity.sqrMagnitude < 0.6f;
                    for (int i = 0; slow && i < bodies.Count; i++)
                        if (bodies[i] && bodies[i].linearVelocity.sqrMagnitude > 0.12f) slow = false;
                    calm = slow ? calm + dt : 0f;
                    if ((age > 0.9f && calm > 0.35f) || age > MaxTumbleTime) Settle();
                    break;

                case Phase.Rest:
                    restT += dt;
                    foreach (var l in limbs)
                    {
                        if (!l.curls || !l.knee) continue;
                        float k = Mathf.Clamp01((restT - l.delay) / l.dur);
                        float twitch = 0.14f * Mathf.Sin(restT * 31f + l.seed) * Mathf.Exp(-restT * 2.2f);
                        l.knee.localRotation = Quaternion.SlerpUnclamped(l.from, l.to, k * k * (3f - 2f * k) + twitch);
                    }
                    if (restT > restLife) BeginMelt();
                    break;

                case Phase.Melt:
                    meltT += dt;
                    float m = Mathf.Clamp01(meltT / 1.8f);
                    float e = m * m;
                    transform.position = meltFrom + Vector3.down * (0.3f * size * e);
                    if (visual) visual.localScale = new Vector3(visualScale.x, Mathf.Lerp(visualScale.y, visualScale.y * 0.2f, e), visualScale.z);
                    if (core && Random.value < dt * 26f)
                    {
                        Vector3 p = core.position + Random.insideUnitSphere * (0.45f * size);
                        Vfx.Dots(p, Vector3.up * Random.Range(0.3f, 1.2f) + Random.insideUnitSphere * 0.4f, gooColor, Random.Range(0.1f, 0.22f) * size, Random.Range(0.3f, 0.6f));
                    }
                    if (m >= 1f) Destroy(gameObject);
                    break;
            }
        }

        /// <summary>The body has come to rest: drop the physics and let the legs curl in.</summary>
        void Settle()
        {
            if (phase != Phase.Tumble) return;
            phase = Phase.Rest;
            if (LogSettle && core)
            {
                Vector3 d = core.position - startPos;
                float upright = Vector3.Dot(core.rotation * (Quaternion.Inverse(startRot) * Vector3.up), Vector3.up);
                Debug.Log($"[corpse] {name} age={age:0.00} travel={new Vector2(d.x, d.z).magnitude:0.00} dy={d.y:0.00} upright={upright:0.00}");
            }
            foreach (var j in joints) if (j) Destroy(j);
            joints.Clear();
            foreach (var rb in bodies)
            {
                if (!rb) continue;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }
            foreach (var c in cols) if (c) c.enabled = false;

            foreach (var l in limbs)
            {
                if (!l.curls || !l.knee || !l.knee.parent || l.knee.childCount == 0) { l.curls = false; continue; }
                Vector3 toFoot = l.knee.GetChild(0).position - l.knee.position;
                Vector3 toHip = l.knee.parent.position - l.knee.position;
                Vector3 axis = Vector3.Cross(toFoot, toHip);
                float room = Vector3.Angle(toFoot, toHip) - 28f;
                if (axis.sqrMagnitude < 1e-8f || room <= 2f) { l.curls = false; continue; }
                float fold = Mathf.Min(Random.Range(26f, 44f), room);
                l.from = l.knee.localRotation;
                l.to = Quaternion.Inverse(l.knee.parent.rotation) * (Quaternion.AngleAxis(fold, axis.normalized) * l.knee.rotation);
            }
        }

        void BeginMelt()
        {
            if (phase == Phase.Melt) return;
            Settle();
            phase = Phase.Melt;
            meltT = 0f;
            meltFrom = transform.position;
            if (visual) visualScale = visual.localScale;
            Live.Remove(this);
            if (core)
            {
                Color decal = gooColor * 0.6f; decal.a = 0.85f;
                Vfx.Splat(core.position, Random.Range(1.3f, 1.8f) * size, decal, 30f);
            }
        }

        void OnDestroy() { Live.Remove(this); }
    }
}
