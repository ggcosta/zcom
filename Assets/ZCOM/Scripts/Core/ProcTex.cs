using UnityEngine;

namespace Zcom
{
    /// <summary>Procedurally generated FX textures (glows, flashes, rings, splats). Runtime only, cached.</summary>
    public static class ProcTex
    {
        static Texture2D glow, flash, ring, disc, smoke, blob, splat, vignette, dash, ringThin, beamStrip;
        static Sprite white, vignetteSprite, ringSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            glow = flash = ring = disc = smoke = blob = splat = vignette = dash = ringThin = beamStrip = null;
            white = vignetteSprite = ringSprite = null;
        }

        delegate float AlphaFn(float u, float v, float r);

        static Texture2D Make(string name, int size, AlphaFn fn)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Clamp01(fn(u, v, r));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        }

        static float Smooth(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        public static Texture2D Glow => glow ? glow : glow = Make("Glow", 64, (u, v, r) => Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f));

        public static Texture2D Blob => blob ? blob : blob = Make("Blob", 32, (u, v, r) => Smooth(1f, 0.55f, r));

        public static Texture2D Flash => flash ? flash : flash = Make("Flash", 128, (u, v, r) =>
        {
            float ang = Mathf.Atan2(v, u);
            float spikes = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 2f)), 60f) * Mathf.Clamp01(1f - r);
            float spikes2 = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 2f + 0.785f)), 120f) * Mathf.Clamp01(1f - r * 1.6f) * 0.6f;
            float core = Mathf.Pow(Mathf.Clamp01(1f - r * 1.8f), 1.5f);
            float halo = Mathf.Pow(Mathf.Clamp01(1f - r), 3f) * 0.35f;
            return Mathf.Max(core, spikes, spikes2) + halo;
        });

        public static Texture2D Ring => ring ? ring : ring = Make("Ring", 128, (u, v, r) =>
        {
            float band = Mathf.Exp(-Mathf.Pow((r - 0.86f) / 0.07f, 2f));
            float inner = r < 0.86f ? Mathf.Pow(r / 0.86f, 6f) * 0.25f : 0f;
            return band + inner;
        });

        public static Texture2D RingThin => ringThin ? ringThin : ringThin = Make("RingThin", 128, (u, v, r) =>
            Mathf.Exp(-Mathf.Pow((r - 0.9f) / 0.035f, 2f)));

        public static Texture2D Disc => disc ? disc : disc = Make("Disc", 128, (u, v, r) =>
            r < 0.97f ? 0.3f + 0.7f * Smooth(0.7f, 0.97f, r) : Smooth(1f, 0.97f, r));

        public static Texture2D Smoke => smoke ? smoke : smoke = Make("Smoke", 64, (u, v, r) =>
        {
            float n = Mathf.PerlinNoise(u * 2.3f + 10f, v * 2.3f + 10f);
            return Mathf.Pow(Mathf.Clamp01(1f - r), 1.4f) * (0.45f + 0.75f * n);
        });

        public static Texture2D Splat => splat ? splat : splat = MakeSplat();

        static Texture2D MakeSplat()
        {
            var rnd = new System.Random(7);
            var drops = new Vector3[9];
            for (int i = 0; i < drops.Length; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float d = 0.55f + (float)rnd.NextDouble() * 0.35f;
                drops[i] = new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0.04f + (float)rnd.NextDouble() * 0.08f);
            }
            return Make("Splat", 128, (u, v, r) =>
            {
                float ang = Mathf.Atan2(v, u);
                float edge = 0.48f + 0.16f * Mathf.PerlinNoise(Mathf.Cos(ang) * 1.7f + 3f, Mathf.Sin(ang) * 1.7f + 3f)
                                   + 0.06f * Mathf.Sin(ang * 7f);
                float a = Smooth(edge + 0.04f, edge - 0.04f, r);
                foreach (var dpt in drops)
                {
                    float dd = Mathf.Sqrt((u - dpt.x) * (u - dpt.x) + (v - dpt.y) * (v - dpt.y));
                    a = Mathf.Max(a, Smooth(dpt.z + 0.02f, dpt.z - 0.02f, dd));
                }
                return a * 0.95f;
            });
        }

        public static Texture2D Vignette => vignette ? vignette : vignette = Make("Vignette", 256, (u, v, r) => Smooth(0.75f, 1.5f, r));

        public static Texture2D Dash
        {
            get
            {
                if (dash) return dash;
                dash = new Texture2D(32, 4, TextureFormat.RGBA32, false) { name = "Dash", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
                var px = new Color32[32 * 4];
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 32; x++)
                        px[y * 32 + x] = new Color32(255, 255, 255, (byte)(x < 18 ? 255 : 0));
                dash.SetPixels32(px);
                dash.Apply(false, false);
                return dash;
            }
        }

        /// <summary>For LineRenderers: bright along the centre line, soft falloff across the width.</summary>
        public static Texture2D BeamStrip
        {
            get
            {
                if (beamStrip) return beamStrip;
                beamStrip = new Texture2D(4, 64, TextureFormat.RGBA32, false) { name = "BeamStrip", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[4 * 64];
                for (int y = 0; y < 64; y++)
                {
                    float v = (y + 0.5f) / 64f * 2f - 1f;
                    float a = Mathf.Exp(-v * v * 5f) * 0.85f + Mathf.Exp(-v * v * 60f) * 0.6f;
                    for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
                beamStrip.SetPixels32(px);
                beamStrip.Apply(false, false);
                return beamStrip;
            }
        }

        public static Sprite WhiteSprite => white ? white : white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        public static Sprite VignetteSprite => vignetteSprite ? vignetteSprite : vignetteSprite = Sprite.Create(Vignette, new Rect(0, 0, 256, 256), new Vector2(0.5f, 0.5f));
        public static Sprite RingSprite => ringSprite ? ringSprite : ringSprite = Sprite.Create(RingThin, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f));
    }
}
