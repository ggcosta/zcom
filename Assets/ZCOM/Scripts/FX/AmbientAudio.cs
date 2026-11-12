using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Soundscape: constant moon wind bed, a pulsing tension layer, and music (Resources/Music, CC0): an exploration
    /// loop that crossfades into a combat loop while hostiles are engaged or the evac countdown runs, and a stinger
    /// on extraction. Everything ducks while paused and fades out when the raid ends.
    /// </summary>
    public class AmbientAudio : MonoBehaviour
    {
        const float ExploreVol = 0.5f, CombatVol = 0.6f, StingerVol = 0.7f;
        AudioSource wind, tension, explore, combat, stinger;
        float tensionLevel, combatMix, musicFade = 1f;
        bool stingerPlayed;

        AudioSource Music(string file, bool loop)
        {
            var clip = Resources.Load<AudioClip>("Music/" + file);
            if (!clip) return null;
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = loop;
            s.volume = 0f;
            s.spatialBlend = 0f;
            s.playOnAwake = false;
            if (loop) s.Play();
            return s;
        }

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
            explore = Music("music_explore", true);
            combat = Music("music_combat", true);
            stinger = Music("music_extract", false);
        }

        void Update()
        {
            var gm = GameManager.I;
            float target = gm && !gm.IsGameOver && !gm.IsExtracted ? Mathf.Clamp01(gm.AlertedHostiles / 3f) : 0f;
            var ez = gm ? gm.Extraction : null;
            if (ez && ez.Progress01 > 0f) target = Mathf.Max(target, 0.8f);
            tensionLevel = Mathf.MoveTowards(tensionLevel, target, Time.unscaledDeltaTime * (target > tensionLevel ? 0.8f : 0.25f));
            if (tension) tension.volume = tensionLevel * 0.2f;

            // music: crossfade on the same tension signal, fade everything out when the raid ends
            bool ended = gm && (gm.IsGameOver || gm.IsExtracted);
            float dt = Time.unscaledDeltaTime;
            combatMix = Mathf.MoveTowards(combatMix, tensionLevel > 0.15f ? 1f : 0f, dt * (tensionLevel > 0.15f ? 0.9f : 0.2f));
            musicFade = Mathf.MoveTowards(musicFade, ended ? 0f : 1f, dt * (ended ? 0.5f : 1f));
            float duck = GameFeel.Paused ? 0.35f : 1f;
            float intro = Mathf.Clamp01(Time.timeSinceLevelLoad / 3f);
            if (explore) explore.volume = ExploreVol * (1f - combatMix) * musicFade * duck * intro;
            if (combat) combat.volume = CombatVol * combatMix * musicFade * duck;
            if (gm && gm.IsExtracted && stinger && !stingerPlayed)
            {
                stingerPlayed = true;
                stinger.volume = StingerVol;
                stinger.Play();
            }
            if (wind) wind.volume = GameFeel.Paused ? 0.12f : 0.28f;
            if (tension) tension.pitch = GameFeel.Paused ? 0.5f : 1f;
        }
    }
}
