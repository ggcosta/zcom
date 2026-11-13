using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// The operative's dropship ZC-07 "KESTREL", in two instances:
    /// - Exterior (KEPLER-7, landing zone): the hull with the ramp down and the rear blast door shut. Nobody walks in
    ///   here; the raid starts at the foot of the ramp, and holding position at the door extracts (ExtractionZone),
    ///   which loads the interior instance.
    /// - Interior (Ship scene, the hub): only the inside, larger than the hull (it is its own instance and the outside is
    ///   never shown): flight deck, crew bay (stash), mid bay (marked bays for future workstations) and cargo hold, as a
    ///   permanent cutaway in the dark. The rear door (<see cref="ShipDoor"/>) leads back out to KEPLER-7 (a new raid).
    /// Model: Tools/Blender/zcom_dropship.py -> Resources/Models/Dropship.fbx (one baked PBR material per object in
    /// Resources/Materials/Ship). Inside, the upper hull and the upper interior stay cut away (they keep casting shadows,
    /// so the interior stays an interior). Ship-local space: origin on the ground under the hull's middle, nose +Z,
    /// ramp -Z, deck at y = 1.7.
    /// </summary>
    public class Dropship : MonoBehaviour
    {
        public static Dropship I { get; private set; }

        public const float Deck = 1.7f;
        /// <summary>Ship-local points the game and the tests use.</summary>
        public static readonly Vector3 SpawnLocal = new Vector3(0f, Deck, -11.8f), HoldLocal = new Vector3(0f, Deck, -9f),
            RampFootLocal = new Vector3(0f, 0f, -14.6f), DoorOutsideLocal = new Vector3(0f, 0f, -10.6f),
            DoorInsideLocal = new Vector3(0f, Deck, -13.2f), StashLocal = new Vector3(4.95f, Deck, 8.5f);

        public bool interior;
        public Renderer[] upper = new Renderer[0];

        public Vector3 SpawnWorld => transform.TransformPoint(interior ? SpawnLocal : RampFootLocal);
        public Vector3 HoldWorld => transform.TransformPoint(HoldLocal);
        public Vector3 RampFootWorld => transform.TransformPoint(RampFootLocal);
        public Vector3 DoorWorld => transform.TransformPoint(interior ? DoorInsideLocal : DoorOutsideLocal);

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
            // the interior instance is a permanent cutaway
            if (!interior) return;
            foreach (var r in upper)
                if (r) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        // ------------------------------------------------------------------ build (editor scene builder, via MapBuilder)

        static GameObject model;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static Material mGlass;

        static GameObject Part(string name, Transform parent, Vector3 pos, float yaw = 0f, Vector3? scale = null)
        {
            if (!model) model = Resources.Load<GameObject>("Models/Dropship");
            if (!model) return null;
            var src = model.transform.Find(name);
            if (!src) { Debug.LogWarning($"[Dropship] part {name} missing"); return null; }
            var go = Object.Instantiate(src.gameObject, parent, false);
            go.name = name;
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * src.localRotation;
            go.transform.localScale = scale ?? Vector3.one;
            var m = Mat(name);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) ms[i] = m;
                r.sharedMaterials = ms;
                r.shadowCastingMode = name.EndsWith("Glass") ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return go;
        }

        static Material Mat(string part)
        {
            if (part.EndsWith("Glass"))
            {
                if (!mGlass)
                {
                    mGlass = Mats.Lit(new Color(0.08f, 0.16f, 0.2f, 0.55f), 0.3f, 0.97f, Mats.HDR(new Color(0.2f, 0.55f, 0.75f), 0.12f));
                    mGlass.name = "ShipCanopy";
                    mGlass.SetFloat("_Surface", 1f);
                    mGlass.SetFloat("_Blend", 0f);
                    mGlass.SetOverrideTag("RenderType", "Transparent");
                    mGlass.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mGlass.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mGlass.SetInt("_ZWrite", 0);
                    mGlass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mGlass.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                return mGlass;
            }
            if (mats.TryGetValue(part, out var m) && m) return m;
            m = Resources.Load<Material>("Materials/Ship/" + part);
            if (!m) { m = Mats.Lit(new Color(0.22f, 0.23f, 0.25f), 0.4f, 0.45f); m.name = "Ship_" + part; }
            mats[part] = m;
            return m;
        }

        static void Block(Transform parent, string name, Vector3 center, Vector3 size, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.AddComponent<BoxCollider>().size = size;
        }

        static Light Lamp(Transform parent, string name, Vector3 pos, Color c, float intensity, float range, LightType type = LightType.Point, Vector3 euler = default, float angle = 90f, bool shadows = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            var l = go.AddComponent<Light>();
            l.type = type; l.color = c; l.intensity = intensity; l.range = range;
            if (type == LightType.Spot) { l.spotAngle = angle; l.innerSpotAngle = angle * 0.5f; }
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            if (shadows) { l.shadowStrength = 0.85f; l.shadowNearPlane = 0.2f; }
            return l;
        }

        /// <summary>Builds the ship under parent at pos (ground), nose pointing along yaw. interior: the hub instance
        /// (walkable interior, permanent cutaway, exit door); otherwise the closed exterior for the open world.</summary>
        public static Dropship Build(Transform parent, Vector3 pos, float yaw, bool interior)
        {
            mats.Clear();
            model = null;
            var root = new GameObject(interior ? "Dropship_KESTREL_Interior" : "Dropship_KESTREL");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var t = root.transform;
            var ship = root.AddComponent<Dropship>();
            ship.interior = interior;

            var upper = new List<Renderer>();
            var parts = interior
                ? new[] { "Interior_Lower", "Interior_Upper" }
                : new[] { "Hull_Lower", "Hull_Upper", "Ramp", "Cockpit", "CanopyGlass", "RearDoor" };
            foreach (var n in parts)
            {
                var go = Part(n, t, Vector3.zero);
                if (go && (n.EndsWith("_Upper") || n == "CanopyGlass")) upper.AddRange(go.GetComponentsInChildren<Renderer>());
            }
            if (!interior)
            {
                Part("Engine", t, new Vector3(-7.55f, 3.25f, -2.6f));
                Part("Engine", t, new Vector3(7.55f, 3.25f, -2.6f), 0f, new Vector3(-1f, 1f, 1f));
                foreach (var g in new[] { new Vector3(3f, 0f, 4.3f), new Vector3(-3f, 0f, 4.3f), new Vector3(3f, 0f, -5f), new Vector3(-3f, 0f, -5f) })
                    Part("LandingGear", t, g);
            }
            ship.upper = upper.ToArray();

            var col = new GameObject("Collision").transform;
            col.SetParent(t, false);
            var lights = new GameObject("Lights").transform;
            lights.SetParent(t, false);
            Color cool = new Color(0.8f, 0.88f, 1f);

            if (!interior) BuildExteriorBits(t, col, lights, cool);
            if (!interior)
            {
                // closed hull: nothing gets inside or under it
                Block(col, "Hull", new Vector3(0f, 3f, -1.2f), new Vector3(9.6f, 6f, 15f));
                Block(col, "Nose", new Vector3(0f, 3f, 9.2f), new Vector3(7.5f, 6f, 5.4f));
                // boarding = extraction: hold position at the door (opens once the operative has left the ramp area)
                var ez = new GameObject("Extraction").AddComponent<ExtractionZone>();
                ez.transform.SetParent(t, false);
                ez.transform.localPosition = new Vector3(0f, 0f, -10.4f);
                ez.radius = 2.4f;
                ez.holdTime = 6f;
                ez.armDistance = 12f;
                ez.beaconLight = Lamp(ez.transform, "BoardingBeacon", new Vector3(0f, 3.4f, 0f), new Color(0.35f, 0.9f, 1f), 0f, 6f);
                return ship;
            }
            BuildInteriorBits(t, col, lights, cool);
            return ship;
        }

        static void BuildExteriorBits(Transform t, Transform col, Transform lights, Color cool)
        {
            // ramp (walkable) and its rails, the shut rear door
            Vector3 top = new Vector3(0f, Deck - 0.02f, -8.6f), foot = new Vector3(0f, 0.03f, -13.5f);
            var rampRot = Quaternion.LookRotation(top - foot);
            Block(col, "Ramp", (top + foot) * 0.5f - rampRot * Vector3.up * 0.12f, new Vector3(6.2f, 0.24f, (top - foot).magnitude + 0.1f), rampRot);
            foreach (float s in new[] { -1f, 1f }) Block(col, "RampRail", new Vector3(s * 3.2f, 1f, -11f), new Vector3(0.2f, 2f, 5f));
            Block(col, "RearDoor", new Vector3(0f, 3f, -8.55f), new Vector3(7f, 6f, 0.3f));
            Lamp(lights, "RampFlood", new Vector3(0f, 4.9f, -9.2f), new Color(1f, 0.82f, 0.6f), 5f, 9f, LightType.Spot, new Vector3(62f, 180f, 0f), 75f, true);
            Lamp(lights, "DoorLight", new Vector3(0f, 3.6f, -9.4f), new Color(0.35f, 0.88f, 1f), 1.6f, 4f);
            foreach (float s in new[] { -1f, 1f })
            {
                Lamp(lights, "Belly", new Vector3(s * 3.2f, 0.7f, 3f), cool, 2.2f, 5f, LightType.Spot, new Vector3(90f, 0f, 0f), 110f);
                Lamp(lights, "Nav", new Vector3(s * 4.8f, 2.8f, 5.8f), s > 0 ? new Color(1f, 0.1f, 0.05f) : new Color(0.2f, 1f, 0.3f), 1.2f, 2.5f);
                Lamp(lights, "Engine", new Vector3(s * 7.55f, 3.25f, -7.2f), new Color(1f, 0.5f, 0.18f), 1.6f, 4f);
            }

        }

        /// <summary>The interior instance (Blender layout: half width 5.4, viewscreens at -17.4, rear door at +14;
        /// ship-local Unity = (-x, z, -y) of Blender).</summary>
        static void BuildInteriorBits(Transform t, Transform col, Transform lights, Color cool)
        {
            const float W = 5.4f;
            // cargo on the hold floor
            foreach (var (p, yaw) in new[] { (new Vector3(2.4f, 0f, -7.0f), 0f), (new Vector3(-2.3f, 0f, -11.4f), 90f), (new Vector3(2.6f, 0f, -11.6f), 180f) })
            {
                Part("CargoStack", t, p, yaw);
                Block(col, "Cargo", p + new Vector3(0f, Deck + 0.7f, 0f), yaw == 90f ? new Vector3(1.4f, 1.4f, 1.7f) : new Vector3(1.7f, 1.4f, 1.4f));
            }
            // the stash: locker on the port wall of the crew bay, facing inboard; the drawer slides out when it opens
            var sg = new GameObject("Stash").transform;
            sg.SetParent(t, false);
            sg.localPosition = StashLocal;
            sg.localRotation = Quaternion.Euler(0f, -90f, 0f);
            Part("StashLocker", sg, Vector3.zero);
            var drawer = Part("StashDrawer", sg, new Vector3(0.48f, 1.0f, 0.4f));
            var locker = sg.gameObject.AddComponent<StashLocker>();
            locker.drawer = drawer ? drawer.transform : null;
            locker.lockerLight = Lamp(sg, "StashLight", new Vector3(0f, 2.35f, 1.0f), new Color(1f, 0.66f, 0.32f), 1.8f, 5f);
            var sc = sg.gameObject.AddComponent<BoxCollider>();
            sc.center = new Vector3(0f, 1.35f, 0f); sc.size = new Vector3(3.1f, 2.7f, 0.95f);
            // the exit: the inside of the rear door
            var door = new GameObject("ExitDoor").AddComponent<ShipDoor>();
            door.transform.SetParent(t, false);
            door.transform.localPosition = DoorInsideLocal;

            // ---------- collision ----------
            Block(col, "Deck", new Vector3(0f, Deck - 0.4f, 1.7f), new Vector3(2f * W + 0.6f, 0.8f, 32f));
            Block(col, "FlightDeckFloor", new Vector3(0f, Deck, 15.2f), new Vector3(6.8f, 0.4f, 4.4f));
            foreach (float s in new[] { -1f, 1f })
            {
                Block(col, "Wall", new Vector3(s * (W + 0.35f), 3f, 1.7f), new Vector3(0.7f, 6f, 32.4f));
                Block(col, "WallFlight", new Vector3(s * 4.45f, 3f, 15.3f), new Vector3(2.1f, 6f, 4.6f));
                Block(col, "FlightCorner", new Vector3(s * 2.75f, 3f, 16.85f), new Vector3(0.5f, 6f, 2.2f), Quaternion.Euler(0f, s * 41f, 0f));
                Block(col, "PilotSeat", new Vector3(s * 1.0f, Deck + 0.6f, 15.5f), new Vector3(0.7f, 1.2f, 0.8f));
                Block(col, "Station", new Vector3(s * 2.95f, Deck + 0.6f, 14.2f), new Vector3(0.75f, 1.2f, 1.6f));
                Block(col, "FrontWall", new Vector3(s * (0.9f + (W - 0.9f) / 2f), 3f, 13f), new Vector3(W - 0.9f, 6f, 0.4f));
                Block(col, "MidWall", new Vector3(s * (1.8f + (W - 1.8f) / 2f), 3f, 4f), new Vector3(W - 1.8f, 6f, 0.4f));
                Block(col, "HoldWall", new Vector3(s * (2.1f + (W - 2.1f) / 2f), 3f, -4.5f), new Vector3(W - 2.1f, 6f, 0.4f));
                foreach (float z in new[] { -2f, 2f }) Block(col, "Rack", new Vector3(s * 5.05f, Deck + 1.3f, z), new Vector3(0.5f, 2.6f, 2.2f));
            }
            Block(col, "Viewscreens", new Vector3(0f, 3f, 17.6f), new Vector3(4f, 6f, 0.4f));
            Block(col, "Dashboard", new Vector3(0f, Deck + 0.6f, 16.6f), new Vector3(4.4f, 1.2f, 0.6f));
            Block(col, "CenterConsole", new Vector3(0f, Deck + 0.6f, 16f), new Vector3(0.5f, 1.2f, 1.0f));
            Block(col, "RearDoor", new Vector3(0f, 3f, -14.2f), new Vector3(2f * W + 0.6f, 6f, 0.6f));
            Block(col, "Lockers", new Vector3(5.1f, Deck + 1.1f, 11.9f), new Vector3(0.7f, 2.2f, 1.9f));
            Block(col, "Bunks", new Vector3(-4.85f, Deck + 1.1f, 10.45f), new Vector3(1.1f, 2.2f, 4.4f));
            Block(col, "CrewSeats", new Vector3(-5.1f, Deck + 0.6f, 5.9f), new Vector3(0.7f, 1.2f, 1.7f));
            Block(col, "Table", new Vector3(-1.2f, Deck + 0.4f, 8.4f), new Vector3(3.0f, 0.8f, 2.6f));
            Block(col, "HoldSeatsStbd", new Vector3(-5.1f, Deck + 0.6f, -9.15f), new Vector3(0.7f, 1.2f, 7.8f));
            Block(col, "HoldSeatsPort", new Vector3(5.1f, Deck + 0.6f, -11.05f), new Vector3(0.7f, 1.2f, 4.4f));

            // ---------- lights: ceiling panels, flight deck screens, door, stash ----------
            int n = 0;
            for (float z = 12f; z > -13.5f; z -= 4.3f)
                foreach (float x in new[] { -2.2f, 2.2f })
                    Lamp(lights, "Ceiling", new Vector3(x, Deck + 3.7f, z), cool, 8f, 9.5f, LightType.Spot, new Vector3(90f, 0f, 0f), 125f, (n++ % 5) == 0);
            Lamp(lights, "FlightDeck", new Vector3(0f, Deck + 2.4f, 15.4f), new Color(0.35f, 0.8f, 1f), 3f, 6f);
            Lamp(lights, "Viewscreens", new Vector3(0f, Deck + 2f, 16.8f), new Color(0.3f, 0.75f, 1f), 2f, 4f);
            Lamp(lights, "DoorRed", new Vector3(3.2f, Deck + 2.6f, -13.3f), new Color(1f, 0.25f, 0.12f), 1.6f, 4.5f);
            Lamp(lights, "DoorRed", new Vector3(-3.2f, Deck + 2.6f, -13.3f), new Color(1f, 0.25f, 0.12f), 1.6f, 4.5f);
            Lamp(lights, "MidBay", new Vector3(0f, Deck + 2.8f, 0f), new Color(1f, 0.72f, 0.4f), 2.2f, 7f);
        }

        /// <summary>Interior instance: the ship's named areas for the HUD (nothing outside is built: the hub only shows the ship).</summary>
        public static void BuildZones(Transform parent, Transform ship)
        {
            var g = new GameObject("Areas").transform;
            g.SetParent(parent, false);
            void Zone(string n, Vector3 p, float r, string sub)
            {
                var z = new GameObject("Zone_" + n).AddComponent<MapZone>();
                z.transform.SetParent(g, false);
                z.transform.position = ship.TransformPoint(p);
                z.zoneName = n; z.radius = r; z.subtitle = sub;
            }
            Zone("FLIGHT DECK", new Vector3(0f, 0f, 15.2f), 2.8f, "The KESTREL's bridge.");
            Zone("CREW BAY", new Vector3(0f, 0f, 8.5f), 4.6f, "The stash locker keeps your loot safe between raids.");
            Zone("MID BAY", new Vector3(0f, 0f, 0f), 4.6f, "Four bays, waiting for workstations.");
            Zone("CARGO HOLD", new Vector3(0f, 0f, -9.2f), 5.2f, "The rear door leads out to KEPLER-7.");
        }
    }
}
