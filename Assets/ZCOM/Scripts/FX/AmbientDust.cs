using UnityEngine;

namespace Zcom
{
    /// <summary>Drifting regolith dust motes around the player: catches the light, sells scale and atmosphere.</summary>
    public class AmbientDust : MonoBehaviour
    {
        ParticleSystem ps;

        void Start()
        {
            var go = new GameObject("DustMotes");
            go.transform.SetParent(transform, false);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.7f, 0.9f, 0.35f), new Color(0.6f, 0.85f, 1f, 0.25f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var em = ps.emission; em.rateOverTime = 45f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(34f, 3f, 34f); sh.position = new Vector3(0, 1.6f, 0);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.15f, 0.45f); vel.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.08f); vel.z = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.25f; noise.frequency = 0.3f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var shader = ShaderBank.I && ShaderBank.I.fxAdditive ? ShaderBank.I.fxAdditive : Shader.Find("ZCOM/FxAdditive");
            var m = new Material(shader ? shader : Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetTexture("_MainTex", ProcTex.Glow);
            m.SetColor("_Color", new Color(0.8f, 0.8f, 0.8f, 1f));
            r.sharedMaterial = m;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        void LateUpdate()
        {
            var p = PlayerController.I;
            if (p && ps) ps.transform.position = p.transform.position;
        }
    }
}
