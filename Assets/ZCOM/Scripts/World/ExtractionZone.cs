using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Evac point: stand inside for <see cref="holdTime"/> seconds to extract with everything in the backpack.
    /// Starting the countdown signals the hive: a pack of skitters converges on the beacon.
    /// The beam, ring and lights pulse harder as the countdown runs.
    /// </summary>
    public class ExtractionZone : MonoBehaviour
    {
        public float radius = 3.2f;
        public float holdTime = 8f;
        public Renderer beam, ring;
        public Light beaconLight;

        public float Progress01 => Mathf.Clamp01(progress / holdTime);
        public bool PlayerInside { get; private set; }
        public float Distance { get; private set; }
        float progress, pulse;
        bool ambushed;
        MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Start() { mpb = new MaterialPropertyBlock(); }

        void Update()
        {
            var p = PlayerController.I;
            var gm = GameManager.I;
            if (!p || !gm || gm.IsExtracted || gm.IsGameOver) return;
            Vector3 d = p.transform.position - transform.position; d.y = 0f;
            Distance = d.magnitude;
            PlayerInside = Distance < radius && p.CanAct;
            float before = progress;
            progress = PlayerInside ? progress + Time.deltaTime : Mathf.Max(0f, progress - Time.deltaTime * 2f);
            if (PlayerInside && before <= 0f) ProcAudio.Play("beam_start", 0.6f, 0f, 0.8f);
            if (PlayerInside && Mathf.FloorToInt(before) != Mathf.FloorToInt(progress)) ProcAudio.Play("beep", 0.5f, 0f, 1f + Progress01 * 0.6f);
            if (!ambushed && progress > 1.5f) Ambush();
            if (progress >= holdTime) gm.OnExtracted();

            pulse += Time.deltaTime * (2f + Progress01 * 10f);
            float k = 0.6f + 0.4f * Mathf.Sin(pulse) + Progress01 * 1.2f;
            if (beaconLight) beaconLight.intensity = 2.5f + k * 2.5f;
            if (beam)
            {
                mpb.SetColor(ColorId, new Color(0.35f, 0.9f, 1f) * (0.35f + k * 0.35f));
                beam.SetPropertyBlock(mpb);
            }
            if (ring) ring.transform.Rotate(0f, Time.deltaTime * (20f + Progress01 * 160f), 0f);
        }

        void Ambush()
        {
            ambushed = true;
            if (Hud.I) Hud.I.Banner("HIVE ALERTED", "The beacon drew them in. Hold the evac point!", "WARNING");
            ProcAudio.Play("screech", 0.7f, 0f, 0.8f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Random.Range(-0.4f, 0.4f);
                Vector3 pos = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 16f;
                var al = AlienFactory.Create(i == 3 ? AlienType.Spitter : AlienType.Skitter, pos, Quaternion.identity);
                al.Alert(false);
            }
        }
    }
}
