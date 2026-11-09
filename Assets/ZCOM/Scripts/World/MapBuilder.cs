using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Zcom
{
    /// <summary>
    /// Builds the small raid map "KEPLER-7": a moon surface ringed by cliffs with five areas
    /// (Landing Zone, Outpost with enterable buildings, Crash Site, Xeno Nest, Mining Ridge),
    /// alien camps guarding them and loot crates. Used by the editor scene builder; edit layout here.
    /// </summary>
    public static class MapBuilder
    {
        public const float HalfX = 46f, HalfZ = 36f;

        public struct Result
        {
            public GameObject root;
            public NavMeshSurface surface;
            public Vector3 spawn;
            public Quaternion spawnRotation;
        }

        static System.Random rng;
        static float R() => (float)rng.NextDouble();
        static float R(float a, float b) => a + (b - a) * R();

        static Material mGround, mRock, mRockDark, mFloor, mPad, mSteel, mDarkSteel, mHull, mWall, mRoof, mTrim;
        static Material mCyan, mOrange, mYellow, mRed, mWindow, mScreen, mCrate, mCrateAlt, mBed;
        static Material mCreep, mAlienSkin, mAlienDark, mAlienGlow, mGoo, mFireGlow, mChestBody, mChestGlow, mOreGold, mOreXenite, mTank;
        static List<(Vector3 pos, float radius)> keepClear;

        /// <summary>Terrain created by the last Build (the editor scene builder saves it and its layers as assets).</summary>
        public static TerrainData TerrainDataOut;
        public static TerrainLayer[] TerrainLayersOut;
        /// <summary>Editor hook: persist the TerrainData / layers as assets before heights and splat maps are written.</summary>
        public static System.Action<Object> RegisterAsset;
        static Material mMetalTile;

        public static Result Build(Texture2D groundTex, Texture2D deckAlbedo, Texture2D deckEmission)
        {
            rng = new System.Random(2077);
            keepClear = new List<(Vector3, float)>();
            CreateMaterials(groundTex, deckAlbedo, deckEmission);

            var root = new GameObject("Map");
            var t = root.transform;

            // ---------- Terrain & borders ----------
            BuildTerrain(t);
            BuildBorders(t);

            // ---------- Areas ----------
            Vector3 spawn = new Vector3(-31f, 0f, -22f);
            BuildLandingZone(t, new Vector3(-32f, 0, -25f));
            BuildWalkway(t, new Vector3(-28f, 0, -19.5f), new Vector3(-14f, 0, -4f));
            BuildOutpost(t, new Vector3(-8f, 0, 2f));
            BuildCrashSite(t, new Vector3(22f, 0, -16f));
            BuildNest(t, new Vector3(25f, 0, 19f));
            BuildMiningRidge(t, new Vector3(-27f, 0, 20f));
            BuildEvac(t, EvacPos);
            BuildScatter(t);

            root.AddComponent<AmbientDust>();

            // ---------- Navigation ----------
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

            return new Result { root = root, surface = surface, spawn = spawn, spawnRotation = Quaternion.Euler(0f, 45f, 0f) };
        }

        // ------------------------------------------------------------------ materials

        static void CreateMaterials(Texture2D groundTex, Texture2D deckAlbedo, Texture2D deckEmission)
        {
            mGround = Mats.Lit(Color.white, 0f, 0.18f); mGround.name = "Regolith";
            if (groundTex) { mGround.SetTexture("_BaseMap", groundTex); mGround.SetTextureScale("_BaseMap", new Vector2((HalfX * 2f + 16f) / 7f, (HalfZ * 2f + 16f) / 7f)); }
            mFloor = Mats.Lit(Color.white, 0.35f, 0.5f, Mats.HDR(new Color(0.1f, 0.7f, 1f), 0.8f)); mFloor.name = "DeckFloor";
            if (deckAlbedo) mFloor.SetTexture("_BaseMap", deckAlbedo);
            if (deckEmission) mFloor.SetTexture("_EmissionMap", deckEmission);
            mFloor.SetTextureScale("_BaseMap", new Vector2(1.5f, 1.5f));
            mPad = Mats.Lit(Color.white, 0.35f, 0.5f, Mats.HDR(new Color(0.1f, 0.7f, 1f), 1f)); mPad.name = "PadDeck";
            if (deckAlbedo) mPad.SetTexture("_BaseMap", deckAlbedo);
            if (deckEmission) mPad.SetTexture("_EmissionMap", deckEmission);
            mPad.SetTextureScale("_BaseMap", new Vector2(2.5f, 2.5f));

            mRock = Mats.Lit(new Color(0.27f, 0.24f, 0.28f), 0.05f, 0.2f); mRock.name = "Rock";
            mRockDark = Mats.Lit(new Color(0.16f, 0.14f, 0.17f), 0.05f, 0.15f); mRockDark.name = "RockDark";
            mSteel = Tiled(Mats.Lit(new Color(0.75f, 0.77f, 0.82f), 0.5f, 0.45f), "metal", 1f); mSteel.name = "Steel";
            mDarkSteel = Tiled(Mats.Lit(new Color(0.32f, 0.33f, 0.37f), 0.55f, 0.4f), "metal", 1f); mDarkSteel.name = "DarkSteel";
            mHull = Mats.Lit(new Color(0.55f, 0.56f, 0.6f), 0.4f, 0.45f); mHull.name = "ShipHull";
            mWall = Tiled(Mats.Lit(new Color(0.95f, 0.95f, 1f), 0.35f, 0.4f), "metal", 1f); mWall.name = "HabWall";
            mRoof = Tiled(Mats.Lit(new Color(0.55f, 0.56f, 0.62f), 0.45f, 0.35f), "metal", 1.5f); mRoof.name = "HabRoof";
            mTrim = Mats.Lit(new Color(0.85f, 0.45f, 0.12f), 0.3f, 0.5f); mTrim.name = "HabTrim";
            mCyan = Mats.Lit(Color.black, 0f, 0.8f, Mats.HDR(new Color(0.05f, 0.65f, 1f), 2.2f)); mCyan.name = "NeonCyan";
            mOrange = Mats.Lit(Color.black, 0f, 0.8f, Mats.HDR(new Color(1f, 0.35f, 0.05f), 2.2f)); mOrange.name = "NeonOrange";
            mYellow = Mats.Lit(Color.black, 0f, 0.8f, Mats.HDR(new Color(1f, 0.7f, 0.1f), 1.6f)); mYellow.name = "NeonYellow";
            mRed = Mats.Lit(Color.black, 0f, 0.8f, Mats.HDR(new Color(1f, 0.1f, 0.05f), 3f)); mRed.name = "NeonRed";
            mWindow = Mats.Lit(new Color(0.05f, 0.12f, 0.18f), 0.1f, 0.95f, Mats.HDR(new Color(0.35f, 0.75f, 1f), 0.9f)); mWindow.name = "Window";
            mScreen = Mats.Lit(new Color(0.02f, 0.05f, 0.08f), 0f, 0.9f, Mats.HDR(new Color(0.15f, 0.55f, 1f), 2.2f)); mScreen.name = "Screen";
            mCrate = Mats.Lit(new Color(0.24f, 0.27f, 0.2f), 0.2f, 0.35f); mCrate.name = "Crate";
            mCrateAlt = Mats.Lit(new Color(0.42f, 0.24f, 0.1f), 0.2f, 0.35f); mCrateAlt.name = "CrateOrange";
            mBed = Mats.Lit(new Color(0.2f, 0.32f, 0.42f), 0.05f, 0.3f); mBed.name = "Bedding";
            mAlienSkin = Mats.Lit(new Color(0.26f, 0.09f, 0.3f), 0.1f, 0.75f); mAlienSkin.name = "HiveSkin";
            mAlienDark = Mats.Lit(new Color(0.1f, 0.04f, 0.12f), 0.1f, 0.85f); mAlienDark.name = "HiveDark";
            mAlienGlow = Mats.Lit(new Color(0.1f, 0.3f, 0.05f), 0f, 0.9f, Mats.HDR(new Color(0.3f, 1f, 0.2f), 1.7f)); mAlienGlow.name = "HiveGlow";
            mGoo = Mats.Lit(new Color(0.1f, 0.25f, 0.04f), 0f, 0.95f, Mats.HDR(new Color(0.25f, 0.8f, 0.1f), 0.7f));
            mCreep = Mats.Lit(new Color(0.13f, 0.055f, 0.16f), 0.05f, 0.7f, Mats.HDR(new Color(0.3f, 0.06f, 0.4f), 0.08f)); mCreep.name = "HiveCreep"; mGoo.name = "Goo";
            mFireGlow = Mats.Lit(Color.black, 0f, 0.5f, Mats.HDR(new Color(1f, 0.4f, 0.08f), 4f)); mFireGlow.name = "FireGlow";
            mChestBody = Mats.Lit(new Color(0.2f, 0.22f, 0.26f), 0.6f, 0.45f); mChestBody.name = "ChestBody";
            mChestGlow = Mats.Lit(Color.black, 0f, 0.8f, Mats.HDR(Color.white, 2f)); mChestGlow.name = "ChestGlow";
            mOreGold = Mats.Lit(new Color(1f, 0.75f, 0.2f), 1f, 0.8f, Mats.HDR(new Color(1f, 0.7f, 0.2f), 0.8f)); mOreGold.name = "OreGold";
            mOreXenite = Mats.Lit(new Color(0.1f, 0.3f, 0.15f), 0f, 0.95f, Mats.HDR(new Color(0.35f, 1f, 0.55f), 2.5f)); mOreXenite.name = "OreXenite";
            mTank = Mats.Lit(new Color(0.1f, 0.3f, 0.12f), 0.1f, 0.95f, Mats.HDR(new Color(0.3f, 1f, 0.4f), 1.5f)); mTank.name = "SpecimenTank";
        }

        static Material Tiled(Material m, string tile, float scale)
        {
            var tex = Resources.Load<Texture2D>("Textures/Terrain/" + tile);
            var nrm = Resources.Load<Texture2D>("Textures/Terrain/" + tile + "_n");
            if (tex) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", new Vector2(scale, scale)); }
            if (nrm) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 0.8f); }
            return m;
        }

        // ------------------------------------------------------------------ terrain

        const float TerrainW = 152f, TerrainL = 132f, TerrainH = 30f, TerrainBase = 2f;
        static readonly List<(Vector2 c, float r, float depth)> craters = new List<(Vector2, float, float)>();
        static readonly List<(Vector2 c, float r)> flats = new List<(Vector2, float)>();
        static readonly List<(Vector2 a, Vector2 b, float w)> flatLines = new List<(Vector2, Vector2, float)>();
        static readonly Vector2 NestC = new Vector2(25f, 19f), CrashCrater = new Vector2(22f, -8f);
        static readonly Vector3 EvacPos = new Vector3(3f, 0f, 29f);

        static float S01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        static float FlatMask(float x, float z)
        {
            var p = new Vector2(x, z);
            float m = 0f;
            foreach (var (c, r) in flats) m = Mathf.Max(m, 1f - S01(((p - c).magnitude - r) / 3f));
            foreach (var (a, b, w) in flatLines)
            {
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                m = Mathf.Max(m, 1f - S01(((a + ab * t - p).magnitude - w) / 2.5f));
            }
            return m;
        }

        static float Outside(float x, float z)
        {
            float ex = Mathf.Max(0f, Mathf.Abs(x) - (HalfX + 0.5f)), ez = Mathf.Max(0f, Mathf.Abs(z) - (HalfZ + 0.5f));
            return Mathf.Sqrt(ex * ex + ez * ez);
        }

        /// <summary>World ground height. Play areas are flat at 0; dunes and craters in between; cliffs beyond the bounds.</summary>
        public static float Height(float x, float z)
        {
            float e = Outside(x, z);
            float n1 = Mathf.PerlinNoise(x * 0.045f + 31f, z * 0.045f + 17f), n2 = Mathf.PerlinNoise(x * 0.14f + 5f, z * 0.14f + 9f);
            float h = S01(e / 8f) * (5f + n1 * 10f + n2 * 3f) + S01((e - 8f) / 20f) * 6f;
            float inside = 1f - S01(e / 1.5f);
            float dunes = (Mathf.PerlinNoise(x * 0.06f, z * 0.06f) - 0.5f) * 0.8f + (Mathf.PerlinNoise(x * 0.2f + 3f, z * 0.2f + 7f) - 0.5f) * 0.18f;
            // talus: ground creeps up towards the cliffs
            float edge = Mathf.Min(HalfX - Mathf.Abs(x), HalfZ - Mathf.Abs(z));
            dunes += (1f - S01(edge / 6f)) * 0.9f;
            float cr = 0f;
            var p = new Vector2(x, z);
            foreach (var (c, r, depth) in craters)
            {
                float d = (p - c).magnitude / r;
                if (d < 1f) cr -= (1f - d * d) * depth;
                cr += Mathf.Exp(-((d - 1f) / 0.22f) * ((d - 1f) / 0.22f)) * depth * 0.35f;
            }
            return h + inside * (1f - FlatMask(x, z)) * (dunes + cr);
        }

        static Vector3 OnGround(Vector3 p) => new Vector3(p.x, Height(p.x, p.z), p.z);

        static void BuildTerrain(Transform t)
        {
            craters.Clear(); flats.Clear(); flatLines.Clear();
            // play areas stay perfectly flat (buildings, pads, chests, camps)
            flats.Add((new Vector2(-32f, -25f), 8.5f));       // landing zone
            flats.Add((new Vector2(-8f, 2f), 15.5f));         // outpost
            flats.Add((new Vector2(-27f, 20f), 7f));          // mining ridge rig
            flats.Add((NestC, 6f));
            flats.Add((new Vector2(26.5f, -20.5f), 2.5f));    // crash-site crate
            flats.Add((new Vector2(21f, -19f), 4f));          // crash-site camp
            flatLines.Add((new Vector2(-28f, -19.5f), new Vector2(-14f, -4f), 2f));
            flats.Add((new Vector2(EvacPos.x, EvacPos.z), 6f));                // evac point
            craters.Add((CrashCrater, 8.5f, 1.3f));
            var rr = new System.Random(77);
            for (int i = 0; i < 40 && craters.Count < 14; i++)
            {
                var c = new Vector2((float)(rr.NextDouble() * 2 - 1) * (HalfX - 6f), (float)(rr.NextDouble() * 2 - 1) * (HalfZ - 6f));
                float r = 2f + (float)rr.NextDouble() * 3.5f;
                if (FlatMask(c.x, c.y) > 0.01f || (c - CrashCrater).magnitude < 12f) continue;
                craters.Add((c, r, 0.35f + r * 0.08f));
            }

            const int res = 257;
            var td = new TerrainData { heightmapResolution = res };
            td.name = "KeplerTerrain";
            RegisterAsset?.Invoke(td);
            td.size = new Vector3(TerrainW, TerrainH, TerrainL);
            var heights = new float[res, res];
            for (int iz = 0; iz < res; iz++)
                for (int ix = 0; ix < res; ix++)
                {
                    float x = -TerrainW / 2f + TerrainW * ix / (res - 1), z = -TerrainL / 2f + TerrainL * iz / (res - 1);
                    heights[iz, ix] = Mathf.Clamp01((Height(x, z) + TerrainBase) / TerrainH);
                }
            td.SetHeights(0, 0, heights);

            TerrainLayer Layer(string tex, float tile, Vector4 tint, float smooth)
            {
                var l = new TerrainLayer
                {
                    diffuseTexture = Resources.Load<Texture2D>("Textures/Terrain/" + tex),
                    normalMapTexture = Resources.Load<Texture2D>("Textures/Terrain/" + tex + "_n"),
                    tileSize = new Vector2(tile, tile),
                    normalScale = 1f,
                    smoothness = smooth,
                    metallic = 0f,
                    diffuseRemapMax = tint,
                };
                l.name = "Layer_" + tex + "_" + tile.ToString(System.Globalization.CultureInfo.InvariantCulture);
                RegisterAsset?.Invoke(l);
                return l;
            }
            var layers = new[]
            {
                // tints pull the lavender source textures back to a warm grey dust so the lighting (not the albedo) sets the colour
                Layer("regolith", 6f, new Vector4(0.8f, 0.9f, 0.74f, 1f), 0.08f),
                Layer("rock", 7f, new Vector4(0.9f, 0.88f, 0.9f, 1f), 0.15f),
                Layer("regolith", 4.5f, new Vector4(0.41f, 0.46f, 0.38f, 1f), 0.05f),   // scorched / crater floor / dark dust drifts
                Layer("hive", 4f, new Vector4(1f, 1f, 1f, 1f), 0.45f),
            };
            td.alphamapResolution = 256;
            td.terrainLayers = layers;
            int ar = td.alphamapResolution;
            var alpha = new float[ar, ar, 4];
            for (int iz = 0; iz < ar; iz++)
                for (int ix = 0; ix < ar; ix++)
                {
                    float x = -TerrainW / 2f + TerrainW * (ix + 0.5f) / ar, z = -TerrainL / 2f + TerrainL * (iz + 0.5f) / ar;
                    float hx = Height(x + 0.5f, z) - Height(x - 0.5f, z), hz = Height(x, z + 0.5f) - Height(x, z - 0.5f);
                    float slope = new Vector2(hx, hz).magnitude;           // rise per metre
                    float n = Mathf.PerlinNoise(x * 0.11f + 50f, z * 0.11f + 20f);
                    float rock = S01((slope - 0.35f) / 0.5f) + S01((Outside(x, z) - 1f) / 4f) * 0.8f;
                    float dark = 0f;
                    foreach (var (c, r, depth) in craters)
                        dark = Mathf.Max(dark, 1f - S01(((new Vector2(x, z) - c).magnitude / r - 0.55f) / 0.5f));
                    dark = Mathf.Max(dark, (1f - S01(((new Vector2(x, z) - new Vector2(-35.5f, -28.5f)).magnitude - 2.5f) / 2f)) * 0.8f);   // drop-pod scorch
                    dark *= 0.55f + 0.45f * n;
                    // large-scale variation so the plains are not one flat colour: dark dust drifts and bare rock patches
                    float drift = Mathf.PerlinNoise(x * 0.043f + 7f, z * 0.043f + 13f);
                    float bare = Mathf.PerlinNoise(x * 0.06f + 91f, z * 0.06f + 37f);
                    dark = Mathf.Max(dark, S01((drift - 0.54f) / 0.22f) * 0.55f);
                    rock += S01((bare - 0.62f) / 0.18f) * 0.45f;
                    float hive = 1f - S01(((new Vector2(x, z) - NestC).magnitude + (n - 0.5f) * 6f - 10f) / 3.5f);
                    float reg = 0.35f + n * 0.3f;
                    float sum = reg + rock + dark + hive;
                    alpha[iz, ix, 0] = reg / sum; alpha[iz, ix, 1] = rock / sum; alpha[iz, ix, 2] = dark / sum; alpha[iz, ix, 3] = hive / sum;
                }
            td.SetAlphamaps(0, 0, alpha);

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain";
            go.transform.SetParent(t, false);
            go.transform.position = new Vector3(-TerrainW / 2f, -TerrainBase, -TerrainL / 2f);
            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 2f;
            terrain.basemapDistance = 250f;
            terrain.drawInstanced = true;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            TerrainDataOut = td;
            TerrainLayersOut = layers;
        }

        // ------------------------------------------------------------------ generated props

        /// <summary>AI-sculpted, Blender-baked prop (Resources/Models/Prop_*) placed on the ground.</summary>
        static GameObject Prop(string name, Transform parent, Vector3 pos, float yaw, float scale = 1f, bool collider = true, float sink = 0.05f, Vector3 tilt = default)
        {
            var mat = Resources.Load<Material>("Materials/Props/" + name);
            var go = CharacterModels.Spawn("Prop_" + name, parent, mat ? new Dictionary<string, Material> { { "Prop_" + name, mat } } : null);
            if (!go) return null;
            go.transform.position = OnGround(pos) - Vector3.up * sink * scale;
            go.transform.rotation = Quaternion.Euler(tilt.x, yaw, tilt.z);
            go.transform.localScale = Vector3.one * scale;
            if (collider)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            return go;
        }

        // ------------------------------------------------------------------ hab kit (Tools/Blender/zcom_habkit.py)

        static GameObject habKit;
        static Dictionary<string, Material> kitMats;

        static GameObject Kit(string piece, Transform parent, Vector3 localPos, float localYaw, Vector3 scale = default, bool shadows = true)
        {
            if (!habKit) habKit = Resources.Load<GameObject>("Models/HabKit");
            if (!habKit) return null;
            var src = habKit.transform.Find(piece);
            if (!src) return null;
            if (kitMats == null)
            {
                var glass = Mats.Lit(new Color(0.02f, 0.1f, 0.14f), 0.1f, 0.95f, Mats.HDR(new Color(0.3f, 0.85f, 1f), 2.4f)); glass.name = "HabGlass";
                var amber = Mats.Lit(Color.black, 0f, 0.8f, Mats.HDR(new Color(1f, 0.6f, 0.15f), 3f)); amber.name = "HabAmber";
                var red = Mats.Lit(Color.black, 0f, 0.8f, Mats.HDR(new Color(1f, 0.1f, 0.05f), 4f)); red.name = "HabRed";
                var trim = Tiled(Mats.Lit(new Color(0.85f, 0.38f, 0.08f), 0.25f, 0.45f), "metal_dark", 1f); trim.SetTexture("_BaseMap", null); trim.name = "HabTrim";
                // plain dark steel: the panel texture tiled over a 2 m grate reads as a checkerboard from the game camera
                var grate = Tiled(Mats.Lit(new Color(0.1f, 0.105f, 0.12f), 0.75f, 0.5f), "metal_dark", 0.5f); grate.SetTexture("_BaseMap", null); grate.name = "HabGrate";
                kitMats = new Dictionary<string, Material>
                {
                    { "HabWall", Tiled(Mats.Lit(Color.white, 0.3f, 0.42f), "metal_light", 1f) },
                    { "HabFrame", Tiled(Mats.Lit(Color.white, 0.5f, 0.45f), "metal_dark", 1f) },
                    { "HabRoof", Tiled(Mats.Lit(new Color(0.55f, 0.55f, 0.6f), 0.45f, 0.35f), "metal", 1f) },
                    { "HabGrate", grate },
                    { "HabTrim", trim }, { "HabGlass", glass }, { "HabAmber", amber }, { "HabRed", red },
                };
            }
            var go = Object.Instantiate(src.gameObject, parent, false);
            go.name = piece;
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, localYaw, 0f) * src.localRotation;
            if (scale != default) go.transform.localScale = Vector3.Scale(src.localScale, scale);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                    if (ms[i] && kitMats.TryGetValue(ms[i].name.Replace(" (Instance)", ""), out var m)) ms[i] = m;
                r.sharedMaterials = ms;
                r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return go;
        }

        static void HideRenderer(GameObject go) { var r = go.GetComponent<Renderer>(); if (r) r.enabled = false; }

        // ------------------------------------------------------------------ building blocks

        /// <summary>Solid box with a collider (blocks movement, bullets and line of sight; carves the navmesh).</summary>
        static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material m, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        /// <summary>Decoration without collider.</summary>
        static GameObject Visual(string name, Transform parent, Vector3 localPos, Vector3 scale, Material m, PrimitiveType type = PrimitiveType.Cube, Vector3 euler = default, bool shadows = true) =>
            Prims.Part(name, type, parent, localPos, scale, m, euler, shadows);

        static Light PointLight(string name, Transform parent, Vector3 localPos, Color c, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        /// <summary>Downward spot (lamp heads): a defined pool of light on the ground, with soft shadows.</summary>
        static Light SpotLight(string name, Transform parent, Vector3 localPos, Color c, float intensity, float range, float angle = 110f, bool shadows = true)
        {
            var l = PointLight(name, parent, localPos, c, intensity, range);
            l.type = LightType.Spot;
            l.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.5f;
            if (shadows)
            {
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 0.8f;
                l.shadowNearPlane = 0.4f;
            }
            return l;
        }

        /// <summary>Low cover wall: one box collider, dressed with a plinth, hazard band, cap, end posts and a light strip.</summary>
        static void Barricade(string name, Transform parent, Vector3 localPos, float yaw, float length = 2.6f)
        {
            HideRenderer(Box(name, parent, localPos + new Vector3(0, 0.55f, 0), new Vector3(length, 1.1f, 0.3f), mSteel, new Vector3(0, yaw, 0)));
            var v = new GameObject(name + "Visual").transform;
            v.SetParent(parent, false);
            v.localPosition = localPos;
            v.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Visual("Plinth", v, new Vector3(0, 0.09f, 0), new Vector3(length + 0.1f, 0.18f, 0.52f), mDarkSteel);
            Visual("Body", v, new Vector3(0, 0.57f, 0), new Vector3(length, 0.8f, 0.22f), mSteel);
            Visual("Band", v, new Vector3(0, 0.36f, 0), new Vector3(length - 0.5f, 0.16f, 0.27f), mTrim);
            Visual("Cap", v, new Vector3(0, 1.02f, 0), new Vector3(length + 0.06f, 0.12f, 0.36f), mDarkSteel);
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Visual("Post", v, new Vector3(sgn * (length / 2f - 0.1f), 0.57f, 0), new Vector3(0.22f, 1.08f, 0.42f), mDarkSteel);
                Visual("Strip", v, new Vector3(0, 0.82f, sgn * 0.115f), new Vector3(length * 0.45f, 0.035f, 0.02f), mCyan, PrimitiveType.Cube, default, false);
            }
        }

        static Transform Group(string name, Transform parent, Vector3 pos, float yaw = 0f)
        {
            var g = new GameObject(name).transform;
            g.SetParent(parent, false);
            g.position = pos;
            g.rotation = Quaternion.Euler(0f, yaw, 0f);
            return g;
        }

        static void Rock(Transform parent, Vector3 pos, float size, float height, Material m = null)
        {
            string kind = height > size * 1.1f ? "RockSpire" : height < size * 0.45f ? "RockSlab" : "RockBoulder";
            float baseSize = kind == "RockSpire" ? 7.5f : kind == "RockSlab" ? 5f : 4.2f;
            float sc = kind == "RockSpire" ? height / 7.5f : size / baseSize * 0.8f;
            Prop(kind, parent, pos, R(0f, 360f), Mathf.Clamp(sc, 0.25f, 2.5f), true, 0.08f);
        }

        static void Crater(Transform parent, Vector3 pos, float radius) { }   // craters are sculpted into the terrain

        static void Lamp(Transform parent, Vector3 pos, Color color, Material neon)
        {
            var g = Group("Lamp", parent, OnGround(pos), R(0f, 360f));
            var post = Box("Post", g, new Vector3(0, 1.8f, 0), new Vector3(0.3f, 3.6f, 0.3f), mDarkSteel);
            if (Kit("Lamp", g, Vector3.zero, 0f)) HideRenderer(post);
            else Visual("Head", g, new Vector3(0, 3.45f, 0.7f), new Vector3(0.35f, 0.1f, 0.25f), neon, PrimitiveType.Cube, default, false);
            SpotLight("Light", g, new Vector3(0, 3.3f, 0.9f), color, 30f, 12f);
        }

        static void Barrel(Transform parent, Vector3 localPos, Material m)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            b.name = "Barrel";
            b.transform.SetParent(parent, false);
            b.transform.localPosition = localPos + new Vector3(0, 0.55f, 0);
            b.transform.localScale = new Vector3(0.7f, 0.55f, 0.7f);
            b.GetComponent<MeshRenderer>().sharedMaterial = m;
            Visual("Band", b.transform, new Vector3(0, 0.5f, 0), new Vector3(1.02f, 0.08f, 1.02f), mDarkSteel, PrimitiveType.Cylinder);
        }

        static void Zone(Transform parent, string name, string subtitle, Vector3 pos, float radius, Color color)
        {
            var g = new GameObject($"Zone_{name}");
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            var z = g.AddComponent<MapZone>();
            z.zoneName = name;
            z.subtitle = subtitle;
            z.radius = radius;
            z.color = color;
            keepClear.Add((pos, radius));
        }

        static void Camp(Transform parent, string name, Vector3 pos, float radius, params (AlienType type, Vector3 offset)[] aliens)
        {
            var g = new GameObject($"Camp_{name}");
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            var c = g.AddComponent<AlienCamp>();
            c.campName = name;
            c.guardRadius = radius;
            var list = new List<AlienCamp.Entry>();
            foreach (var a in aliens) list.Add(new AlienCamp.Entry { type = a.type, offset = a.offset });
            c.aliens = list.ToArray();
        }

        /// <summary>Lootable supply crate. With a weapon it glows in the weapon's rarity colour.</summary>
        static void Chest(Transform parent, Vector3 worldPos, float yaw, CrateLoot loot, bool weapon = false, WeaponKind kind = WeaponKind.IonCarbine)
        {
            var go = new GameObject(weapon ? $"Crate_{kind}" : $"Crate_{loot}");
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(worldPos, Quaternion.Euler(0f, yaw, 0f));
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, 0.4f, 0);
            bc.size = new Vector3(1.4f, 0.8f, 0.9f);
            Visual("Feet", go.transform, new Vector3(0, 0.03f, 0), new Vector3(1.5f, 0.06f, 1.0f), mDarkSteel);
            Visual("Base", go.transform, new Vector3(0, 0.33f, 0), new Vector3(1.4f, 0.6f, 0.9f), mChestBody);
            var strips = new List<MeshRenderer>
            {
                Visual("StripFront", go.transform, new Vector3(0, 0.38f, 0.455f), new Vector3(1.1f, 0.06f, 0.02f), mChestGlow, PrimitiveType.Cube, default, false).GetComponent<MeshRenderer>(),
                Visual("StripL", go.transform, new Vector3(-0.705f, 0.38f, 0), new Vector3(0.02f, 0.06f, 0.7f), mChestGlow, PrimitiveType.Cube, default, false).GetComponent<MeshRenderer>(),
                Visual("StripR", go.transform, new Vector3(0.705f, 0.38f, 0), new Vector3(0.02f, 0.06f, 0.7f), mChestGlow, PrimitiveType.Cube, default, false).GetComponent<MeshRenderer>(),
            };
            var lid = new GameObject("LidPivot").transform;
            lid.SetParent(go.transform, false);
            lid.localPosition = new Vector3(0, 0.63f, -0.45f);
            Visual("Lid", lid, new Vector3(0, 0.09f, 0.45f), new Vector3(1.44f, 0.18f, 0.94f), mChestBody);
            strips.Add(Visual("LidStrip", lid, new Vector3(0, 0.185f, 0.45f), new Vector3(1.0f, 0.02f, 0.12f), mChestGlow, PrimitiveType.Cube, default, false).GetComponent<MeshRenderer>());
            Visual("Latch", lid, new Vector3(0, 0.03f, 0.92f), new Vector3(0.22f, 0.14f, 0.05f), mDarkSteel);

            var chest = go.AddComponent<LootChest>();
            chest.containsWeapon = weapon;
            chest.weapon = kind;
            chest.loot = loot;
            chest.lid = lid;
            chest.glowStrips = strips.ToArray();
            chest.glowLight = PointLight("CrateLight", go.transform, new Vector3(0, 1.3f, 0), chest.Tint, 1.5f, 4.5f);
        }

        enum Door { South, North, West, East }

        /// <summary>Enterable prefab-hab: walls with a doorway, glowing windows, a roof that hides when you're inside.</summary>
        static Transform House(Transform parent, string name, Vector3 pos, float yaw, float w, float d, Door door, Color lightColor)
        {
            const float h = 2.7f, th = 0.3f, doorW = 2.4f, doorH = 2.25f;
            var root = Group(name, parent, pos, yaw);
            Visual("Floor", root, new Vector3(0, 0.03f, 0), new Vector3(w, 0.06f, d), mFloor, PrimitiveType.Cube, default, false);
            bool kit = Resources.Load<GameObject>("Models/HabKit");
            var roofParts = new List<Renderer>();

            void Wall(Door side)
            {
                bool horiz = side == Door.South || side == Door.North;
                float len = horiz ? w + th : d;
                Vector3 c = side == Door.South ? new Vector3(0, h / 2f, -d / 2f) : side == Door.North ? new Vector3(0, h / 2f, d / 2f)
                          : side == Door.West ? new Vector3(-w / 2f, h / 2f, 0) : new Vector3(w / 2f, h / 2f, 0);
                Vector3 along = horiz ? Vector3.right : Vector3.forward;
                Vector3 Size(float l, float hh) => horiz ? new Vector3(l, hh, th) : new Vector3(th, hh, l);
                float pieceYaw = side == Door.South ? 180f : side == Door.North ? 0f : side == Door.West ? -90f : 90f;
                // kit pieces face outward along +Z; "along" for a yawed piece runs along its local -X (South/East) or +X
                Vector3 baseC = new Vector3(c.x, 0f, c.z);

                // collision stays simple boxes (hidden when the kit is present)
                if (side != door)
                {
                    var wb = Box($"Wall{side}", root, c, Size(len, h), mWall);
                    if (kit) HideRenderer(wb);
                    else Visual($"Window{side}", root, c + new Vector3(0, 0.35f, 0), Size(Mathf.Min(2.2f, len * 0.4f), 0.55f), mWindow, PrimitiveType.Cube, default, false);
                }
                else
                {
                    float seg = (len - doorW) / 2f;
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        var wb = Box($"Wall{side}{sgn}", root, c + along * (sgn * (doorW / 2f + seg / 2f)), Size(seg, h), mWall);
                        if (kit) HideRenderer(wb);
                    }
                    var lb = Box($"Lintel{side}", root, new Vector3(c.x, doorH + (h - doorH) / 2f, c.z), Size(doorW, h - doorH), mWall);
                    if (kit) HideRenderer(lb);
                }
                if (!kit) return;

                void Run(float from, float to, bool windows)
                {
                    float l = to - from;
                    if (l < 0.4f) return;
                    int n = Mathf.Max(1, Mathf.RoundToInt(l / 2f));
                    float mw = l / n;
                    for (int i = 0; i < n; i++)
                    {
                        float m = from + mw * (i + 0.5f);
                        Kit(windows && i % 2 == 1 ? "WallWindow" : "WallPanel", root, baseC + along * m, pieceYaw, new Vector3(mw / 2f, 1f, 1f));
                        if (i > 0) Kit("Rib", root, baseC + along * (from + mw * i), pieceYaw);
                    }
                }
                float half = (horiz ? w : d) / 2f;
                if (side == door)
                {
                    Kit("DoorFrame", root, baseC, pieceYaw);
                    Kit("DoorSteps", root, baseC, pieceYaw, default, false);
                    Run(-half, -1.5f, true);
                    Run(1.5f, half, true);
                }
                else Run(-half, half, true);
            }
            Wall(Door.South); Wall(Door.North); Wall(Door.West); Wall(Door.East);

            if (kit)
            {
                foreach (var cx in new[] { -1, 1 })
                    foreach (var cz in new[] { -1, 1 })
                        Kit("Pillar", root, new Vector3(cx * w / 2f, 0, cz * d / 2f), 0f);
                int nx = Mathf.Max(1, Mathf.RoundToInt(w / 2f)), nz = Mathf.Max(1, Mathf.RoundToInt(d / 2f));
                float tx = w / nx, tz = d / nz;
                for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                    {
                        var tile = Kit("RoofTile", root, new Vector3(-w / 2f + tx * (ix + 0.5f), h, -d / 2f + tz * (iz + 0.5f)), 0f, new Vector3((tx + 0.3f) / 2f, 1f, (tz + 0.3f) / 2f));
                        if (tile) roofParts.AddRange(tile.GetComponentsInChildren<Renderer>());
                    }
                float top = h + 0.2f;
                void RoofProp(string p, Vector3 lp, float yawP)
                {
                    var go = Kit(p, root, lp + new Vector3(0, top, 0), yawP);
                    if (go) roofParts.AddRange(go.GetComponentsInChildren<Renderer>());
                }
                RoofProp("RoofVent", new Vector3(w * 0.15f, 0, d * 0.1f), 0f);
                RoofProp("RoofPipes", new Vector3(-w * 0.1f, 0, -d * 0.15f), 90f);
                RoofProp(R() < 0.5f ? "RoofDish" : "RoofVent", new Vector3(-w * 0.32f, 0, d * 0.25f), R(0f, 360f));
                RoofProp("RoofBeacon", new Vector3(w * 0.42f, 0, d * 0.38f), 0f);
            }
            else
            {
                roofParts.Add(Visual("Roof", root, new Vector3(0, h + 0.15f, 0), new Vector3(w + 0.6f, 0.3f, d + 0.6f), mRoof).GetComponent<Renderer>());
            }

            var light = PointLight("InteriorLight", root, new Vector3(0, h - 0.4f, 0), lightColor, 6f, Mathf.Max(w, d) * 0.9f);
            var b = root.gameObject.AddComponent<Building>();
            b.footprint = new Vector2(w, d);
            b.roof = roofParts.ToArray();
            b.interiorLight = light;
            return root;
        }

        static void Bunk(Transform room, Vector3 localPos, float yaw)
        {
            var g = new GameObject("Bunk").transform;
            g.SetParent(room, false);
            g.localPosition = localPos;
            g.localRotation = Quaternion.Euler(0, yaw, 0);
            Box("Frame", g, new Vector3(0, 0.25f, 0), new Vector3(0.9f, 0.5f, 2f), mDarkSteel);
            Visual("Mattress", g, new Vector3(0, 0.55f, 0), new Vector3(0.85f, 0.12f, 1.9f), mBed);
            Visual("Pillow", g, new Vector3(0, 0.65f, 0.75f), new Vector3(0.6f, 0.1f, 0.3f), mWall);
        }

        static void Console(Transform room, Vector3 localPos, float yaw)
        {
            var g = new GameObject("Console").transform;
            g.SetParent(room, false);
            g.localPosition = localPos;
            g.localRotation = Quaternion.Euler(0, yaw, 0);
            Box("Desk", g, new Vector3(0, 0.45f, 0), new Vector3(1.4f, 0.9f, 0.6f), mDarkSteel);
            Visual("Screen", g, new Vector3(0, 1.2f, 0.15f), new Vector3(1.1f, 0.6f, 0.05f), mScreen, PrimitiveType.Cube, new Vector3(-12, 0, 0), false);
        }

        // ------------------------------------------------------------------ areas

        static void BuildBorders(Transform t)
        {
            var g = Group("Cliffs", t, Vector3.zero);
            void Edge(Vector3 from, Vector3 to, float yaw)
            {
                Vector3 d = to - from;
                int n = Mathf.CeilToInt(d.magnitude / 12f);
                for (int i = 0; i <= n; i++)
                {
                    Vector3 p = from + d * (i / (float)n) + new Vector3(R(-1.5f, 1.5f), 0, R(-1.5f, 1.5f));
                    Prop("RockCliff", g, p, yaw + R(-12f, 12f), R(0.85f, 1.3f), true, 0.6f);
                    if (R() < 0.45f) Prop("RockSpire", g, p + Quaternion.Euler(0, yaw, 0) * new Vector3(R(-5f, 5f), 0, -R(4f, 7f)), R(0, 360), R(0.8f, 1.5f), false, 0.3f);
                }
            }
            float ox = HalfX + 3.5f, oz = HalfZ + 3.5f;
            Edge(new Vector3(-ox - 4f, 0, oz), new Vector3(ox + 4f, 0, oz), 180f);
            Edge(new Vector3(-ox - 4f, 0, -oz), new Vector3(ox + 4f, 0, -oz), 0f);
            Edge(new Vector3(ox, 0, -oz), new Vector3(ox, 0, oz), -90f);
            Edge(new Vector3(-ox, 0, -oz), new Vector3(-ox, 0, oz), 90f);
            // boulders at the foot of the cliffs
            for (int i = 0; i < 26; i++)
            {
                float a = R(0f, Mathf.PI * 2f);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Vector3 p = new Vector3(Mathf.Clamp(dir.x * 999f, -(HalfX - 1.5f), HalfX - 1.5f), 0, Mathf.Clamp(dir.z * 999f, -(HalfZ - 1.5f), HalfZ - 1.5f));
                Prop(R() < 0.6f ? "RockBoulder" : "RockSlab", g, p, R(0, 360), R(0.5f, 1.1f), true, 0.1f);
            }
            // Invisible bounds so nothing slips between boulders.
            for (int i = 0; i < 4; i++)
            {
                bool xSide = i < 2;
                float s = i % 2 == 0 ? 1f : -1f;
                var w = new GameObject($"Bounds{i}");
                w.transform.SetParent(g, false);
                w.transform.position = xSide ? new Vector3(s * (HalfX + 1f), 3f, 0) : new Vector3(0, 3f, s * (HalfZ + 1f));
                w.AddComponent<BoxCollider>().size = xSide ? new Vector3(1f, 6f, HalfZ * 2f + 10f) : new Vector3(HalfX * 2f + 10f, 6f, 1f);
                w.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            }
        }

        static void BuildLandingZone(Transform t, Vector3 c)
        {
            var g = Group("LandingZone", t, c);
            Zone(t, "LANDING ZONE", "Your drop pod. It's quiet here... for now.", c, 9f, new Color(1f, 0.75f, 0.2f));
            Visual("PadBase", g, new Vector3(0, 0.03f, 0), new Vector3(10f, 0.03f, 10f), mDarkSteel, PrimitiveType.Cylinder, default, false);
            Visual("PadRing", g, new Vector3(0, 0.045f, 0), new Vector3(9.2f, 0.03f, 9.2f), mYellow, PrimitiveType.Cylinder, default, false);
            Visual("PadDeck", g, new Vector3(0, 0.06f, 0), new Vector3(8.6f, 0.03f, 8.6f), mPad, PrimitiveType.Cylinder, default, false);
            for (int i = 0; i < 4; i++)
                Visual($"Chevron{i}", g, Quaternion.Euler(0, i * 90f + 45f, 0) * new Vector3(0, 0.09f, 2.6f), new Vector3(1.4f, 0.02f, 0.35f), mYellow, PrimitiveType.Cube, new Vector3(0, i * 90f + 45f, 0), false);

            // Drop pod (hatch facing the pad)
            Prop("DropPod", g, c + new Vector3(-3.6f, 0, -3.6f), 45f, 1f, true, 0.05f);
            PointLight("PodLight", g, new Vector3(-2.2f, 1.5f, -2.2f), new Color(1f, 0.55f, 0.2f), 7f, 8f);
            Chest(g, c + new Vector3(3.5f, 0, -3f), -30f, CrateLoot.Basic);
            Prop("Containers", g, c + new Vector3(5.6f, 0, 0.2f), 100f, 0.85f);
            Prop("Canisters", g, c + new Vector3(5.2f, 0, 2.6f), 15f, 0.9f);
            Lamp(g, c + new Vector3(4.5f, 0, 4.5f), new Color(1f, 0.62f, 0.28f), mYellow);
            Lamp(g, c + new Vector3(-5f, 0, 4f), new Color(1f, 0.62f, 0.28f), mYellow);
        }

        static void BuildWalkway(Transform t, Vector3 a, Vector3 b)
        {
            var g = Group("Walkway", t, Vector3.zero);
            Vector3 d = b - a;
            float len = d.magnitude;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            int n = Mathf.FloorToInt(len / 2.6f);
            for (int i = 0; i <= n; i++)
            {
                Vector3 p = a + d * (i / (float)n);
                if (!Kit("Grate", g, p, yaw, default, false))
                    Visual($"Plate{i}", g, p + new Vector3(0, 0.04f, 0), new Vector3(2.2f, 0.06f, 2.3f), mPad, PrimitiveType.Cube, new Vector3(0, yaw, 0), false);
                if (i % 2 == 0)
                {
                    // guide lights: a trail of small cyan pools leading from the pod to the outpost
                    for (int s = -1; s <= 1; s += 2)
                        Visual("Marker", g, p + Quaternion.Euler(0, yaw, 0) * new Vector3(s * 1.25f, 0.12f, 0), new Vector3(0.14f, 0.14f, 0.5f), mCyan, PrimitiveType.Cube, new Vector3(0, yaw, 0), false);
                    PointLight("GuideLight", g, OnGround(p) + new Vector3(0, 0.7f, 0), new Color(0.2f, 0.75f, 1f), 2.4f, 5f);
                }
            }
            keepClear.Add(((a + b) * 0.5f, 3f));
            keepClear.Add((a, 3f)); keepClear.Add((b, 3f));
        }

        static void BuildOutpost(Transform t, Vector3 c)
        {
            var g = Group("Outpost", t, c);
            Zone(t, "KEPLER-7 OUTPOST", "Abandoned mining colony. Something got here first.", c, 15f, new Color(0.35f, 0.9f, 1f));

            // Barracks: door south, bunks + crate with a Nova Scatter
            var a = House(g, "Barracks", c + new Vector3(-6f, 0, 3f), 0f, 8f, 6f, Door.South, new Color(1f, 0.85f, 0.6f));
            Bunk(a, new Vector3(-3.0f, 0, 1.4f), 90f);
            Bunk(a, new Vector3(-0.6f, 0, 1.4f), 90f);
            Box("Locker", a, new Vector3(3.4f, 1f, 2.4f), new Vector3(0.9f, 2f, 0.6f), mSteel);
            Box("Table", a, new Vector3(1.2f, 0.4f, -0.8f), new Vector3(1.2f, 0.8f, 0.8f), mDarkSteel);
            Chest(g, a.TransformPoint(new Vector3(2.8f, 0, 0.2f)), -90f, CrateLoot.Basic, true, WeaponKind.NovaScatter);

            // Lab: door west, consoles + specimen tank + xenite crate
            var b = House(g, "Lab", c + new Vector3(6f, 0, 7f), 0f, 7f, 6f, Door.West, new Color(0.6f, 1f, 0.75f));
            Console(b, new Vector3(1.5f, 0, 2.4f), 0f);
            Console(b, new Vector3(-1.2f, 0, 2.4f), 0f);
            var tank = Box("Tank", b, new Vector3(2.6f, 1.1f, -1.6f), new Vector3(1.1f, 2.2f, 1.1f), mTank);
            Visual("TankCap", b, new Vector3(2.6f, 2.25f, -1.6f), new Vector3(1.3f, 0.15f, 1.3f), mDarkSteel, PrimitiveType.Cylinder);
            PointLight("TankGlow", b, new Vector3(2.6f, 1.2f, -0.8f), new Color(0.3f, 1f, 0.4f), 1.6f, 4f);
            Chest(g, b.TransformPoint(new Vector3(0.2f, 0, -1.8f)), 0f, CrateLoot.Xenite);

            // Storage shed: door north, barrels + gold crate
            var s = House(g, "Storage", c + new Vector3(2f, 0, -7.5f), 0f, 6f, 5f, Door.North, new Color(1f, 0.7f, 0.4f));
            Barrel(s, new Vector3(-2f, 0, -1.4f), mTrim);
            Barrel(s, new Vector3(-1.1f, 0, -1.7f), mSteel);
            Box("Shelf", s, new Vector3(2.4f, 0.9f, 0f), new Vector3(0.6f, 1.8f, 3f), mDarkSteel);
            Chest(g, s.TransformPoint(new Vector3(0.3f, 0, -1.3f)), 0f, CrateLoot.Gold);

            // Yard props
            var mast = Group("CommMast", g, c + new Vector3(-1f, 0, 11.5f));
            Box("Pole", mast, new Vector3(0, 4f, 0), new Vector3(0.35f, 8f, 0.35f), mSteel);
            Visual("Dish", mast, new Vector3(0.6f, 7f, 0), new Vector3(2.2f, 0.12f, 2.2f), mWall, PrimitiveType.Cylinder, new Vector3(0, 0, 60));
            Visual("Beacon", mast, new Vector3(0, 8.1f, 0), Vector3.one * 0.3f, mRed, PrimitiveType.Sphere, default, false);
            PointLight("BeaconLight", mast, new Vector3(0, 8f, 0), new Color(1f, 0.15f, 0.1f), 2f, 6f);

            var gen = Group("Generator", g, c + new Vector3(6.5f, 0, -1.5f), 20f);
            Box("Housing", gen, new Vector3(0, 0.8f, 0), new Vector3(2.4f, 1.6f, 1.4f), mSteel);
            Visual("Core", gen, new Vector3(0, 0.9f, 0.72f), new Vector3(0.9f, 0.9f, 0.05f), mCyan, PrimitiveType.Cube, default, false);
            PointLight("GenGlow", gen, new Vector3(0, 1f, 1.4f), new Color(0.2f, 0.7f, 1f), 5f, 6f);
            Prop("Canisters", g, c + new Vector3(-1.2f, 0, -2.9f), 20f, 0.9f);
            Prop("Containers", g, c + new Vector3(-11.2f, 0, -4f), 75f, 0.8f);
            Prop("Containers", g, c + new Vector3(11.8f, 0, -6.5f), -10f, 0.75f);
            // Low fence (cover) along the south of the yard
            // (gap at i == 1 where the walkway from the landing zone comes in)
            for (int i = 0; i < 4; i++)
                if (i != 1) Barricade($"Fence{i}", g, new Vector3(-12.5f + i * 3.4f, 0f, -10.5f), i % 2 == 0 ? 4f : -4f);
            Lamp(g, c + new Vector3(-1.5f, 0, -3f), new Color(0.45f, 0.78f, 1f), mCyan);
            Lamp(g, c + new Vector3(10f, 0, 2.5f), new Color(0.45f, 0.78f, 1f), mCyan);

            Camp(t, "OUTPOST", c + new Vector3(0, 0, 1f), 8f,
                (AlienType.Skitter, new Vector3(-1.5f, 0, -1.5f)), (AlienType.Skitter, new Vector3(1.5f, 0, -0.5f)),
                (AlienType.Skitter, new Vector3(0f, 0, 2f)), (AlienType.Spitter, new Vector3(3f, 0, 3f)));
        }

        static void BuildCrashSite(Transform t, Vector3 c)
        {
            var g = Group("CrashSite", t, c);
            Zone(t, "CRASH SITE", "A freighter went down hard. Its cargo might still be intact.", c, 13f, new Color(1f, 0.5f, 0.2f));
            Vector3 cc = new Vector3(CrashCrater.x, 0, CrashCrater.y);
            Prop("Wreck", g, cc + new Vector3(0, 0, 0.5f), 200f, 1f, true, 0.15f, new Vector3(4f, 0, -3f));
            // spilled cargo and debris
            Prop("Containers", g, c + new Vector3(-6f, 0, -1.5f), 35f, 0.8f, true, 0.1f, new Vector3(0, 0, 12f));
            Prop("Canisters", g, c + new Vector3(6.5f, 0, -2f), -30f, 0.85f, true, 0.1f, new Vector3(8f, 0, 0));
            Prop("RockBoulder", g, c + new Vector3(-8f, 0, 3f), 40f, 0.7f);
            Prop("RockSlab", g, c + new Vector3(8.5f, 0, 4f), 140f, 0.8f);
            foreach (var fp in new[] { new Vector3(-4f, 0f, 9f), new Vector3(4.5f, 0f, 6.5f), new Vector3(-1f, 0f, 4.5f) })
            {
                var f = Group("Fire", g, OnGround(cc + fp - new Vector3(0, 0, 8f)) + Vector3.up * 0.15f);
                Visual("Embers", f, Vector3.zero, new Vector3(0.9f, 0.08f, 0.9f), mFireGlow, PrimitiveType.Cylinder, default, false);
                PointLight("FireLight", f, new Vector3(0, 0.8f, 0), new Color(1f, 0.5f, 0.15f), 9f, 10f);
                f.gameObject.AddComponent<Smolder>();
            }
            PointLight("WreckGlow", g, (cc - c) + new Vector3(0, 3f, 0), new Color(1f, 0.55f, 0.25f), 12f, 15f);
            Chest(g, c + new Vector3(4.5f, 0, -4.5f), 200f, CrateLoot.Gold);

            Camp(t, "CRASH SITE", c + new Vector3(-1f, 0, -3f), 8f,
                (AlienType.Spitter, new Vector3(-3f, 0, -2f)), (AlienType.Spitter, new Vector3(4f, 0, 1f)),
                (AlienType.Skitter, new Vector3(0f, 0, 0f)), (AlienType.Skitter, new Vector3(2f, 0, -2f)));
        }

        static void BuildNest(Transform t, Vector3 c)
        {
            var g = Group("XenoNest", t, c);
            Zone(t, "XENO NEST", "The hive. Whatever's in here is worth the risk.", c, 13f, new Color(0.7f, 0.35f, 1f));

            // Hive spires and egg clusters (organic ground is painted into the terrain)
            var spires = new[] { new Vector3(-6f, 0, 3f), new Vector3(-3f, 0, 7.5f), new Vector3(5.5f, 0, 6.5f), new Vector3(7.5f, 0, -2f), new Vector3(-7.5f, 0, -4f), new Vector3(1f, 0, -7.5f) };
            foreach (var sp in spires)
                Prop("HiveSpire", g, c + sp, R(0, 360), R(0.75f, 1.25f), true, 0.1f);
            foreach (var ep in new[] { new Vector3(-4f, 0, -1f), new Vector3(4f, 0, 3f), new Vector3(-1.5f, 0, 5f), new Vector3(5f, 0, -5f), new Vector3(-5f, 0, 6f) })
                Prop("EggCluster", g, c + ep, R(0, 360), R(0.8f, 1.2f), true, 0.05f);
            // sickly green against violet, with a deep magenta at the back: the one place on the map that is not warm/cool
            PointLight("NestGlowA", g, new Vector3(-3f, 3.2f, 2f), new Color(0.4f, 1f, 0.3f), 34f, 16f);
            PointLight("NestGlowB", g, new Vector3(4f, 3.2f, -2f), new Color(0.72f, 0.28f, 1f), 38f, 16f);
            PointLight("NestGlowC", g, new Vector3(3.5f, 3f, 6.5f), new Color(1f, 0.2f, 0.55f), 24f, 14f);

            // A lost supply pod in the middle of the hive, holding the best weapon on the map.
            var podPos = c + new Vector3(1f, 0, 1.5f);
            Prop("DropPod", g, podPos + new Vector3(-2f, 0, 1f), 30f, 0.6f, true, 0.4f, new Vector3(25f, 0, 15f));
            Chest(g, podPos, 220f, CrateLoot.Xenite, true, WeaponKind.HelixBeam);

            Camp(t, "XENO NEST", c + new Vector3(0, 0, -0.5f), 8f,
                (AlienType.Brute, new Vector3(0f, 0, -2f)),
                (AlienType.Skitter, new Vector3(-3f, 0, 0f)), (AlienType.Skitter, new Vector3(3f, 0, 1f)), (AlienType.Skitter, new Vector3(-1f, 0, 3f)),
                (AlienType.Spitter, new Vector3(4f, 0, -4f)));
        }

        static void BuildMiningRidge(Transform t, Vector3 c)
        {
            var g = Group("MiningRidge", t, c);
            Zone(t, "MINING RIDGE", "Ore veins and an old drill rig.", c, 12f, new Color(1f, 0.8f, 0.3f));
            foreach (var p in new[] { new Vector3(-8f, 0, 5f), new Vector3(-9f, 0, -3f), new Vector3(-2f, 0, 8.5f), new Vector3(6f, 0, 8f) })
                Rock(g, c + p, R(4f, 6f), R(3f, 5f));

            // Ore boulders with glowing veins
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = c + new Vector3(R(-6f, 6f), 0, R(-5f, 5f));
                float s = R(1.4f, 2.2f);
                var ore = Group("OreBoulder", g, p, R(0, 360));
                Prop("RockBoulder", ore, p, R(0, 360), s / 3.2f, true, 0.05f);
                bool xen = i % 2 == 0;
                for (int k = 0; k < 4; k++)
                    Visual(xen ? "Crystal" : "Vein", ore, new Vector3(R(-0.5f, 0.5f) * s, R(0.4f, 0.9f) * s, R(-0.5f, 0.5f) * s),
                        xen ? new Vector3(0.18f, R(0.5f, 0.9f), 0.18f) : new Vector3(0.5f, 0.08f, 0.2f), xen ? mOreXenite : mOreGold,
                        PrimitiveType.Cube, new Vector3(R(-30, 30), R(0, 90), R(-30, 30)), false);
                if (xen) PointLight("Glow", ore, new Vector3(0, s, 0), new Color(0.35f, 1f, 0.55f), 1.2f, 4f);
            }

            // Drill rig
            Prop("DrillRig", g, c + new Vector3(2f, 0, 3.5f), 195f, 1f, true, 0.05f);
            PointLight("RigLight", g, new Vector3(2f, 4.5f, 3f), new Color(1f, 0.55f, 0.2f), 16f, 12f);
            Lamp(g, c + new Vector3(4f, 0, -4f), new Color(1f, 0.62f, 0.28f), mYellow);

            Chest(g, c + new Vector3(-1.5f, 0, -3.5f), 30f, CrateLoot.Xenite);

            Camp(t, "MINING RIDGE", c + new Vector3(0, 0, 0f), 6f,
                (AlienType.Skitter, new Vector3(-1f, 0, 0f)), (AlienType.Skitter, new Vector3(1.5f, 0, -1f)));
        }

        /// <summary>Extraction point: pad, sky beam, beacon lamps. Hold position on it to extract (ExtractionZone).</summary>
        static void BuildEvac(Transform t, Vector3 c)
        {
            var g = Group("EvacPoint", t, c);
            Zone(t, "EVAC POINT", "Hold the pad until the dropship locks on.", c, 6f, new Color(0.35f, 0.9f, 1f));
            Visual("PadBase", g, new Vector3(0, 0.03f, 0), new Vector3(8f, 0.03f, 8f), mDarkSteel, PrimitiveType.Cylinder, default, false);
            Visual("PadRing", g, new Vector3(0, 0.045f, 0), new Vector3(7.2f, 0.03f, 7.2f), mCyan, PrimitiveType.Cylinder, default, false);
            Visual("PadDeck", g, new Vector3(0, 0.06f, 0), new Vector3(6.6f, 0.03f, 6.6f), mFloor, PrimitiveType.Cylinder, default, false);
            var fx = Shader.Find("ZCOM/FxAdditive");
            var beamMat = new Material(fx ? fx : Shader.Find("Universal Render Pipeline/Unlit")) { name = "EvacBeam" };
            beamMat.SetTexture("_MainTex", ProcTex.Glow);
            beamMat.SetColor("_Color", new Color(0.35f, 0.9f, 1f) * 0.35f);
            var beam = Visual("Beam", g, new Vector3(0, 32f, 0), new Vector3(1.1f, 30f, 1.1f), beamMat, PrimitiveType.Cylinder, default, false);
            var core = Visual("BeamCore", g, new Vector3(0, 32f, 0), new Vector3(0.35f, 30f, 0.35f), beamMat, PrimitiveType.Cylinder, default, false);
            foreach (var dpos in new[] { new Vector3(4.6f, 0, 0), new Vector3(-4.6f, 0, 0), new Vector3(0, 0, 4.6f), new Vector3(0, 0, -4.6f) })
            {
                var lg = Group("Beacon", g, c + dpos, Mathf.Atan2(-dpos.x, -dpos.z) * Mathf.Rad2Deg);
                if (!Kit("Lamp", lg, Vector3.zero, 0f)) Box("Post", lg, new Vector3(0, 1.8f, 0), new Vector3(0.3f, 3.6f, 0.3f), mDarkSteel);
                SpotLight("Light", lg, new Vector3(0, 3.3f, 0.9f), new Color(0.3f, 0.85f, 1f), 26f, 11f);
            }
            var bl = PointLight("BeaconLight", g, new Vector3(0, 4f, 0), new Color(0.35f, 0.9f, 1f), 4f, 16f);
            var ez = g.gameObject.AddComponent<ExtractionZone>();
            ez.beam = beam.GetComponent<Renderer>();
            ez.beaconLight = bl;
            foreach (var col in g.GetComponentsInChildren<Collider>())
                if (col.transform.name.StartsWith("Beam") || col.transform.name == "Ring" || col.transform.name.StartsWith("Pad")) Object.DestroyImmediate(col);
        }

        static void BuildScatter(Transform t)
        {
            var g = Group("Scatter", t, Vector3.zero);
            int placed = 0, tries = 0;
            while (placed < 30 && tries++ < 600)
            {
                Vector3 p = new Vector3(R(-HalfX + 4f, HalfX - 4f), 0, R(-HalfZ + 4f, HalfZ - 4f));
                bool clear = true;
                foreach (var (pos, radius) in keepClear)
                    if ((p - pos).magnitude < radius + 2f) { clear = false; break; }
                if (!clear) continue;
                placed++;
                float k = R();
                if (k < 0.45f) Prop("RockBoulder", g, p, R(0, 360), R(0.35f, 0.95f), true, 0.08f);
                else if (k < 0.8f) Prop("RockSlab", g, p, R(0, 360), R(0.5f, 1.2f), true, 0.1f);
                else Prop("RockSpire", g, p, R(0, 360), R(0.35f, 0.7f), true, 0.1f);
            }
        }
    }
}
