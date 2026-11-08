using UnityEngine;

namespace Zcom
{
    /// <summary>Small burning wreckage fire: flames, smoke and embers, with a flickering light.</summary>
    public class Smolder : MonoBehaviour
    {
        public float intensity = 1f;
        Light flicker;
        float baseIntensity, t, seed;

        void Start()
        {
            flicker = GetComponentInChildren<Light>();
            if (flicker) baseIntensity = flicker.intensity;
            seed = Random.value * 100f;
        }

        void Update()
        {
            t -= Time.deltaTime;
            if (t <= 0f)
            {
                t = 0.05f / Mathf.Max(0.2f, intensity);
                Vfx.Flame(transform.position, intensity);
            }
            if (flicker) flicker.intensity = baseIntensity * (0.65f + 0.5f * Mathf.PerlinNoise(Time.time * 6f, seed));
        }
    }
}
