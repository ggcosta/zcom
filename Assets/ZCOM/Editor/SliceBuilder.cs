using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Zcom.EditorTools
{
    /// <summary>
    /// Generates the whole vertical-slice scene (lighting, post-processing, arena, player, managers).
    /// Menu: ZCOM > Build Vertical Slice Scene. Re-run any time; it regenerates everything under Assets/ZCOM/Generated.
    /// </summary>
    public static class SliceBuilder
    {
        public const string ScenePath = "Assets/ZCOM/Scenes/VerticalSlice.unity";
        const string GenDir = "Assets/ZCOM/Generated";

        [MenuItem("ZCOM/Build Vertical Slice Scene", priority = 0)]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        /// <summary>Batch-mode entry point: -executeMethod Zcom.EditorTools.SliceBuilder.BuildBatch</summary>
        public static void BuildBatch()
        {
            Build();
            EditorApplication.Exit(0);
        }

        public static void Build()
        {
            MarshalAnimSetup.EnsureController();
            PropSetup.EnsureMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnsureFolder("Assets/ZCOM/Scenes");
            if (AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.DeleteAsset(GenDir);
            EnsureFolder(GenDir);
            EnsureFolder(GenDir + "/Materials");
            EnsureFolder(GenDir + "/Textures");

            // ---------- Environment lighting ----------
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.26f, 0.29f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.15f, 0.14f, 0.21f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.04f, 0.07f);
            // low purple haze: distant cliffs fade, light sources bloom into it
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0105f;
            RenderSettings.fogColor = new Color(0.075f, 0.065f, 0.11f);

            var sunGo = new GameObject("Sun");
            sunGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.86f, 0.88f, 1f);
            sun.intensity = 2.1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            RenderSettings.sun = sun;

            var rimGo = new GameObject("RimLight");
            rimGo.transform.rotation = Quaternion.Euler(25f, 150f, 0f);
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(0.65f, 0.35f, 1f);
            rim.intensity = 0.45f;
            rim.shadows = LightShadows.None;

            // ---------- Post processing ----------
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, GenDir + "/SlicePostFX.asset");
            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(false);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            var bloom = Add<Bloom>();
            bloom.intensity.Override(1.15f);
            bloom.threshold.Override(0.95f);
            bloom.scatter.Override(0.68f);
            var tone = Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            var color = Add<ColorAdjustments>();
            color.postExposure.Override(0.5f);
            color.contrast.Override(14f);
            color.saturation.Override(12f);
            var vig = Add<Vignette>();
            vig.intensity.Override(0.32f);
            vig.smoothness.Override(0.45f);
            var ca = Add<ChromaticAberration>();
            ca.intensity.Override(0.08f);
            var grain = Add<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.12f);
            EditorUtility.SetDirty(profile);

            var volGo = new GameObject("PostFX");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            // ---------- Map ----------
            var albedo = SaveTexture(MakeDeckAlbedo(), "DeckAlbedo", true);
            var emissive = SaveTexture(MakeDeckEmission(), "DeckEmission", true);
            var groundTex = SaveTexture(MakeGroundAlbedo(), "Regolith", true);
            MapBuilder.RegisterAsset = o => AssetDatabase.CreateAsset(o, $"{GenDir}/{o.name}.{(o is TerrainData ? "asset" : "terrainlayer")}");
            var map = MapBuilder.Build(groundTex, albedo, emissive);
            MapBuilder.RegisterAsset = null;
            if (MapBuilder.TerrainDataOut) EditorUtility.SetDirty(MapBuilder.TerrainDataOut);
            AssetDatabase.SaveAssets();

            // ---------- Player ----------
            var player = PlayerFactory.Build(map.spawn);
            player.transform.rotation = map.spawnRotation;

            // ---------- Camera ----------
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.008f, 0.012f, 0.03f);
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 900f;
            cam.fieldOfView = 30f;
            camGo.AddComponent<AudioListener>();
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var rig = camGo.AddComponent<CameraRig>();
            rig.target = player.transform;
            {
                Quaternion rot = Quaternion.Euler(rig.pitch, rig.yaw, 0f);
                camGo.transform.SetPositionAndRotation(player.transform.position - rot * Vector3.forward * rig.distance, rot);
            }

            // ---------- Managers ----------
            var systems = new GameObject("Systems");
            var bank = systems.AddComponent<ShaderBank>();
            bank.lit = Shader.Find("Universal Render Pipeline/Lit");
            bank.fxAdditive = Shader.Find("ZCOM/FxAdditive");
            bank.fxAlpha = Shader.Find("ZCOM/FxAlpha");
            systems.AddComponent<GameFeel>();
            systems.AddComponent<Vfx>();
            systems.AddComponent<ProcAudio>();
            systems.AddComponent<Hud>();
            var gm = systems.AddComponent<GameManager>();
            gm.navSurface = map.surface;
            gm.player = player;

            // ---------- Persist generated materials ----------
            PersistMaterials();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != ScenePath) scenes.Add(s);
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[ZCOM] Vertical slice scene built at {ScenePath}. Press Play!");
        }

        static void PersistMaterials()
        {
            var saved = new Dictionary<Material, string>();
            var names = new HashSet<string>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || saved.ContainsKey(m) || AssetDatabase.Contains(m)) continue;
                    string baseName = string.IsNullOrEmpty(m.name) ? "Mat" : m.name;
                    foreach (char ch in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(ch, '_');
                    baseName = baseName.Replace('/', '_').Replace(' ', '_');
                    string n = baseName;
                    int i = 1;
                    while (!names.Add(n)) n = baseName + "_" + (i++);
                    string path = $"{GenDir}/Materials/{n}.mat";
                    AssetDatabase.CreateAsset(m, path);
                    saved[m] = path;
                }
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Texture2D SaveTexture(Texture2D tex, string name, bool srgb)
        {
            string path = $"{GenDir}/Textures/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = srgb;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 8;
            imp.filterMode = FilterMode.Trilinear;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // One texture tile = 4m x 4m of deck: 2x2 plates with seams, bolts, wear, and hazard trims.
        const int TexSize = 512;

        static Texture2D MakeDeckAlbedo()
        {
            var t = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, true);
            var px = new Color32[TexSize * TexSize];
            var rnd = new System.Random(3);
            float[] plateTone = new float[4];
            for (int i = 0; i < 4; i++) plateTone[i] = 0.85f + (float)rnd.NextDouble() * 0.3f;
            int half = TexSize / 2;
            for (int y = 0; y < TexSize; y++)
                for (int x = 0; x < TexSize; x++)
                {
                    int px0 = x % half, py0 = y % half;
                    int plate = (x / half) + 2 * (y / half);
                    float baseV = 0.2f * plateTone[plate];
                    float noise = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.04f + Mathf.PerlinNoise(x * 0.4f, y * 0.4f) * 0.02f;
                    float scratch = Mathf.PerlinNoise(x * 0.9f, y * 0.02f) > 0.82f ? 0.03f : 0f;
                    float v = baseV + noise + scratch;
                    // inner panel inset line
                    int inset = 22;
                    bool insetLine = (px0 == inset || px0 == half - inset || py0 == inset || py0 == half - inset)
                                     && px0 >= inset && px0 <= half - inset && py0 >= inset && py0 <= half - inset;
                    if (insetLine) v *= 0.6f;
                    // seams
                    int seam = Mathf.Min(Mathf.Min(px0, half - 1 - px0), Mathf.Min(py0, half - 1 - py0));
                    if (seam < 3) v = 0.04f;
                    else if (seam < 5) v *= 1.35f;
                    // bolts in plate corners
                    foreach (var (bx, by) in new[] { (12, 12), (half - 13, 12), (12, half - 13), (half - 13, half - 13) })
                    {
                        float d = Mathf.Sqrt((px0 - bx) * (px0 - bx) + (py0 - by) * (py0 - by));
                        if (d < 5f) v = d < 3.5f ? 0.32f : 0.06f;
                    }
                    Color c = new Color(v * 0.95f, v * 1.0f, v * 1.12f, 1f);
                    // hazard stripe band on one plate edge
                    if (plate == 1 && py0 > half - 30 && py0 < half - 10 && px0 > 30 && px0 < half - 30)
                        c = ((px0 + py0) / 12) % 2 == 0 ? new Color(0.55f, 0.42f, 0.08f) : new Color(0.05f, 0.05f, 0.05f);
                    px[y * TexSize + x] = c;
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>Moon dust: purple-grey regolith with soft mottling, pebbles and faint wheel/boot scuffs.</summary>
        static Texture2D MakeGroundAlbedo()
        {
            const int N = 512;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true);
            var px = new Color32[N * N];
            var rnd = new System.Random(11);
            float[] pebbles = new float[N * N];
            for (int i = 0; i < 2600; i++)
            {
                int cx = rnd.Next(N), cy = rnd.Next(N);
                float r = 1f + (float)rnd.NextDouble() * 2.5f;
                float shade = (float)rnd.NextDouble() < 0.5f ? -0.35f : 0.25f;
                for (int y = -4; y <= 4; y++)
                    for (int x = -4; x <= 4; x++)
                    {
                        float d = Mathf.Sqrt(x * x + y * y);
                        if (d > r) continue;
                        int xx = (cx + x + N) % N, yy = (cy + y + N) % N;
                        pebbles[yy * N + xx] = shade * (1f - d / (r + 0.5f));
                    }
            }
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    // tileable noise via torus mapping
                    float a = u * Mathf.PI * 2f, b = v * Mathf.PI * 2f;
                    float n1 = Mathf.PerlinNoise(Mathf.Cos(a) * 1.5f + 10f + Mathf.Cos(b) * 0.3f, Mathf.Sin(a) * 1.5f + 10f + Mathf.Sin(b) * 1.5f);
                    float n2 = Mathf.PerlinNoise(Mathf.Cos(a) * 6f + 30f, Mathf.Sin(b) * 6f + Mathf.Cos(b) * 3f + 30f);
                    float n3 = Mathf.PerlinNoise(x * 0.35f, y * 0.35f);
                    float val = 0.2f + n1 * 0.07f + n2 * 0.05f + n3 * 0.025f;
                    val *= 1f + pebbles[y * N + x];
                    px[y * N + x] = new Color(val * 1.02f, val * 0.95f, val * 1.1f, 1f);
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        static Texture2D MakeDeckEmission()
        {
            var t = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, true);
            var px = new Color32[TexSize * TexSize];
            int half = TexSize / 2;
            for (int y = 0; y < TexSize; y++)
                for (int x = 0; x < TexSize; x++)
                {
                    float e = 0f;
                    // thin light strips along the main seams, broken into segments
                    bool seamX = Mathf.Abs(y - half) < 2 && (x / 32) % 3 != 0;
                    bool seamY = Mathf.Abs(x - half) < 2 && (y / 32) % 3 == 1;
                    if (seamX || seamY) e = 0.55f;
                    // little status lights near bolts
                    int px0 = x % half, py0 = y % half;
                    float d = Mathf.Sqrt((px0 - 40) * (px0 - 40) + (py0 - 12) * (py0 - 12));
                    if (d < 2.5f && (x / half + y / half) % 2 == 0) e = 1f;
                    byte b = (byte)(e * 255);
                    px[y * TexSize + x] = new Color32(b, b, b, 255);
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }
}
