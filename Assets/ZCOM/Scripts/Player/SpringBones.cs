using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Verlet spring chains for cloth-like secondary motion (the Marshal's cape). Runs after the Animator:
    /// each bone's tail lags behind with inertia, is pulled back toward the animated pose by stiffness,
    /// falls with gravity and is pushed out of a few body spheres so legs never punch through the cloth.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class SpringBones : MonoBehaviour
    {
        public float stiffness = 0.55f;
        public float drag = 0.32f;
        public Vector3 gravity = new Vector3(0f, -2.4f, 0f);

        public struct Sphere { public Transform t; public Vector3 offset; public float radius; }
        public readonly List<Sphere> colliders = new List<Sphere>();

        class Node
        {
            public Transform t;
            public Quaternion restLocal;
            public Vector3 axis;        // local direction to the tail
            public float length;
            public Vector3 tail, prevTail;
        }
        readonly List<List<Node>> chains = new List<List<Node>>();

        public void AddChain(Transform root)
        {
            var chain = new List<Node>();
            var t = root;
            float lastLen = 0.3f;
            while (t)
            {
                Transform child = null;
                foreach (Transform c in t) if (c.name.StartsWith("Cape_")) { child = c; break; }
                var n = new Node { t = t, restLocal = t.localRotation };
                if (child)
                {
                    n.axis = child.localPosition.normalized;
                    n.length = Vector3.Scale(child.localPosition, t.lossyScale).magnitude;
                    lastLen = n.length;
                }
                else
                {
                    n.axis = Vector3.up;
                    n.length = lastLen;
                }
                n.tail = n.prevTail = t.position + t.rotation * n.axis * n.length;
                chain.Add(n);
                t = child;
            }
            chains.Add(chain);
        }

        public void AddCollider(Transform t, Vector3 offset, float radius)
        {
            if (t) colliders.Add(new Sphere { t = t, offset = offset, radius = radius });
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            if (dt <= 0f) return;
            foreach (var chain in chains)
            {
                foreach (var n in chain)
                {
                    // Animated pose for this bone (parent already updated this frame).
                    Quaternion parentRot = n.t.parent ? n.t.parent.rotation : Quaternion.identity;
                    Quaternion restRot = parentRot * n.restLocal;
                    Vector3 head = n.t.position;
                    Vector3 restTail = head + restRot * n.axis * n.length;

                    Vector3 vel = (n.tail - n.prevTail) * (1f - drag);
                    Vector3 next = n.tail + vel + (restTail - n.tail) * stiffness * dt * 18f + gravity * dt * dt * 30f;
                    next = head + (next - head).normalized * n.length;
                    foreach (var c in colliders)
                    {
                        if (!c.t) continue;
                        Vector3 cp = c.t.TransformPoint(c.offset);
                        Vector3 d = next - cp;
                        float r = c.radius + 0.02f;
                        if (d.sqrMagnitude < r * r)
                        {
                            next = cp + d.normalized * r;
                            next = head + (next - head).normalized * n.length;
                        }
                    }
                    n.prevTail = n.tail;
                    n.tail = next;
                    Vector3 from = restRot * n.axis;
                    n.t.rotation = Quaternion.FromToRotation(from, next - head) * restRot;
                }
            }
        }
    }
}
