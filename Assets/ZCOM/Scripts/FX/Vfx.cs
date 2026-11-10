using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// All transient effects, pooled. World-space particle systems driven by Emit() plus ring-buffer pools of
    /// tracers, billboards, flat rings, decals, point lights and physics debris (casings, gibs, magazines).
    /// </summary>
    public class Vfx : MonoBehaviour
    {
        static Vfx inst;
        public static Vfx I
        {
            get
            {
                if (inst == null) inst = new GameObject("Vfx").AddComponent<Vfx>();
                return inst;
            }
        }

        class RingPool<T> where T : class
        {
            readonly List<T> items = new List<T>();
            readonly int cap;
            readonly System.Func<T> make;
            int cursor;
            public RingPool(int cap, System.Func<T> make) { this.cap = cap; this.make = make; }
            public List<T> Items => items;
            public T Next()
            {
                if (items.Count < cap) { var t = make(); items.Add(t); return t; }
                var r = items[cursor];
                cursor = (cursor + 1) % cap;
                return r;
            }
        }

        class TracerFx { public LineRenderer lr; public Vector3 a, b; public float t, life, width; public Color color; public bool active; }
        class Quad { public Transform tr; public MeshRenderer mr; public Vector3 pos; public float t, life, size0, size1, roll; public Color color; public bool active, billboard, fadeIn, alphaBlend; }
        class FxLight { public Light light; public float t, life, intensity; public bool active; }
        class DebrisFx { public Transform tr; public Rigidbody rb; public MeshFilter mf; public MeshRenderer mr; public float t, life; public Vector3 scale; public bool active; }

        ParticleSystem sparks, goo, dots, fire, smoke, embers;
        Material matFlash, matGlow, matRing, matDisc, matDecal, matTracer;
        Material matBrass, matShell, matShrapnel;
        RingPool<TracerFx> tracers;
        RingPool<Quad> billboards, flats, decals;
        RingPool<FxLight> lights;
        RingPool<DebrisFx> debris;
        MaterialPropertyBlock mpb;
        PhysicsMaterial debrisPhys;
        float decalLayer;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Color Warm = new Color(1f, 0.75f, 0.4f);

        class BoltFx { public LineRenderer lr; public Vector3 a, dir; public float dist, t, speed, length, width; public Color color; public bool active; }
        RingPool<BoltFx> bolts;
        Material matBolt;

        void Awake()
        {
            if (inst != null && inst != this) { Destroy(gameObject); return; }
            inst = this;
            mpb = new MaterialPropertyBlock();

            matFlash = Mats.Additive(ProcTex.Flash, Color.white, false);
            matGlow = Mats.Additive(ProcTex.Glow, Color.white, false);
            matRing = Mats.Additive(ProcTex.Ring, Color.white, false);
            matDisc = Mats.Additive(ProcTex.Disc, Color.white, false);
            matDecal = Mats.Alpha(ProcTex.Splat, Color.white, false);
            matTracer = Mats.Additive(ProcTex.Glow, Mats.HDR(Color.white, 5f), true);
            matTracer.SetTextureScale("_MainTex", new Vector2(1f, 1f));
            matBrass = Mats.Lit(new Color(0.85f, 0.62f, 0.25f), 0.9f, 0.7f);
            matShell = Mats.Lit(new Color(0.75f, 0.12f, 0.08f), 0.3f, 0.6f);
            matShrapnel = Mats.Lit(new Color(0.1f, 0.1f, 0.1f), 0.5f, 0.4f, Mats.HDR(new Color(1f, 0.45f, 0.1f), 3f));
            debrisPhys = new PhysicsMaterial("Debris") { bounciness = 0.35f, dynamicFriction = 0.5f, staticFriction = 0.6f, bounceCombine = PhysicsMaterialCombine.Maximum };

            var fade = Gradient2(Color.white, 1f, Color.white, 0f);
            sparks = MakeSystem("Sparks", Mats.Additive(ProcTex.Glow, Mats.HDR(Color.white, 5f)), 1200, 1.4f,
                ParticleSystemRenderMode.Stretch, Curve(1f, 0f), fade, 0.6f);
            goo = MakeSystem("Goo", Mats.Alpha(ProcTex.Blob, Color.white), 800, 2.4f,
                ParticleSystemRenderMode.Billboard, Curve(1f, 0.35f), Gradient2(Color.white, 1f, Color.white, 0.0f), 0f);
            dots = MakeSystem("GlowDots", Mats.Additive(ProcTex.Glow, Mats.HDR(Color.white, 3f)), 1500, 0f,
                ParticleSystemRenderMode.Billboard, Curve(1f, 0f), fade, 1.5f);
            var fireGrad = new Gradient();
            fireGrad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.7f, 0.3f), 0.25f), new GradientColorKey(new Color(1f, 0.25f, 0.05f), 0.7f), new GradientColorKey(new Color(0.3f, 0.05f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fire = MakeSystem("Fire", Mats.Additive(ProcTex.Smoke, Mats.HDR(Color.white, 4f)), 400, -0.2f,
                ParticleSystemRenderMode.Billboard, CurveK(0.6f, 1f, 0.5f), fireGrad, 3f);
            smoke = MakeSystem("Smoke", Mats.Alpha(ProcTex.Smoke, Color.white), 400, -0.05f,
                ParticleSystemRenderMode.Billboard, Curve(0.6f, 1.6f), Gradient3Alpha(0f, 1f, 0f), 1.8f);
            embers = MakeSystem("Embers", Mats.Additive(ProcTex.Glow, Mats.HDR(new Color(1f, 0.55f, 0.2f), 5f)), 600, 0.25f,
                ParticleSystemRenderMode.Stretch, Curve(1f, 0f), fade, 1f);

            tracers = new RingPool<TracerFx>(48, MakeTracer);
            matBolt = Mats.Additive(ProcTex.BeamStrip, Mats.HDR(Color.white, 3.2f), true);
            bolts = new RingPool<BoltFx>(96, MakeBolt);
            billboards = new RingPool<Quad>(64, () => MakeQuad("Billboard"));
            flats = new RingPool<Quad>(32, () => MakeQuad("Flat"));
            decals = new RingPool<Quad>(200, () => MakeQuad("Decal"));   // blood stains linger, so the pool is deep
            lights = new RingPool<FxLight>(14, MakeLight);
            debris = new RingPool<DebrisFx>(280, MakeDebris);
        }

        // ------------------------------------------------------------------ builders

        static AnimationCurve Curve(float a, float b) => AnimationCurve.EaseInOut(0f, a, 1f, b);
        static AnimationCurve CurveK(float a, float mid, float b) => new AnimationCurve(new Keyframe(0f, a), new Keyframe(0.3f, mid), new Keyframe(1f, b));

        static Gradient Gradient2(Color c0, float a0, Color c1, float a1)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c0, 0f), new GradientColorKey(c1, 1f) }, new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a1, 1f) });
            return g;
        }

        static Gradient Gradient3Alpha(float a0, float a1, float a2)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a1, 0.12f), new GradientAlphaKey(a2, 1f) });
            return g;
        }

        ParticleSystem MakeSystem(string name, Material mat, int max, float gravity, ParticleSystemRenderMode mode,
            AnimationCurve size, Gradient color, float drag)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(color);
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, size);
            if (drag > 0f)
            {
                var lv = ps.limitVelocityOverLifetime;
                lv.enabled = true;
                lv.limit = 1000f;
                lv.drag = drag;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = mode;
            r.velocityScale = 0.04f;
            r.lengthScale = 1.2f;
            r.maxParticleSize = 3f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        TracerFx MakeTracer()
        {
            var go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = matTracer;
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return new TracerFx { lr = lr };
        }

        BoltFx MakeBolt()
        {
            var go = new GameObject("Bolt");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = matBolt;
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 3;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return new BoltFx { lr = lr };
        }

        Quad MakeQuad(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Prims.Mesh(PrimitiveType.Quad);
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return new Quad { tr = go.transform, mr = mr };
        }

        FxLight MakeLight()
        {
            var go = new GameObject("FxLight");
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.shadows = LightShadows.None;
            l.enabled = false;
            return new FxLight { light = l };
        }

        DebrisFx MakeDebris()
        {
            var go = new GameObject("Debris");
            go.layer = 2; // Ignore Raycast: never blocks bullets
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var col = go.AddComponent<BoxCollider>();
            col.sharedMaterial = debrisPhys;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.1f;
            rb.linearDamping = 0.1f;
            rb.angularDamping = 0.2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            go.SetActive(false);
            return new DebrisFx { tr = go.transform, rb = rb, mf = mf, mr = mr };
        }

        // ------------------------------------------------------------------ emit helpers

        static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color c)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize = size,
                startLifetime = life,
                startColor = c,
                rotation = Random.Range(0f, 360f),
            };
            ps.Emit(ep, 1);
        }

        Quad Billboard(Material mat, Vector3 pos, float size0, float size1, float life, Color color)
        {
            var q = billboards.Next();
            q.mr.sharedMaterial = mat;
            q.pos = pos; q.t = 0f; q.life = life; q.size0 = size0; q.size1 = size1; q.color = color;
            q.roll = Random.Range(0f, 360f); q.billboard = true; q.fadeIn = false; q.alphaBlend = false; q.active = true;
            q.tr.gameObject.SetActive(true);
            UpdateQuad(q, 0f);
            return q;
        }

        Quad Flat(RingPool<Quad> pool, Material mat, Vector3 pos, float size0, float size1, float life, Color color, bool fadeIn, bool alphaBlend)
        {
            var q = pool.Next();
            q.mr.sharedMaterial = mat;
            q.pos = pos; q.t = 0f; q.life = life; q.size0 = size0; q.size1 = size1; q.color = color;
            q.roll = Random.Range(0f, 360f); q.billboard = false; q.fadeIn = fadeIn; q.alphaBlend = alphaBlend; q.active = true;
            q.tr.gameObject.SetActive(true);
            UpdateQuad(q, 0f);
            return q;
        }

        // ------------------------------------------------------------------ public API

        public static void MuzzleFlash(Vector3 pos, Vector3 dir) => MuzzleFlash(pos, dir, 1f, new Color(1f, 0.78f, 0.45f));

        public static void MuzzleFlash(Vector3 pos, Vector3 dir, float scale, Color color)
        {
            var v = I;
            v.Billboard(v.matFlash, pos + dir * 0.15f * scale, Random.Range(0.9f, 1.3f) * scale, Random.Range(0.5f, 0.8f) * scale, 0.05f, Mats.HDR(color, 6f));
            v.Billboard(v.matGlow, pos, 2.2f * scale, 0.8f * scale, 0.07f, Mats.HDR(color * 0.85f, 1.6f));
            LightFlash(pos + Vector3.up * 0.2f, color, 3f * scale, 4.5f * scale, 0.06f);
            int sparks = Mathf.RoundToInt(4 * scale * scale);
            for (int i = 0; i < sparks; i++)
                Emit(v.sparks, pos, (dir + Random.insideUnitSphere * 0.35f * scale).normalized * Random.Range(9f, 16f), Random.Range(0.04f, 0.08f), Random.Range(0.06f, 0.14f), color);
            for (int i = 0; i < Mathf.CeilToInt(scale); i++)
                Emit(v.smoke, pos + dir * 0.3f, dir * 1.5f * scale + Vector3.up * 0.6f + Random.insideUnitSphere * 0.3f, Random.Range(0.35f, 0.6f) * scale, Random.Range(0.35f, 0.55f) * scale, new Color(0.75f, 0.75f, 0.8f, 0.18f));
        }

        public static void Tracer(Vector3 a, Vector3 b, Color color) => Tracer(a, b, color, 0.09f, 1f);

        /// <summary>Bullet streak. lifeMultiplier &gt; 1 leaves a lingering beam (rail).</summary>
        public static void Tracer(Vector3 a, Vector3 b, Color color, float width, float lifeMultiplier)
        {
            var tr = I.tracers.Next();
            tr.a = a; tr.b = b; tr.t = 0f; tr.life = (0.07f + Vector3.Distance(a, b) * 0.0015f) * lifeMultiplier; tr.color = color; tr.active = true;
            tr.width = width;
            tr.lr.enabled = true;
            tr.lr.SetPosition(0, a);
            tr.lr.SetPosition(1, b);
            tr.lr.widthMultiplier = width;
        }

        /// <summary>Visible plasma bolt flying from a to b (purely visual; damage is applied by the gun on arrival).</summary>
        public static void Bolt(Vector3 a, Vector3 b, Color color, float width, float length, float speed)
        {
            var bo = I.bolts.Next();
            Vector3 d = b - a;
            bo.dist = d.magnitude;
            bo.dir = bo.dist > 0.001f ? d / bo.dist : Vector3.forward;
            bo.a = a; bo.t = 0f; bo.speed = speed; bo.length = length; bo.width = width; bo.color = color; bo.active = true;
            bo.lr.enabled = true;
            bo.lr.widthMultiplier = width;
            Color head = Color.Lerp(color, Color.white, 0.2f);
            bo.lr.startColor = new Color(color.r, color.g, color.b, 0.05f);
            bo.lr.endColor = head;
            bo.lr.SetPosition(0, a);
            bo.lr.SetPosition(1, a);
        }

        /// <summary>Energy weapon muzzle: coloured flash, halo ring, light and a few sparks. No smoke.</summary>
        public static void EnergyMuzzle(Vector3 pos, Vector3 dir, Color color, float scale)
        {
            var v = I;
            v.Billboard(v.matGlow, pos, 1.6f * scale, 0.6f * scale, 0.07f, Mats.HDR(color, 3f));
            v.Billboard(v.matFlash, pos + dir * 0.1f, 0.7f * scale, 0.35f * scale, 0.04f, Mats.HDR(Color.Lerp(color, Color.white, 0.6f), 5f));
            v.Flat(v.flats, v.matRing, pos, 0.15f * scale, 0.9f * scale, 0.12f, Mats.HDR(color, 2.5f), false, false);
            LightFlash(pos + Vector3.up * 0.2f, color, 3.5f * scale, 5f * scale, 0.06f);
            int n = Mathf.RoundToInt(3 * scale * scale);
            for (int i = 0; i < n; i++)
                Emit(v.sparks, pos, (dir + Random.insideUnitSphere * 0.5f).normalized * Random.Range(6f, 12f), Random.Range(0.03f, 0.06f), Random.Range(0.06f, 0.14f), Color.Lerp(color, Color.white, 0.4f));
        }

        /// <summary>Plasma splash where a bolt lands.</summary>
        public static void EnergyImpact(Vector3 p, Vector3 normal, Color color, float scale)
        {
            var v = I;
            Color light = Color.Lerp(color, Color.white, 0.4f);
            for (int i = 0; i < Mathf.RoundToInt(9 * scale); i++)
            {
                Vector3 d = (normal * 0.8f + Random.insideUnitSphere).normalized;
                Emit(v.sparks, p, d * Random.Range(3f, 9f), Random.Range(0.03f, 0.06f), Random.Range(0.12f, 0.3f), light);
            }
            for (int i = 0; i < 3; i++)
                Emit(v.dots, p, normal * Random.Range(0.5f, 2f) + Random.insideUnitSphere, Random.Range(0.12f, 0.22f), Random.Range(0.15f, 0.3f), color);
            v.Billboard(v.matGlow, p + normal * 0.05f, 0.5f * scale, 1.3f * scale, 0.1f, Mats.HDR(color, 2.5f));
            LightFlash(p + normal * 0.3f, color, 2.5f * scale, 3.5f, 0.07f);
        }

        /// <summary>Continuous beam contact point (call every frame while the beam touches something).</summary>
        public static void BeamBurn(Vector3 p, Vector3 normal, Color color)
        {
            var v = I;
            Color light = Color.Lerp(color, Color.white, 0.5f);
            for (int i = 0; i < 2; i++)
                Emit(v.sparks, p, (normal + Random.insideUnitSphere).normalized * Random.Range(3f, 8f), Random.Range(0.03f, 0.05f), Random.Range(0.1f, 0.25f), light);
            if (Random.value < 0.5f) Emit(v.dots, p, normal + Vector3.up * Random.Range(0.5f, 1.5f), Random.Range(0.15f, 0.3f), Random.Range(0.2f, 0.4f), color);
            if (Random.value < 0.12f) Emit(v.smoke, p, Vector3.up * Random.Range(0.6f, 1.4f), Random.Range(0.4f, 0.7f), Random.Range(0.6f, 1f), new Color(0.3f, 0.28f, 0.35f, 0.3f));
            v.Billboard(v.matGlow, p, Random.Range(0.8f, 1.2f), 0.6f, 0.04f, Mats.HDR(color, 2.5f));
        }

        /// <summary>One puff of a burning fire (call repeatedly).</summary>
        public static void Flame(Vector3 p, float intensity)
        {
            var v = I;
            Vector2 j = Random.insideUnitCircle * 0.3f;
            Emit(v.fire, p + new Vector3(j.x, 0.1f, j.y), Vector3.up * Random.Range(1.2f, 2.4f) + Random.insideUnitSphere * 0.3f,
                Random.Range(0.5f, 0.9f) * intensity, Random.Range(0.35f, 0.6f), Color.Lerp(new Color(1f, 0.7f, 0.3f), new Color(1f, 0.4f, 0.15f), Random.value));
            if (Random.value < 0.35f)
                Emit(v.smoke, p + Vector3.up * 0.8f, Vector3.up * Random.Range(1f, 2f) + Random.insideUnitSphere * 0.4f,
                    Random.Range(1f, 1.8f) * intensity, Random.Range(1.8f, 3f), new Color(0.08f, 0.07f, 0.08f, 0.55f));
            if (Random.value < 0.25f)
                Emit(v.embers, p + Vector3.up * 0.4f, (Vector3.up * 2f + Random.insideUnitSphere).normalized * Random.Range(1f, 3f), Random.Range(0.04f, 0.08f), Random.Range(0.8f, 1.6f), Color.white);
        }

        /// <summary>Hot plasma vapour puffing out of a weapon vent.</summary>
        public static void Vent(Vector3 p, Vector3 dir, Color color)
        {
            var v = I;
            for (int i = 0; i < 6; i++)
                Emit(v.dots, p, (dir.normalized + Random.insideUnitSphere * 0.4f) * Random.Range(1f, 2.5f), Random.Range(0.08f, 0.16f), Random.Range(0.2f, 0.45f), color);
            Emit(v.smoke, p, dir.normalized * 1.2f + Vector3.up * 0.5f, Random.Range(0.25f, 0.45f), Random.Range(0.4f, 0.7f), new Color(0.8f, 0.8f, 0.9f, 0.15f));
        }

        public static void ImpactSparks(Vector3 p, Vector3 normal, Vector3 dir)
        {
            var v = I;
            Vector3 refl = Vector3.Reflect(dir, normal);
            for (int i = 0; i < 10; i++)
            {
                Vector3 d = (refl * 0.6f + normal * 0.6f + Random.insideUnitSphere * 0.7f).normalized;
                Emit(v.sparks, p, d * Random.Range(4f, 12f), Random.Range(0.03f, 0.07f), Random.Range(0.12f, 0.35f), Warm);
            }
            for (int i = 0; i < 2; i++)
                Emit(v.smoke, p + normal * 0.1f, normal * Random.Range(0.5f, 1.2f) + Random.insideUnitSphere * 0.3f, Random.Range(0.4f, 0.7f), Random.Range(0.4f, 0.8f), new Color(0.6f, 0.6f, 0.65f, 0.35f));
            v.Billboard(v.matGlow, p + normal * 0.05f, 0.9f, 0.3f, 0.06f, Mats.HDR(new Color(1f, 0.7f, 0.35f), 2f));
            LightFlash(p + normal * 0.3f, new Color(1f, 0.7f, 0.35f), 2f, 3f, 0.05f);
        }

        public static void Sparks(Vector3 p, Vector3 dir, Color tint, int count)
        {
            var v = I;
            for (int i = 0; i < count; i++)
                Emit(v.sparks, p, (dir.normalized + Random.insideUnitSphere).normalized * Random.Range(3f, 9f), Random.Range(0.04f, 0.08f), Random.Range(0.12f, 0.3f), tint);
        }

        public static void Goo(Vector3 p, Vector3 dir, Color color, int count)
        {
            var v = I;
            Vector3 d0 = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.up;
            for (int i = 0; i < count; i++)
            {
                Vector3 vel = d0 * Random.Range(1.5f, 5f) + Random.insideUnitSphere * 2.5f + Vector3.up * Random.Range(1f, 3.5f);
                Color c = Color.Lerp(color, color * 0.5f, Random.value); c.a = 0.95f;
                Emit(v.goo, p, vel, Random.Range(0.09f, 0.24f), Random.Range(0.35f, 0.7f), c);
            }
            for (int i = 0; i < Mathf.Max(1, count / 4); i++)
                Emit(v.dots, p, d0 * Random.Range(1f, 3f) + Random.insideUnitSphere * 1.5f, Random.Range(0.12f, 0.25f), Random.Range(0.15f, 0.3f), color);
        }

        public static void Dots(Vector3 p, Vector3 vel, Color color, float size, float life)
        {
            Emit(I.dots, p, vel, size, life, color);
        }

        public static void Burst(Vector3 p, Color color, int count)
        {
            var v = I;
            for (int i = 0; i < count; i++)
                Emit(v.dots, p, Random.onUnitSphere * Random.Range(2f, 5f), Random.Range(0.15f, 0.3f), Random.Range(0.25f, 0.5f), color);
            v.Billboard(v.matGlow, p, 1.5f, 3f, 0.2f, Mats.HDR(color, 1.5f));
            LightFlash(p, color, 3f, 4f, 0.2f);
        }

        public static void Glow(Vector3 p, Color hdr, float size, float life)
        {
            I.Billboard(I.matGlow, p, size, size * 1.4f, life, hdr);
        }

        public static void LightFlash(Vector3 p, Color color, float intensity, float range, float life)
        {
            var l = I.lights.Next();
            l.t = 0f; l.life = life; l.intensity = intensity; l.active = true;
            l.light.transform.position = p;
            l.light.color = color;
            l.light.range = range;
            l.light.intensity = intensity;
            l.light.enabled = true;
        }

        /// <summary>Expanding flat ring on the ground (dash, small shockwaves).</summary>
        public static void Ring(Vector3 p, float r0, float r1, Color hdr, float life)
        {
            I.Flat(I.flats, I.matRing, p + Vector3.up * 0.06f, r0 * 2f, r1 * 2f, life, hdr, false, false);
        }

        public static void Shockwave(Vector3 p, float radius, Color hdr, float life)
        {
            var v = I;
            v.Flat(v.flats, v.matRing, p + Vector3.up * 0.07f, 0.6f, radius * 2f, life, hdr, false, false);
            v.Flat(v.flats, v.matDisc, p + Vector3.up * 0.06f, radius * 0.6f, radius * 2f, life * 0.6f, hdr * 0.4f, false, false);
        }

        /// <summary>Danger zone: outline at full radius + a fill that grows until the attack lands.</summary>
        public static void Telegraph(Vector3 p, float radius, float duration, Color color)
        {
            var v = I;
            v.Flat(v.flats, v.matRing, p + Vector3.up * 0.05f, radius * 2f, radius * 2f, duration, Mats.HDR(color, 2f), true, false);
            v.Flat(v.flats, v.matDisc, p + Vector3.up * 0.04f, 0.2f, radius * 2f, duration, Mats.HDR(color, 0.9f), true, false);
        }

        public static void Dust(Vector3 p, float radius)
        {
            var v = I;
            for (int i = 0; i < 18; i++)
            {
                Vector2 c = Random.insideUnitCircle.normalized;
                Vector3 dir = new Vector3(c.x, 0f, c.y);
                Emit(v.smoke, p + dir * Random.Range(0.3f, radius * 0.5f) + Vector3.up * 0.2f, dir * Random.Range(3f, 7f) + Vector3.up * Random.Range(0.3f, 1.2f),
                    Random.Range(0.8f, 1.6f), Random.Range(0.5f, 1.0f), new Color(0.45f, 0.42f, 0.4f, 0.5f));
            }
            for (int i = 0; i < 14; i++)
                Emit(v.sparks, p + Vector3.up * 0.1f, (Random.insideUnitSphere + Vector3.up).normalized * Random.Range(4f, 9f), 0.06f, Random.Range(0.2f, 0.4f), new Color(1f, 0.6f, 0.3f));
        }

        /// <summary>Ground stain. Sits on the terrain under <paramref name="p"/> (its y is ignored) and fades over the last 30% of its life.</summary>
        public static void Splat(Vector3 p, float size, Color color, float life = 14f)
        {
            var v = I;
            v.decalLayer = (v.decalLayer + 0.0007f) % 0.02f;
            var terrain = Terrain.activeTerrain;
            float ground = terrain ? terrain.SampleHeight(p) + terrain.transform.position.y : 0f;
            v.Flat(v.decals, v.matDecal, new Vector3(p.x, ground + 0.012f + v.decalLayer, p.z), size, size, life, color, false, true);
        }

        /// <summary>Shell fragments knocked off by a hit: small flat shards that bounce and settle.</summary>
        public static void ChitinChips(Vector3 p, Vector3 dir, Material mat, float size, int count)
        {
            var v = I;
            if (mat == null) mat = v.matShrapnel;
            Vector3 d0 = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.up;
            var mesh = Prims.Mesh(PrimitiveType.Cube);
            float scale = Mathf.Lerp(0.85f, 1.6f, Mathf.InverseLerp(0.9f, 1.8f, size));
            for (int i = 0; i < count; i++)
            {
                float s = Random.Range(0.07f, 0.16f) * scale;
                Vector3 vel = d0 * Random.Range(2f, 5.5f) + Random.insideUnitSphere * 2f + Vector3.up * Random.Range(1.5f, 4f);
                Debris(mesh, mat, p + Random.insideUnitSphere * 0.1f, Random.rotation, new Vector3(s, s * 0.35f, s * Random.Range(1f, 1.8f)),
                    vel, Random.insideUnitSphere * 28f, Random.Range(2.2f, 3.4f));
            }
        }

        public static void Debris(Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale, Vector3 vel, Vector3 angVel, float life)
        {
            var d = I.debris.Next();
            d.mf.sharedMesh = mesh;
            d.mr.sharedMaterial = mat;
            d.scale = scale;
            d.t = 0f; d.life = life; d.active = true;
            d.tr.gameObject.SetActive(false);
            d.tr.SetPositionAndRotation(pos, rot);
            d.tr.localScale = scale;
            d.tr.gameObject.SetActive(true);
            d.rb.position = pos;
            d.rb.rotation = rot;
            d.rb.linearVelocity = vel;
            d.rb.angularVelocity = angVel;
        }

        public static void Casing(Vector3 pos, Vector3 vel, bool shotgunShell = false)
        {
            var v = I;
            if (shotgunShell)
                Debris(Prims.Mesh(PrimitiveType.Cylinder), v.matShell, pos, Random.rotation, new Vector3(0.06f, 0.06f, 0.06f), vel * 1.2f, Random.insideUnitSphere * 20f, 5f);
            else
                Debris(Prims.Mesh(PrimitiveType.Cube), v.matBrass, pos, Random.rotation, new Vector3(0.035f, 0.035f, 0.09f), vel, Random.insideUnitSphere * 30f, 4f);
        }

        /// <param name="carcass">The body stays behind as a ragdoll, so only a few broken-off pieces fly (no legs, less meat).</param>
        public static void AlienDeath(Vector3 center, float size, Color gooColor, Color glowColor, Material skin, Material glowMat, Vector3 dir, bool explosive, Material shell = null, bool carcass = false)
        {
            var v = I;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) dir.Normalize();
            float force = explosive ? 1.8f : 1f;

            // The heavy pieces: carapace plates and leg shards that stay on the ground long after the flash is gone.
            var shellMat = shell != null ? shell : skin != null ? skin : v.matShrapnel;
            int plates = carcass ? Mathf.RoundToInt(size) : Mathf.RoundToInt(2 * size) + (explosive ? 1 : 0);
            for (int i = 0; i < plates; i++)
            {
                float s = Random.Range(0.32f, 0.5f) * size;
                Vector3 vel = (dir * Random.Range(1.5f, 4f) + Random.insideUnitSphere * 2f + Vector3.up * Random.Range(2.5f, 5f)) * force;
                Debris(Prims.Mesh(PrimitiveType.Sphere), shellMat, center + Random.insideUnitSphere * 0.25f * size, Random.rotation,
                    new Vector3(s, s * 0.35f, s * Random.Range(0.8f, 1.3f)), vel, Random.insideUnitSphere * 9f, Random.Range(7f, 10f));
            }
            int shards = carcass ? 0 : Mathf.RoundToInt(4 * size) + (explosive ? 2 : 0);
            for (int i = 0; i < shards; i++)
            {
                float w = Random.Range(0.05f, 0.08f) * size;
                Vector3 vel = (dir * Random.Range(1f, 4.5f) + Random.insideUnitSphere * 3.5f + Vector3.up * Random.Range(3f, 6.5f)) * force;
                Debris(Prims.Mesh(PrimitiveType.Cube), shellMat, center + Random.insideUnitSphere * 0.35f * size, Random.rotation,
                    new Vector3(w, w, Random.Range(0.4f, 0.65f) * size), vel, Random.insideUnitSphere * 22f, Random.Range(5f, 7f));
            }

            int gibs = carcass ? Mathf.RoundToInt(3 * size) : Mathf.RoundToInt(9 * size) + (explosive ? 4 : 0);
            for (int i = 0; i < gibs; i++)
            {
                var mesh = Prims.Mesh(Random.value < 0.6f ? PrimitiveType.Sphere : PrimitiveType.Cube);
                var mat = (Random.value < 0.75f || glowMat == null) ? skin : glowMat;
                if (mat == null) mat = v.matShrapnel;
                float s = Random.Range(0.12f, 0.3f) * size;
                Vector3 vel = (dir * Random.Range(1f, 5f) + Random.insideUnitSphere * 3.5f + Vector3.up * Random.Range(3f, 7f)) * force;
                Debris(mesh, mat, center + Random.insideUnitSphere * 0.3f * size, Random.rotation, new Vector3(s, s * Random.Range(0.6f, 1.2f), s), vel, Random.insideUnitSphere * 20f, Random.Range(3f, 4.5f));
            }
            Goo(center, dir + Vector3.up, gooColor, Mathf.RoundToInt(34 * size));
            for (int i = 0; i < 8 * size; i++)
                Emit(v.dots, center, Random.onUnitSphere * Random.Range(2f, 6f) * force, Random.Range(0.15f, 0.35f), Random.Range(0.25f, 0.5f), glowColor);
            v.Billboard(v.matGlow, center, 1.5f * size, 3.5f * size, 0.16f, Mats.HDR(glowColor, 2.2f));
            LightFlash(center, glowColor, 5f, 6f * size, 0.22f);
            Shockwave(new Vector3(center.x, 0f, center.z), 1.6f * size, Mats.HDR(glowColor, 2f), 0.28f);
            // A pool where it died, plus spatter thrown down-range by the killing shot (all round for explosions).
            Color decal = gooColor * 0.7f; decal.a = 0.9f;
            Vector3 pool = center + dir * 0.5f;
            Splat(pool, Random.Range(2.1f, 2.9f) * size, decal, 40f);
            bool radial = explosive || dir.sqrMagnitude < 0.5f;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            int spatter = Mathf.RoundToInt(4 * size) + (explosive ? 3 : 0);
            for (int i = 0; i < spatter; i++)
            {
                Vector3 o;
                if (radial) { Vector2 c = Random.insideUnitCircle.normalized * Random.Range(1.2f, 3.2f) * size; o = new Vector3(c.x, 0f, c.y); }
                else o = dir * (Random.Range(1f, 3.6f) * size) + side * (Random.Range(-0.9f, 0.9f) * size);
                Color sc = gooColor * Random.Range(0.5f, 0.7f); sc.a = Random.Range(0.7f, 0.9f);
                Splat(pool + o, Random.Range(0.45f, 1.1f) * size, sc, 40f);
            }
        }

        public static void Explosion(Vector3 p, float radius)
        {
            var v = I;
            Vector3 up = Vector3.up;
            v.Billboard(v.matFlash, p + up * 0.6f, radius * 1.4f, radius * 2.2f, 0.09f, Mats.HDR(new Color(1f, 0.95f, 0.85f), 7f));
            v.Billboard(v.matGlow, p + up * 0.8f, radius * 1f, radius * 3f, 0.3f, Mats.HDR(new Color(1f, 0.6f, 0.25f), 4f));
            LightFlash(p + up * 1.5f, new Color(1f, 0.6f, 0.3f), 22f, radius * 3.2f, 0.5f);
            v.Flat(v.flats, v.matRing, p + up * 0.08f, 0.5f, radius * 2.6f, 0.38f, Mats.HDR(new Color(1f, 0.7f, 0.4f), 3f), false, false);
            v.Flat(v.flats, v.matDisc, p + up * 0.07f, radius * 0.5f, radius * 2.1f, 0.18f, Mats.HDR(new Color(1f, 0.5f, 0.2f), 1.5f), false, false);

            for (int i = 0; i < 46; i++)
            {
                Vector3 d = Random.onUnitSphere; d.y = Mathf.Abs(d.y) * 0.8f + 0.1f;
                Emit(v.fire, p + d * Random.Range(0f, 0.7f) + up * 0.4f, d * Random.Range(2f, 10f), Random.Range(1.0f, 2.4f), Random.Range(0.35f, 0.75f),
                    Color.Lerp(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.5f, 0.2f), Random.value));
            }
            for (int i = 0; i < 24; i++)
            {
                Vector2 c = Random.insideUnitCircle;
                Emit(v.smoke, p + up * Random.Range(0.3f, 1.5f), new Vector3(c.x, 0f, c.y) * Random.Range(1f, 5f) + up * Random.Range(0.8f, 3.5f),
                    Random.Range(1.8f, 3.4f), Random.Range(1.4f, 2.8f), new Color(0.12f, 0.11f, 0.12f, 0.75f));
            }
            for (int i = 0; i < 70; i++)
            {
                Vector3 d = (Random.onUnitSphere + up * 0.6f).normalized;
                Emit(v.sparks, p + up * 0.3f, d * Random.Range(8f, 26f), Random.Range(0.05f, 0.12f), Random.Range(0.3f, 0.8f), Warm);
            }
            for (int i = 0; i < 34; i++)
                Emit(v.embers, p + Random.insideUnitSphere * radius * 0.4f + up * 0.5f, (Random.insideUnitSphere + up).normalized * Random.Range(1f, 6f),
                    Random.Range(0.05f, 0.12f), Random.Range(0.8f, 2.2f), Color.white);
            for (int i = 0; i < 7; i++)
            {
                float s = Random.Range(0.08f, 0.2f);
                Debris(Prims.Mesh(PrimitiveType.Cube), v.matShrapnel, p + up * 0.4f, Random.rotation, new Vector3(s, s * 0.6f, s * 1.4f),
                    (Random.insideUnitSphere * 7f + up * Random.Range(4f, 9f)), Random.insideUnitSphere * 25f, Random.Range(2f, 3.5f));
            }
            Splat(p, radius * 1.1f, new Color(0.02f, 0.02f, 0.025f, 0.85f));
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            float dt = Time.deltaTime;
            var cam = CameraRig.I ? CameraRig.I.Cam : Camera.main;
            Quaternion camRot = cam ? cam.transform.rotation : Quaternion.identity;

            foreach (var tr in tracers.Items)
            {
                if (!tr.active) continue;
                tr.t += dt;
                float k = tr.t / tr.life;
                if (k >= 1f) { tr.active = false; tr.lr.enabled = false; continue; }
                Vector3 tail = Vector3.Lerp(tr.a, tr.b, Ease.OutCubic(k));
                tr.lr.SetPosition(0, tail);
                Color head = tr.color; head.a = 1f - k;
                Color tailC = tr.color; tailC.a = (1f - k) * 0.15f;
                tr.lr.startColor = tailC;
                tr.lr.endColor = head;
                tr.lr.widthMultiplier = tr.width * (1f - k * 0.5f);
            }

            foreach (var bo in bolts.Items)
            {
                if (!bo.active) continue;
                bo.t += dt;
                float head = Mathf.Min(bo.dist, bo.t * bo.speed);
                float tail = Mathf.Clamp(bo.t * bo.speed - bo.length, 0f, bo.dist);
                if (tail >= bo.dist - 0.001f) { bo.active = false; bo.lr.enabled = false; continue; }
                bo.lr.SetPosition(0, bo.a + bo.dir * tail);
                bo.lr.SetPosition(1, bo.a + bo.dir * head);
                Emit(dots, bo.a + bo.dir * head, Random.insideUnitSphere * 0.3f, bo.width * 3.2f, 0.06f, bo.color);
                if (Random.value < 0.5f) Emit(dots, bo.a + bo.dir * Mathf.Lerp(tail, head, Random.value), Random.insideUnitSphere * 0.6f, bo.width * 1.2f, 0.18f, bo.color);
            }

            foreach (var q in billboards.Items) if (q.active) { q.t += dt; UpdateQuad(q, camRot); }
            foreach (var q in flats.Items) if (q.active) { q.t += dt; UpdateQuad(q, camRot); }
            foreach (var q in decals.Items) if (q.active) { q.t += dt; UpdateQuad(q, camRot); }

            foreach (var l in lights.Items)
            {
                if (!l.active) continue;
                l.t += dt;
                float k = l.t / l.life;
                if (k >= 1f) { l.active = false; l.light.enabled = false; continue; }
                l.light.intensity = l.intensity * (1f - k) * (1f - k);
            }

            foreach (var d in debris.Items)
            {
                if (!d.active) continue;
                d.t += dt;
                if (d.t >= d.life) { d.active = false; d.tr.gameObject.SetActive(false); continue; }
                float remain = d.life - d.t;
                if (remain < 0.5f) d.tr.localScale = d.scale * (remain / 0.5f);
            }
        }

        void UpdateQuad(Quad q, float _) => UpdateQuad(q, CameraRig.I && CameraRig.I.Cam ? CameraRig.I.Cam.transform.rotation : Quaternion.identity);

        void UpdateQuad(Quad q, Quaternion camRot)
        {
            float k = q.t / q.life;
            if (k >= 1f) { q.active = false; q.tr.gameObject.SetActive(false); return; }
            float size = Mathf.Lerp(q.size0, q.size1, Ease.OutCubic(k));
            float fade;
            if (q.alphaBlend) fade = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
            else if (q.fadeIn) fade = k < 0.85f ? 0.3f + 0.7f * (k / 0.85f) : 1f - (k - 0.85f) / 0.15f;
            else fade = (1f - k) * (1f - k * 0.5f);

            q.tr.position = q.pos;
            q.tr.rotation = q.billboard ? camRot * Quaternion.Euler(0f, 0f, q.roll) : Quaternion.Euler(90f, q.roll, 0f);
            q.tr.localScale = new Vector3(size, size, size);
            Color c = q.alphaBlend ? new Color(q.color.r, q.color.g, q.color.b, q.color.a * fade) : q.color * fade;
            mpb.SetColor(ColorId, c);
            q.mr.SetPropertyBlock(mpb);
        }
    }
}
