using UnityEngine;

namespace Zcom
{
    /// <summary>Static stars + faint nebula clouds around and below the orbital platform.</summary>
    public class Starfield : MonoBehaviour
    {
        public int stars = 1800;
        public int nebulae = 14;
        public float minRadius = 140f;
        public float maxRadius = 320f;
        public int seed = 42;

        void Start()
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();

            var starPs = MakeSystem("Stars", Mats.Additive(ProcTex.Glow, Color.white * 2.5f), stars + 10);
            for (int i = 0; i < stars; i++)
            {
                Vector3 dir = new Vector3(R() * 2 - 1, R() * 2 - 1.3f, R() * 2 - 1).normalized;
                float rad = Mathf.Lerp(minRadius, maxRadius, R());
                float tint = R();
                Color c = tint < 0.15f ? new Color(1f, 0.8f, 0.6f) : tint < 0.3f ? new Color(0.65f, 0.8f, 1f) : Color.white;
                c *= Mathf.Lerp(0.35f, 1f, R());
                c.a = 1f;
                Emit(starPs, transform.position + dir * rad, Mathf.Lerp(0.5f, 2.2f, R() * R()) * rad / 200f, c);
            }

            var nebPs = MakeSystem("Nebula", Mats.Additive(ProcTex.Smoke, Color.white * 0.6f), nebulae + 4);
            for (int i = 0; i < nebulae; i++)
            {
                Vector3 dir = new Vector3(R() * 2 - 1, -R() * 1.2f - 0.2f, R() * 2 - 1).normalized;
                Color c = Color.Lerp(new Color(0.35f, 0.15f, 0.6f), new Color(0.1f, 0.3f, 0.6f), R());
                c.a = Mathf.Lerp(0.12f, 0.25f, R());
                Emit(nebPs, transform.position + dir * 260f, Mathf.Lerp(120f, 220f, R()), c, R() * 360f);
            }
        }

        ParticleSystem MakeSystem(string name, Material mat, int max)
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
            main.startSpeed = 0f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.maxParticleSize = 50f;
            r.minParticleSize = 0f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = 100f;
            ps.Play();
            return ps;
        }

        static void Emit(ParticleSystem ps, Vector3 pos, float size, Color c, float rot = 0f)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = Vector3.zero,
                startSize = size,
                startLifetime = 1e6f,
                startColor = c,
                rotation = rot,
            };
            ps.Emit(ep, 1);
        }
    }
}
