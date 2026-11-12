using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Sound effects by id, played through a small voice pool with pitch variation. Every id has a clip synthesised
    /// from noise/oscillators (no audio assets needed); recorded clips in Resources/Audio replace them by id, see LoadRecorded.
    /// </summary>
    public class ProcAudio : MonoBehaviour
    {
        static ProcAudio inst;
        static Dictionary<string, AudioClip[]> bank;
        const int SR = 44100;
        static System.Random rng = new System.Random(1);

        [Range(0, 1)] public float masterVolume = 0.8f;
        AudioSource[] voices;
        int next;

        static ProcAudio I
        {
            get
            {
                if (inst == null) inst = new GameObject("ProcAudio").AddComponent<ProcAudio>();
                return inst;
            }
        }

        void Awake()
        {
            if (inst != null && inst != this) { Destroy(gameObject); return; }
            inst = this;
            voices = new AudioSource[32];
            for (int i = 0; i < voices.Length; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                voices[i] = s;
            }
            if (bank == null) BuildBank();
        }

        /// <summary>Raw clip access (e.g. for looping sources).</summary>
        public static AudioClip GetClip(string id)
        {
            if (bank == null) BuildBank();
            return bank.TryGetValue(id, out var c) ? c[0] : null;
        }

        /// <summary>
        /// Level trims for recordings that replace a synthesised id: the synth clips are mastered at lower peaks (see
        /// their Add() calls), while recordings arrive at -1 dBFS, so without a trim they would jump out of the mix.
        /// </summary>
        static readonly Dictionary<string, float> RecordedTrim = new Dictionary<string, float>
        {
            { "ui_open", 0.45f }, { "ui_close", 0.4f }, { "ui_hover", 0.25f }, { "ui_pick", 0.5f }, { "ui_drop", 0.55f },
            { "ui_deny", 0.45f }, { "ui_scan", 0.35f }, { "ui_reveal", 0.4f }, { "ui_equip", 0.6f },
            { "ion", 0.6f }, { "nova", 0.8f },
        };
        static readonly HashSet<string> recordedIds = new HashSet<string>();

        public static void Play(string id, float volume = 1f, float pitchVariance = 0.05f, float pitch = 1f)
        {
            var p = I;
            if (p.voices == null || bank == null) return;
            if (!bank.TryGetValue(id, out var clips)) return;
            if (recordedIds.Contains(id) && RecordedTrim.TryGetValue(id, out var trim)) volume *= trim;
            AudioSource s = null;
            for (int i = 0; i < p.voices.Length; i++)
            {
                var v = p.voices[(p.next + i) % p.voices.Length];
                if (!v.isPlaying) { s = v; p.next = (p.next + i + 1) % p.voices.Length; break; }
            }
            if (s == null) { s = p.voices[p.next]; p.next = (p.next + 1) % p.voices.Length; }
            s.clip = clips[Random.Range(0, clips.Length)];
            s.volume = volume * p.masterVolume;
            s.pitch = pitch * (1f + Random.Range(-pitchVariance, pitchVariance));
            s.Play();
        }

        // ------------------------------------------------------------------ synthesis

        static float N() => (float)(rng.NextDouble() * 2.0 - 1.0);
        static float Tanh(float x) => (float)System.Math.Tanh(x);
        static float[] Buf(float seconds) => new float[Mathf.CeilToInt(seconds * SR)];
        const float TAU = Mathf.PI * 2f;

        static AudioClip Clip(string name, float[] d, float peak = 0.9f)
        {
            float max = 0.0001f;
            for (int i = 0; i < d.Length; i++) max = Mathf.Max(max, Mathf.Abs(d[i]));
            float g = peak / max;
            // tiny fade-out to avoid clicks
            int fade = Mathf.Min(256, d.Length);
            for (int i = 0; i < d.Length; i++)
            {
                d[i] *= g;
                int fromEnd = d.Length - 1 - i;
                if (fromEnd < fade) d[i] *= fromEnd / (float)fade;
            }
            var c = AudioClip.Create(name, d.Length, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        static void Add(string id, params AudioClip[] clips) => bank[id] = clips;

        /// <summary>How many recorded clips replaced or extended the synthesised bank (0 = Resources/Audio is missing).</summary>
        public static int RecordedClips { get; private set; }

        /// <summary>Like Clip() but without the fade-out so it loops seamlessly.</summary>
        static AudioClip ClipLoop(string name, float[] d, float peak = 0.8f)
        {
            float max = 0.0001f;
            for (int i = 0; i < d.Length; i++) max = Mathf.Max(max, Mathf.Abs(d[i]));
            for (int i = 0; i < d.Length; i++) d[i] *= peak / max;
            var c = AudioClip.Create(name, d.Length, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>Laser "pew": fast descending sine/square sweep with a soft thump.</summary>
        static float[] Ion(int variant)
        {
            var d = Buf(0.2f);
            float ph = 0, ph2 = 0, sub = 0;
            float top = 2200f + variant * 180f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float f = Mathf.Lerp(260f, top, Mathf.Exp(-t * 32f));
                ph += TAU * f / SR;
                ph2 += TAU * f * 1.5f / SR;
                sub += TAU * Mathf.Lerp(60f, 160f, Mathf.Exp(-t * 40f)) / SR;
                float env = Mathf.Exp(-t * 20f);
                float tone = Mathf.Sin(ph) * 0.8f + Mathf.Sign(Mathf.Sin(ph2)) * 0.18f;
                d[i] = Tanh((tone * env + Mathf.Sin(sub) * Mathf.Exp(-t * 30f) * 0.7f + N() * Mathf.Exp(-t * 200f) * 0.4f) * 1.4f);
            }
            return d;
        }

        /// <summary>Plasma shotgun: crackling burst + low sweep + sub boom.</summary>
        static float[] Nova(int variant)
        {
            var d = Buf(0.45f);
            float ph = 0, sub = 0, lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                lp += (N() - lp) * 0.3f;
                ph += TAU * Mathf.Lerp(140f, 1300f + variant * 150f, Mathf.Exp(-t * 14f)) / SR;
                sub += TAU * Mathf.Lerp(38f, 110f, Mathf.Exp(-t * 18f)) / SR;
                float crackle = lp * Mathf.Exp(-t * 16f) * (0.6f + 0.4f * Mathf.Sin(TAU * 90f * t));
                d[i] = Tanh((Mathf.Sin(ph) * Mathf.Exp(-t * 10f) * 0.9f + Mathf.Sin(sub) * Mathf.Exp(-t * 8f) * 1.3f + crackle * 1.4f) * 1.8f);
            }
            return d;
        }

        /// <summary>2-second seamless beam hum (all partials are whole multiples of 0.5 Hz).</summary>
        static float[] BeamLoop()
        {
            const float len = 2f;
            var d = Buf(len);
            int n = d.Length;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float wob = Mathf.Sin(TAU * 6f * t);
                float hum = Mathf.Sin(TAU * 110f * t + wob * 0.6f) + 0.5f * Mathf.Sin(TAU * 220f * t) + 0.25f * Mathf.Sign(Mathf.Sin(TAU * 55f * t));
                float sizzle = Mathf.Sin(TAU * 1760f * t + Mathf.Sin(TAU * 13f * t) * 3f) * 0.18f;
                float am = 0.8f + 0.2f * Mathf.Sin(TAU * 12f * t);
                d[i] = (hum * 0.5f + sizzle) * am;
            }
            // light crackle, cross-faded at the seam
            var rnd = new System.Random(5);
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                lp += ((float)(rnd.NextDouble() * 2 - 1) - lp) * 0.5f;
                float edge = Mathf.Min(1f, Mathf.Min(i, n - 1 - i) / 2000f);
                d[i] += lp * 0.12f * edge;
            }
            return d;
        }

        static float[] CellOut()
        {
            var d = Buf(0.3f);
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                lp += (N() - lp) * 0.15f;
                float click = N() * Mathf.Exp(-t * 300f) + Mathf.Sin(TAU * 1500f * t) * Mathf.Exp(-t * 80f) * 0.5f;
                float hiss = lp * 2f * Mathf.Exp(-t * 9f) * Mathf.Min(1f, t / 0.01f);
                float down = Mathf.Sin(TAU * Mathf.Lerp(900f, 200f, t / 0.3f) * t) * Mathf.Exp(-t * 10f) * 0.4f;
                d[i] = click + hiss + down;
            }
            return d;
        }

        /// <summary>Rising capacitor whine.</summary>
        static float[] Recharge()
        {
            var d = Buf(0.9f);
            float ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float k = t / 0.9f;
                ph += TAU * Mathf.Lerp(250f, 1800f, k * k) / SR;
                float trem = 0.7f + 0.3f * Mathf.Sin(TAU * Mathf.Lerp(10f, 40f, k) * t);
                d[i] = (Mathf.Sin(ph) + 0.3f * Mathf.Sin(ph * 2f)) * trem * Mathf.Min(1f, t / 0.05f) * Mathf.Clamp01((1f - k) / 0.1f);
            }
            return d;
        }

        static void BuildBank()
        {
            bank = new Dictionary<string, AudioClip[]>();
            Add("shot", Clip("shot0", Shot(0)), Clip("shot1", Shot(1)), Clip("shot2", Shot(2)));
            Add("explosion", Clip("explosion", Explosion(1.8f, 2.2f, 1f)));
            Add("shotgun", Clip("shotgun0", Shotgun(0)), Clip("shotgun1", Shotgun(1)));
            Add("rail", Clip("rail", Rail()));
            Add("ion", Clip("ion0", Ion(0)), Clip("ion1", Ion(1)), Clip("ion2", Ion(2)));
            Add("nova", Clip("nova0", Nova(0)), Clip("nova1", Nova(1)));
            Add("beam_loop", ClipLoop("beam_loop", BeamLoop()));
            Add("beam_start", Clip("beam_start", Sweep(0.22f, 220f, 1400f), 0.7f));
            Add("beam_stop", Clip("beam_stop", Sweep(0.2f, 1100f, 160f), 0.6f));
            Add("cell_out", Clip("cell_out", CellOut(), 0.7f));
            Add("recharge", Clip("recharge", Recharge(), 0.6f));
            Add("pump", Clip("pump", Click(0.16f, 1300f, 0.09f), 0.7f));
            Add("equip", Clip("equip", Click(0.12f, 2100f, 0.06f), 0.6f));
            Add("chest_open", Clip("chest_open", ChestOpen()));
            Add("pickup_res", Clip("pickup_res", Arp(new[] { 990f, 1480f }, 0.05f), 0.5f));
            Add("slam", Clip("slam", Explosion(0.7f, 6f, 0.4f), 0.8f));
            Add("hit_alien", Clip("hit0", AlienHit()), Clip("hit1", AlienHit()), Clip("hit2", AlienHit()));
            Add("alien_death", Clip("death0", AlienDeath()), Clip("death1", AlienDeath()));
            Add("screech", Clip("screech", Screech()));
            Add("spit", Clip("spit", Spit()));
            Add("reload_out", Clip("reload_out", Click(0.1f, 1700f, 0.045f)));
            Add("reload_in", Clip("reload_in", Clack()));
            Add("empty", Clip("empty", Click(0.05f, 2600f, -1f), 0.5f));
            Add("beep", Clip("beep", Tone(0.07f, 2000f, 0.004f), 0.6f));
            Add("ready", Clip("ready", Sweep(0.12f, 1100f, 1700f), 0.5f));
            Add("throw", Clip("throw", Whoosh(0.32f, 0.04f, 0.25f)));
            Add("dash", Clip("dash", Whoosh(0.22f, 0.08f, 0.5f)));
            Add("clink", Clip("clink", Clink(), 0.6f));
            Add("step", Clip("step0", Step(0)), Clip("step1", Step(1)), Clip("step2", Step(2)), Clip("step3", Step(3)));
            Add("thump", Clip("thump", Thump(), 0.9f));
            Add("amb_wind", ClipLoop("amb_wind", AmbWind(), 0.7f));
            Add("amb_tension", ClipLoop("amb_tension", AmbTension(), 0.7f));
            Add("hurt", Clip("hurt", Hurt()));
            Add("player_death", Clip("player_death", PlayerDeath()));
            Add("pickup", Clip("pickup", Arp(new[] { 660f, 880f, 1320f }, 0.09f)));
            Add("portal", Clip("portal", Portal()));
            Add("wave", Clip("wave", Alarm(), 0.7f));
            Add("clear", Clip("clear", Arp(new[] { 523f, 659f, 784f, 1046f }, 0.14f), 0.7f));
            // interface
            Add("ui_open", Clip("ui_open", Sweep(0.18f, 520f, 1500f), 0.4f));
            Add("ui_close", Clip("ui_close", Sweep(0.13f, 1300f, 480f), 0.35f));
            Add("ui_hover", Clip("ui_hover", Tone(0.035f, 2500f, 0.002f), 0.22f));
            Add("ui_pick", Clip("ui_pick", Click(0.07f, 1500f, -1f), 0.45f));
            Add("ui_drop", Clip("ui_drop", Click(0.11f, 880f, 0.035f), 0.5f));
            Add("ui_deny", Clip("ui_deny", Arp(new[] { 233f, 175f }, 0.07f), 0.4f));
            Add("ui_scan", Clip("ui_scan", Sweep(0.22f, 900f, 2400f), 0.3f));
            Add("ui_reveal", Clip("ui_reveal", Arp(new[] { 1320f, 1760f }, 0.04f), 0.35f));
            Add("ui_equip", Clip("ui_equip", Click(0.15f, 1150f, 0.05f), 0.55f));
            LoadRecorded();
        }

        /// <summary>
        /// Recorded clips win over the synthesised ones. Every clip under Resources/Audio named "&lt;id&gt;_NN"
        /// (e.g. Audio/screech/screech_02.ogg) becomes a variant of that id; ids with no files keep the synth above,
        /// so a sound is replaced just by dropping files in. A few ids (roar, alien_pain, gib, chitin) only exist as recordings.
        /// </summary>
        static void LoadRecorded()
        {
            RecordedClips = 0;
            var found = new SortedDictionary<string, List<AudioClip>>(System.StringComparer.Ordinal);
            foreach (var c in Resources.LoadAll<AudioClip>("Audio"))
            {
                int cut = c.name.LastIndexOf('_');
                if (cut <= 0) continue;
                c.LoadAudioData();   // decode now, not on the first play in the middle of a fight
                string id = c.name.Substring(0, cut);
                if (!found.TryGetValue(id, out var list)) found[id] = list = new List<AudioClip>();
                list.Add(c);
                RecordedClips++;
            }
            foreach (var kv in found)
            {
                kv.Value.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                bank[kv.Key] = kv.Value.ToArray();
                recordedIds.Add(kv.Key);
            }
        }

        static float[] Shot(int variant)
        {
            var d = Buf(0.28f);
            float lp = 0, lp2 = 0, phase = 0;
            float tone = 150f + variant * 12f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float n = N();
                lp += (n - lp) * 0.28f;
                lp2 += (lp - lp2) * 0.06f;
                float f = Mathf.Lerp(48f, tone, Mathf.Exp(-t * 38f));
                phase += TAU * f / SR;
                float thump = Mathf.Sin(phase) * Mathf.Exp(-t * 16f);
                float crack = n * Mathf.Exp(-t * 140f);
                float body = lp * Mathf.Exp(-t * 32f);
                float tail = lp2 * Mathf.Exp(-t * 8f) * 3f;
                float zap = Mathf.Sin(TAU * (900f - t * 2600f) * t) * Mathf.Exp(-t * 60f) * 0.25f; // sci-fi edge
                d[i] = Tanh((crack * 0.8f + body * 1.0f + thump * 1.0f + tail * 0.45f + zap) * 1.8f);
            }
            return d;
        }

        static float[] Shotgun(int variant)
        {
            var d = Buf(0.55f);
            float lp = 0, lp2 = 0, phase = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float n = N();
                lp += (n - lp) * 0.22f;
                lp2 += (lp - lp2) * 0.04f;
                phase += TAU * Mathf.Lerp(38f, 120f + variant * 10f, Mathf.Exp(-t * 22f)) / SR;
                float boom = Mathf.Sin(phase) * Mathf.Exp(-t * 9f) * 1.3f;
                float crack = n * Mathf.Exp(-t * 70f) * 1.2f;
                float body = lp * Mathf.Exp(-t * 14f) * 1.4f;
                float tail = lp2 * Mathf.Exp(-t * 4.5f) * 4f;
                d[i] = Tanh((boom + crack + body + tail) * 2.2f);
            }
            return d;
        }

        static float[] Rail()
        {
            var d = Buf(0.75f);
            float phase = 0, phase2 = 0, lp = 0, sub = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float n = N();
                lp += (n - lp) * 0.35f;
                phase += TAU * Mathf.Lerp(180f, 2400f, Mathf.Exp(-t * 9f)) / SR;   // descending zap
                phase2 += TAU * Mathf.Lerp(90f, 1200f, Mathf.Exp(-t * 6f)) / SR;
                sub += TAU * Mathf.Lerp(35f, 90f, Mathf.Exp(-t * 10f)) / SR;
                float zap = (Mathf.Sin(phase) + 0.5f * Mathf.Sign(Mathf.Sin(phase2))) * Mathf.Exp(-t * 6f);
                float crack = lp * Mathf.Exp(-t * 40f) * 1.5f;
                float thump = Mathf.Sin(sub) * Mathf.Exp(-t * 7f) * 1.4f;
                float hum = Mathf.Sin(TAU * 60f * t) * Mathf.Sin(TAU * 7f * t) * Mathf.Exp(-t * 3f) * 0.2f;
                d[i] = Tanh((zap * 0.7f + crack + thump + hum) * 1.6f);
            }
            return d;
        }

        static float[] ChestOpen()
        {
            var d = Buf(0.7f);
            float lp = 0, phase = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                lp += (N() - lp) * 0.08f;
                float hiss = lp * 3f * Mathf.Exp(-t * 6f) * Mathf.Min(1f, t / 0.02f);
                phase += TAU * Mathf.Lerp(300f, 900f, Mathf.Clamp01(t / 0.5f)) / SR;
                float chime = t > 0.15f ? Mathf.Sin(phase) * Mathf.Exp(-(t - 0.15f) * 5f) * 0.5f : 0f;
                float clunk = N() * Mathf.Exp(-t * 200f) + Mathf.Sin(TAU * 140f * t) * Mathf.Exp(-t * 30f);
                d[i] = hiss + chime + clunk * 0.8f;
            }
            return d;
        }

        static float[] Explosion(float len, float decay, float noiseAmt)
        {
            var d = Buf(len);
            float brown = 0, lp = 0, phase = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float n = N();
                brown = (brown + n * 0.02f) * 0.997f;
                lp += (n - lp) * 0.05f;
                float f = Mathf.Lerp(30f, 85f, Mathf.Exp(-t * 7f));
                phase += TAU * f / SR;
                float env = (1f - Mathf.Exp(-t * 400f)) * Mathf.Exp(-t * decay);
                float crack = n * Mathf.Exp(-t * 28f) * noiseAmt;
                d[i] = Tanh((brown * 6f * env + Mathf.Sin(phase) * Mathf.Exp(-t * decay * 1.1f) * 1.1f + crack * 0.7f + lp * 2.2f * env * noiseAmt) * 1.6f);
            }
            return d;
        }

        static float[] AlienHit()
        {
            var d = Buf(0.15f);
            float lp = 0, phase = 0;
            float f0 = 380f + N() * 80f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                lp += (N() - lp) * 0.18f;
                phase += TAU * Mathf.Lerp(140f, f0, Mathf.Exp(-t * 30f)) / SR;
                float wobble = 0.6f + 0.4f * Mathf.Sin(TAU * 38f * t);
                d[i] = (lp * 2.5f * Mathf.Exp(-t * 26f) * wobble + Mathf.Sin(phase) * Mathf.Exp(-t * 30f) * 0.6f);
            }
            return d;
        }

        static float[] AlienDeath()
        {
            var d = Buf(0.65f);
            float lp = 0, nlp = 0, phase = 0;
            float f0 = 600f + N() * 120f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float f = Mathf.Lerp(110f, f0, Mathf.Exp(-t * 4.5f)) + Mathf.Sin(TAU * 26f * t) * 45f;
                phase += f / SR;
                float saw = (phase % 1f) * 2f - 1f;
                lp += (saw - lp) * 0.2f;
                nlp += (N() - nlp) * 0.12f;
                float env = (1f - Mathf.Exp(-t * 300f)) * Mathf.Exp(-t * 4.2f);
                d[i] = Tanh(lp * env * 1.5f + nlp * Mathf.Exp(-t * 14f) * 2.2f);
            }
            return d;
        }

        static float[] Screech()
        {
            var d = Buf(0.32f);
            float lp = 0, phase = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float k = t / 0.32f;
                float f = Mathf.Lerp(380f, 950f, k) + Mathf.Sin(TAU * 34f * t) * 60f;
                phase += f / SR;
                float saw = (phase % 1f) * 2f - 1f;
                lp += (saw + N() * 0.3f - lp) * 0.3f;
                d[i] = lp * Mathf.Sin(k * Mathf.PI);
            }
            return d;
        }

        static float[] Spit()
        {
            var d = Buf(0.2f);
            float lp = 0, phase = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                phase += TAU * Mathf.Lerp(160f, 750f, Mathf.Exp(-t * 18f)) / SR;
                lp += (N() - lp) * 0.25f;
                d[i] = Mathf.Sin(phase) * Mathf.Exp(-t * 15f) + lp * Mathf.Exp(-t * 35f) * 1.5f;
            }
            return d;
        }

        static float[] Click(float len, float freq, float second)
        {
            var d = Buf(len);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float c = N() * Mathf.Exp(-t * 300f) + Mathf.Sin(TAU * freq * t) * Mathf.Exp(-t * 90f) * 0.5f;
                if (second > 0f && t > second)
                {
                    float t2 = t - second;
                    c += N() * Mathf.Exp(-t2 * 350f) * 0.8f + Mathf.Sin(TAU * freq * 1.3f * t2) * Mathf.Exp(-t2 * 100f) * 0.4f;
                }
                d[i] = c;
            }
            return d;
        }

        static float[] Clack()
        {
            var d = Buf(0.18f);
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                lp += (N() - lp) * 0.4f;
                float c = lp * Mathf.Exp(-t * 160f) * 1.4f + Mathf.Sin(TAU * 820f * t) * Mathf.Exp(-t * 50f) * 0.5f;
                if (t > 0.06f)
                {
                    float t2 = t - 0.06f;
                    c += N() * Mathf.Exp(-t2 * 250f) + Mathf.Sin(TAU * 3100f * t2) * Mathf.Exp(-t2 * 45f) * 0.35f;
                }
                d[i] = c;
            }
            return d;
        }

        static float[] Tone(float len, float freq, float attack)
        {
            var d = Buf(len);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float env = Mathf.Min(1f, t / attack) * Mathf.Exp(-t * 25f);
                d[i] = Mathf.Sin(TAU * freq * t) * env;
            }
            return d;
        }

        static float[] Sweep(float len, float f0, float f1)
        {
            var d = Buf(len);
            float phase = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                phase += TAU * Mathf.Lerp(f0, f1, t / len) / SR;
                d[i] = Mathf.Sin(phase) * Mathf.Sin(Mathf.PI * t / len);
            }
            return d;
        }

        static float[] Whoosh(float len, float c0, float c1)
        {
            var d = Buf(len);
            float lp = 0, lp2 = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float k = t / len;
                float coef = Mathf.Lerp(c0, c1, Mathf.Sin(k * Mathf.PI));
                lp += (N() - lp) * coef;
                lp2 += (lp - lp2) * coef;
                float env = Mathf.Sin(k * Mathf.PI);
                d[i] = (lp - lp2 * 0.5f) * env * env;
            }
            return d;
        }

        /// <summary>Armoured boot on regolith: low body thud, gravel crunch, faint plate rattle.</summary>
        static float[] Step(int v)
        {
            var d = Buf(0.2f);
            float lp = 0, lp2 = 0, phase = 0;
            float f0 = 62f + v * 6f, rattle = 1500f + v * 230f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float n = N();
                lp += (n - lp) * 0.18f;
                lp2 += (lp - lp2) * 0.3f;
                phase += TAU * Mathf.Lerp(f0, f0 * 0.6f, Mathf.Clamp01(t * 12f)) / SR;
                float thud = Mathf.Sin(phase) * Mathf.Exp(-t * 32f) * (1f - Mathf.Exp(-t * 900f));
                float crunch = lp2 * Mathf.Exp(-t * 38f) * 2.2f + n * Mathf.Exp(-t * 160f) * 0.25f;
                float clank = Mathf.Sin(TAU * rattle * t) * Mathf.Exp(-t * 70f) * 0.12f * (t > 0.012f ? 1f : 0f);
                d[i] = Tanh(thud * 1.4f + crunch + clank);
            }
            return d;
        }

        /// <summary>Thin moon wind: band-passed noise with slow gusts and a distant resonant hum (loops seamlessly).</summary>
        static float[] AmbWind()
        {
            const float len = 12f;
            var d = Buf(len);
            float lp1 = 0, lp2 = 0, bp = 0, ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float n = N();
                lp1 += (n - lp1) * 0.02f;
                lp2 += (lp1 - lp2) * 0.05f;
                bp = lp1 - lp2;
                float gust = 0.55f + 0.3f * Mathf.Sin(TAU * t / len * 2f) + 0.15f * Mathf.Sin(TAU * t / len * 5f + 1f);
                ph += TAU * (55f + 0.6f * Mathf.Sin(TAU * t / len)) / SR;
                float hum = Mathf.Sin(ph) * 0.12f + Mathf.Sin(ph * 2.01f) * 0.05f;
                d[i] = bp * 14f * gust + hum;
            }
            for (int i = 0; i < 4410; i++) { float k = i / 4410f; int j = d.Length - 4410 + i; d[j] = d[j] * (1 - k) + d[i] * k; }
            return d;
        }

        /// <summary>Combat tension bed: low pulsing synth (two detuned saws, filtered) at ~100 bpm.</summary>
        static float[] AmbTension()
        {
            const float bpm = 100f, len = 60f / bpm * 8f;
            var d = Buf(len);
            float lp = 0, p1 = 0, p2 = 0, sub = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float beat = (t * bpm / 60f) % 1f;
                p1 = (p1 + 55f / SR) % 1f; p2 = (p2 + 55.6f / SR) % 1f;
                float saw = (p1 * 2f - 1f) + (p2 * 2f - 1f);
                lp += (saw - lp) * (0.02f + 0.05f * Mathf.Exp(-beat * 6f));
                sub += TAU * 41.2f / SR;
                float pulse = Mathf.Exp(-beat * 5f);
                float tick = ((t * bpm / 60f * 2f) % 1f) < 0.02f ? N() * 0.15f : 0f;
                d[i] = lp * 0.5f * (0.4f + 0.6f * pulse) + Mathf.Sin(sub) * 0.35f * pulse + tick;
            }
            return d;
        }

        /// <summary>Sub-bass punch layered under every shot (gives energy weapons physical weight).</summary>
        static float[] Thump()
        {
            var d = Buf(0.16f);
            float phase = 0, lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                lp += (N() - lp) * 0.08f;
                phase += TAU * Mathf.Lerp(130f, 42f, Mathf.Clamp01(t * 14f)) / SR;
                d[i] = Tanh((Mathf.Sin(phase) * Mathf.Exp(-t * 26f) * (1f - Mathf.Exp(-t * 1500f)) + lp * Mathf.Exp(-t * 60f) * 1.5f) * 1.8f);
            }
            return d;
        }

        static float[] Clink()
        {
            var d = Buf(0.14f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                d[i] = Mathf.Sin(TAU * 2900f * t) * Mathf.Exp(-t * 40f) + Mathf.Sin(TAU * 4170f * t) * Mathf.Exp(-t * 55f) * 0.6f + N() * Mathf.Exp(-t * 500f) * 0.5f;
            }
            return d;
        }

        static float[] Hurt()
        {
            var d = Buf(0.32f);
            float lp = 0, phase = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                phase += TAU * Mathf.Lerp(50f, 130f, Mathf.Exp(-t * 9f)) / SR;
                lp += (N() - lp) * 0.1f;
                d[i] = Tanh((Mathf.Sin(phase) * Mathf.Exp(-t * 9f) * 1.5f + lp * Mathf.Exp(-t * 18f) * 3f) * 2f);
            }
            return d;
        }

        static float[] PlayerDeath()
        {
            var d = Buf(1.3f);
            float lp = 0, phase = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                phase += TAU * Mathf.Lerp(40f, 420f, Mathf.Exp(-t * 2.5f)) / SR;
                lp += (N() - lp) * 0.04f;
                float sq = Mathf.Sign(Mathf.Sin(phase)) * 0.3f + Mathf.Sin(phase) * 0.7f;
                d[i] = Tanh((sq * Mathf.Exp(-t * 2.2f) + lp * 4f * Mathf.Exp(-t * 3f)) * 1.4f);
            }
            return d;
        }

        static float[] Arp(float[] notes, float step)
        {
            var d = Buf(step * notes.Length + 0.25f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float s = 0f;
                for (int n = 0; n < notes.Length; n++)
                {
                    float tn = t - n * step;
                    if (tn < 0f) continue;
                    float env = Mathf.Min(1f, tn / 0.005f) * Mathf.Exp(-tn * 9f);
                    s += (Mathf.Sin(TAU * notes[n] * tn) + 0.3f * Mathf.Sin(TAU * notes[n] * 2f * tn)) * env;
                }
                d[i] = s;
            }
            return d;
        }

        static float[] Portal()
        {
            var d = Buf(0.9f);
            float phase = 0, phase2 = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float k = t / 0.9f;
                float f = 170f + 650f * Mathf.Pow(k, 1.6f);
                phase += TAU * f / SR;
                phase2 += TAU * f * 1.51f / SR;
                float trem = 0.6f + 0.4f * Mathf.Sin(TAU * Mathf.Lerp(8f, 30f, k) * t);
                d[i] = (Mathf.Sin(phase) + 0.4f * Mathf.Sin(phase2)) * trem * Mathf.Sin(k * Mathf.PI);
            }
            return d;
        }

        static float[] Alarm()
        {
            var d = Buf(1.0f);
            float phase = 0, lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float f = ((int)(t / 0.25f) % 2 == 0) ? 520f : 390f;
                phase += f / SR;
                float sq = (phase % 1f) < 0.5f ? 1f : -1f;
                lp += (sq - lp) * 0.08f;
                d[i] = lp * Mathf.Min(1f, t / 0.01f) * Mathf.Clamp01((1f - t) / 0.2f);
            }
            return d;
        }
    }
}
