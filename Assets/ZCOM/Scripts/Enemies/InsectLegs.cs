using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Procedural insect locomotion: every foot stays planted in the world until the body has moved far enough,
    /// then steps in an arc to a predicted spot ahead. Legs step in two alternating groups (tripod / diagonal gait),
    /// and each leg is solved with 2-bone IK (Leg_X_A hip->knee, Leg_X_B knee->foot, Leg_X_B_end at the tip),
    /// knees bending up and outward. Runs after Alien.Animate, so the body's bob, lean and lunges carry through.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class InsectLegs : MonoBehaviour
    {
        public Transform root;            // the alien (ground-level frame, no bob)
        public float stepThreshold = 0.4f;
        public float stepDuration = 0.12f;
        public float stepHeight = 0.15f;
        public float stepLead = 0.15f;    // seconds of velocity added to the landing spot
        public float footstepVolume = 0f;

        class Leg
        {
            public Transform a, b, end;
            public float l1, l2;
            public Vector3 restLocal, planted, from, to;
            public float t = -1f;
            public int group;
            public float side;
        }
        readonly List<Leg> legs = new List<Leg>();
        Vector3 lastPos, vel;
        Transform bodyBone;

        public static InsectLegs Attach(GameObject model, Transform root)
        {
            var c = model.AddComponent<InsectLegs>();
            c.root = root;
            c.Setup(model.transform);
            return c;
        }

        void Setup(Transform model)
        {
            bodyBone = CharacterModels.Find(model, "Body");
            var all = model.GetComponentsInChildren<Transform>(true);
            foreach (var a in all)
            {
                if (!a.name.StartsWith("Leg_") || !a.name.EndsWith("_A")) continue;
                string baseName = a.name.Substring(0, a.name.Length - 2);
                var b = CharacterModels.Find(a, baseName + "_B");
                var end = b ? CharacterModels.Find(b, baseName + "_B_end") : null;
                if (!b || !end) continue;
                var leg = new Leg { a = a, b = b, end = end };
                leg.l1 = Vector3.Distance(a.position, b.position);
                leg.l2 = Vector3.Distance(b.position, end.position);
                leg.side = baseName[4] == 'L' ? 1f : -1f;
                int idx = baseName.Length > 5 ? baseName[5] - '0' : 0;
                leg.group = (idx + (leg.side > 0 ? 0 : 1)) % 2;
                leg.restLocal = root.InverseTransformPoint(end.position);
                leg.planted = Ground(end.position);
                legs.Add(leg);
            }
            lastPos = root.position;
        }

        static Vector3 Ground(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            if (t) p.y = t.SampleHeight(p) + t.transform.position.y;
            return p;
        }

        bool GroupPlanted(int g)
        {
            foreach (var l in legs) if (l.group == g && l.t >= 0f) return false;
            return true;
        }

        void LateUpdate()
        {
            if (!root || legs.Count == 0) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 v = (root.position - lastPos) / dt;
            lastPos = root.position;
            v.y = 0f;
            vel = Vector3.Lerp(vel, v, 1f - Mathf.Exp(-12f * dt));
            float speed = vel.magnitude;
            float dur = stepDuration * Mathf.Clamp(3f / (speed + 1.5f), 0.55f, 1.3f);
            Vector3 center = bodyBone ? bodyBone.position : root.position + Vector3.up;

            foreach (var leg in legs)
            {
                Vector3 desired = Ground(root.TransformPoint(leg.restLocal) + vel * stepLead);
                if (leg.t < 0f)
                {
                    float far = (leg.planted - desired).magnitude;
                    bool must = far > stepThreshold * 2.2f;
                    if ((far > stepThreshold && GroupPlanted(1 - leg.group)) || must)
                    {
                        leg.from = leg.planted;
                        leg.to = Ground(desired + vel * dur * 0.5f);
                        leg.t = 0f;
                    }
                }
                Vector3 target = leg.planted;
                if (leg.t >= 0f)
                {
                    leg.t += dt / dur;
                    float k = Mathf.Clamp01(leg.t);
                    float e = k * k * (3f - 2f * k);
                    target = Vector3.Lerp(leg.from, leg.to, e) + Vector3.up * Mathf.Sin(k * Mathf.PI) * stepHeight;
                    if (leg.t >= 1f)
                    {
                        leg.planted = leg.to;
                        leg.t = -1f;
                        target = leg.planted;
                        if (footstepVolume > 0f) ProcAudio.Play("step", footstepVolume, 0.2f, 1.6f);
                    }
                }
                Solve(leg, target, center);
            }
        }

        void Solve(Leg leg, Vector3 target, Vector3 center)
        {
            Vector3 hip = leg.a.position;
            Vector3 d = target - hip;
            float dist = Mathf.Clamp(d.magnitude, 0.01f, (leg.l1 + leg.l2) * 0.999f);
            Vector3 dn = d.normalized;
            float along = (leg.l1 * leg.l1 + dist * dist - leg.l2 * leg.l2) / (2f * leg.l1 * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, leg.l1 * leg.l1 - along * along));
            Vector3 outward = hip - center; outward.y = 0f;
            Vector3 pole = outward.normalized + Vector3.up * 1.3f;
            pole -= dn * Vector3.Dot(pole, dn);
            if (pole.sqrMagnitude < 1e-6f) pole = Vector3.up;
            Vector3 knee = hip + dn * along + pole.normalized * h;
            leg.a.rotation = Quaternion.FromToRotation(leg.b.position - hip, knee - hip) * leg.a.rotation;
            leg.b.rotation = Quaternion.FromToRotation(leg.end.position - leg.b.position, target - leg.b.position) * leg.b.rotation;
        }
    }
}
