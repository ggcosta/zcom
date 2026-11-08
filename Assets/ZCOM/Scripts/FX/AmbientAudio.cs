using UnityEngine;

namespace Zcom
{
    /// <summary>Soundscape: constant moon wind bed, plus a pulsing tension layer that swells while hostiles are engaged.</summary>
    public class AmbientAudio : MonoBehaviour
    {
        AudioSource wind, tension;
        float tensionLevel;

        AudioSource Loop(string id, float vol)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = ProcAudio.GetClip(id);
            s.loop = true;
            s.volume = vol;
            s.spatialBlend = 0f;
            s.Play();
            return s;
        }

        void Start()
        {
            wind = Loop("amb_wind", 0.28f);
            tension = Loop("amb_tension", 0f);
        }

        void Update()
        {
            var gm = GameManager.I;
            float target = gm && !gm.IsGameOver && !gm.IsExtracted ? Mathf.Clamp01(gm.AlertedHostiles / 3f) : 0f;
            var ez = gm ? gm.Extraction : null;
            if (ez && ez.Progress01 > 0f) target = Mathf.Max(target, 0.8f);
            tensionLevel = Mathf.MoveTowards(tensionLevel, target, Time.unscaledDeltaTime * (target > tensionLevel ? 0.8f : 0.25f));
            if (tension) tension.volume = tensionLevel * 0.32f;
            if (wind) wind.volume = GameFeel.Paused ? 0.12f : 0.28f;
            if (tension) tension.pitch = GameFeel.Paused ? 0.5f : 1f;
        }
    }
}
