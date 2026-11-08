using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>The attachment points a weapon model exposes to the Gun.</summary>
    public struct WeaponParts
    {
        public Transform muzzle;
        public Transform vent;          // heat vent: plasma vapour comes out here
        public Transform cell;          // energy cell: ejected on recharge
        public Transform[] spinners;    // floating emitter rings (spin faster while firing)
        public Renderer[] glows;        // energy cores (brightness shows charge / heat)
    }

    /// <summary>Builds the sci-fi energy weapons from primitives. Used for the held gun, pickups and icons.</summary>
    public static class WeaponModels
    {
        static Material Shell => Mats.Cached("WpnShell", () => Mats.Lit(new Color(0.86f, 0.88f, 0.92f), 0.25f, 0.8f));
        static Material Dark => Mats.Cached("WpnDark", () => Mats.Lit(new Color(0.08f, 0.09f, 0.12f), 0.7f, 0.65f));
        static Material Trim => Mats.Cached("WpnTrim", () => Mats.Lit(new Color(0.3f, 0.32f, 0.38f), 0.85f, 0.7f));

        static Material Glow(WeaponKind k, float intensity = 3f)
        {
            Color c = WeaponDefs.Get(k).color;
            return Mats.Cached($"WpnGlow{k}", () => Mats.Lit(c * 0.25f, 0f, 0.9f, Mats.HDR(c, intensity)));
        }

        static readonly Dictionary<string, Material> wpnMats = new Dictionary<string, Material>();

        static Material WpnMat(string name)
        {
            if (wpnMats.TryGetValue(name, out var m) && m) return m;
            switch (name)
            {
                case "WpnSteel": m = Mats.Lit(new Color(0.42f, 0.44f, 0.47f), 0.75f, 0.55f); break;
                case "WpnGraphite": m = Mats.Lit(new Color(0.11f, 0.115f, 0.13f), 0.5f, 0.5f); break;
                case "WpnRubber": m = Mats.Lit(new Color(0.05f, 0.05f, 0.055f), 0f, 0.25f); break;
                case "WpnAmber": m = Mats.Lit(new Color(0.91f, 0.6f, 0.2f), 0.2f, 0.45f, Mats.HDR(new Color(1f, 0.6f, 0.15f), 0.3f)); break;
                case "WpnCeramic": m = Mats.Lit(new Color(0.78f, 0.78f, 0.76f), 0.05f, 0.55f); break;
                case "WpnPad": m = Mats.Lit(new Color(0.16f, 0.13f, 0.1f), 0f, 0.15f); break;
                default: return null;
            }
            m.name = name;
            wpnMats[name] = m;
            return m;
        }

        /// <summary>Blender-made weapon (Tools/Blender/zcom_weapons.py): Body, Glow, Spinner_*, Muzzle/Vent/Cell empties.</summary>
        static bool TryBuildModel(WeaponKind kind, Transform root, out WeaponParts parts)
        {
            parts = default;
            var prefab = Resources.Load<GameObject>("Models/Weapon_" + kind);
            if (!prefab) return false;
            var go = Object.Instantiate(prefab, root, false);
            go.name = "Weapon_" + kind;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            var glow = Glow(kind, 4f);
            var glows = new List<Renderer>();
            var spinners = new List<Transform>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool isGlow = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] ? mats[i].name.Replace(" (Instance)", "") : "";
                    if (n.StartsWith("WpnGlow")) { mats[i] = glow; isGlow = true; }
                    else mats[i] = WpnMat(n) ?? mats[i];
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = isGlow ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                if (isGlow) glows.Add(r);
                if (r.name.StartsWith("Spinner")) spinners.Add(r.transform);
            }
            parts = new WeaponParts
            {
                muzzle = CharacterModels.Find(go.transform, "Muzzle") ?? Point("Muzzle", root, new Vector3(0, 0.1f, 0.6f)),
                vent = CharacterModels.Find(go.transform, "Vent"),
                cell = CharacterModels.Find(go.transform, "Cell"),
                spinners = spinners.ToArray(),
                glows = glows.ToArray(),
            };
            return true;
        }

        public static WeaponParts Build(WeaponKind kind, Transform root)
        {
            if (TryBuildModel(kind, root, out var built)) return built;
            switch (kind)
            {
                case WeaponKind.NovaScatter: return BuildNova(root);
                case WeaponKind.HelixBeam: return BuildHelix(root);
                default: return BuildIon(root);
            }
        }

        static Transform Point(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        /// <summary>A floating ring of small segments around the barrel axis.</summary>
        static Transform SegmentRing(string name, Transform parent, Vector3 pos, float radius, int count, float seg, Material mat, List<Renderer> glows)
        {
            var pivot = Point(name, parent, pos);
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                var s = Prims.Part($"Seg{i}", PrimitiveType.Cube, pivot, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f),
                    new Vector3(seg, seg, seg * 0.6f), mat, new Vector3(0f, 0f, a * Mathf.Rad2Deg), false);
                glows?.Add(s.GetComponent<Renderer>());
            }
            return pivot;
        }

        static GameObject Capsule(string name, Transform parent, Vector3 pos, float radius, float length, Material m) =>
            Prims.Part(name, PrimitiveType.Capsule, parent, pos, new Vector3(radius * 2f, length * 0.5f, radius * 2f), m, new Vector3(90, 0, 0));

        // ------------------------------------------------------------------ ION CARBINE: sleek, cyan

        static WeaponParts BuildIon(Transform root)
        {
            var glow = Glow(WeaponKind.IonCarbine);
            var glows = new List<Renderer>();

            Capsule("Body", root, new Vector3(0, 0.01f, 0.2f), 0.07f, 0.62f, Shell);
            Prims.Part("Spine", PrimitiveType.Cube, root, new Vector3(0, 0.075f, 0.16f), new Vector3(0.035f, 0.05f, 0.42f), Dark);
            Prims.Part("Underside", PrimitiveType.Cube, root, new Vector3(0, -0.045f, 0.25f), new Vector3(0.09f, 0.04f, 0.36f), Dark);
            Prims.Part("Grip", PrimitiveType.Cube, root, new Vector3(0, -0.12f, 0.02f), new Vector3(0.055f, 0.14f, 0.07f), Dark, new Vector3(-18, 0, 0));
            Prims.Part("Stock", PrimitiveType.Cube, root, new Vector3(0, -0.005f, -0.17f), new Vector3(0.06f, 0.09f, 0.2f), Trim);
            Prims.Part("StockPad", PrimitiveType.Cube, root, new Vector3(0, -0.02f, -0.28f), new Vector3(0.07f, 0.13f, 0.04f), Dark);
            Prims.Part("Barrel", PrimitiveType.Cylinder, root, new Vector3(0, 0.01f, 0.58f), new Vector3(0.045f, 0.12f, 0.045f), Trim, new Vector3(90, 0, 0));
            var core = Prims.Part("Core", PrimitiveType.Cylinder, root, new Vector3(0, 0.01f, 0.32f), new Vector3(0.09f, 0.08f, 0.09f), glow, new Vector3(90, 0, 0), false);
            glows.Add(core.GetComponent<Renderer>());
            var emitter = Prims.Part("Emitter", PrimitiveType.Sphere, root, new Vector3(0, 0.01f, 0.71f), Vector3.one * 0.06f, glow, default, false);
            glows.Add(emitter.GetComponent<Renderer>());
            foreach (float x in new[] { -0.072f, 0.072f })
                glows.Add(Prims.Part("Strip", PrimitiveType.Cube, root, new Vector3(x, 0.01f, 0.12f), new Vector3(0.005f, 0.02f, 0.3f), glow, default, false).GetComponent<Renderer>());
            var ring = SegmentRing("Ring", root, new Vector3(0, 0.01f, 0.64f), 0.085f, 8, 0.025f, glow, glows);
            var cell = Prims.Part("Cell", PrimitiveType.Cylinder, root, new Vector3(0, -0.1f, 0.22f), new Vector3(0.06f, 0.06f, 0.06f), glow, default, false);

            return new WeaponParts
            {
                muzzle = Point("Muzzle", root, new Vector3(0, 0.01f, 0.78f)),
                vent = Point("Vent", root, new Vector3(0, 0.1f, 0.3f)),
                cell = cell.transform,
                spinners = new[] { ring },
                glows = glows.ToArray(),
            };
        }

        // ------------------------------------------------------------------ NOVA SCATTER: chunky, three-prong emitter, pink

        static WeaponParts BuildNova(Transform root)
        {
            var glow = Glow(WeaponKind.NovaScatter);
            var glows = new List<Renderer>();

            Prims.Part("Body", PrimitiveType.Cube, root, new Vector3(0, 0, 0.1f), new Vector3(0.15f, 0.15f, 0.4f), Dark);
            Capsule("Shell", root, new Vector3(0, 0.055f, 0.14f), 0.085f, 0.46f, Shell);
            Prims.Part("Grip", PrimitiveType.Cube, root, new Vector3(0, -0.13f, -0.02f), new Vector3(0.06f, 0.15f, 0.08f), Dark, new Vector3(-18, 0, 0));
            Prims.Part("Stock", PrimitiveType.Cube, root, new Vector3(0, -0.01f, -0.2f), new Vector3(0.08f, 0.12f, 0.22f), Trim);
            Prims.Part("Head", PrimitiveType.Cube, root, new Vector3(0, 0.01f, 0.34f), new Vector3(0.2f, 0.12f, 0.1f), Trim);
            for (int i = -1; i <= 1; i++)
                Prims.Part($"Prong{i}", PrimitiveType.Cube, root, new Vector3(i * 0.07f, 0.01f, 0.46f), new Vector3(0.03f, 0.05f, 0.2f), Shell, new Vector3(0, i * 14f, 0));
            var emitter = Prims.Part("Emitter", PrimitiveType.Sphere, root, new Vector3(0, 0.01f, 0.45f), Vector3.one * 0.09f, glow, default, false);
            glows.Add(emitter.GetComponent<Renderer>());
            foreach (float x in new[] { -0.105f, 0.105f })
                glows.Add(Prims.Part("Capacitor", PrimitiveType.Cylinder, root, new Vector3(x, -0.01f, 0.12f), new Vector3(0.055f, 0.13f, 0.055f), glow, new Vector3(90, 0, 0), false).GetComponent<Renderer>());
            glows.Add(Prims.Part("Stripe", PrimitiveType.Cube, root, new Vector3(0, 0.145f, 0.1f), new Vector3(0.05f, 0.01f, 0.3f), glow, default, false).GetComponent<Renderer>());
            var ring = SegmentRing("Ring", root, new Vector3(0, 0.01f, 0.4f), 0.13f, 6, 0.035f, glow, glows);
            var cell = Prims.Part("Cell", PrimitiveType.Cube, root, new Vector3(0, -0.105f, 0.13f), new Vector3(0.08f, 0.07f, 0.12f), glow, default, false);

            return new WeaponParts
            {
                muzzle = Point("Muzzle", root, new Vector3(0, 0.01f, 0.56f)),
                vent = Point("Vent", root, new Vector3(0, 0.13f, 0.0f)),
                cell = cell.transform,
                spinners = new[] { ring },
                glows = glows.ToArray(),
            };
        }

        // ------------------------------------------------------------------ HELIX BEAM: long, tuning-fork prongs, crystal, purple

        static WeaponParts BuildHelix(Transform root)
        {
            var glow = Glow(WeaponKind.HelixBeam);
            var glows = new List<Renderer>();

            Capsule("Body", root, new Vector3(0, 0, 0.14f), 0.075f, 0.6f, Shell);
            Prims.Part("Spine", PrimitiveType.Cube, root, new Vector3(0, 0.08f, 0.3f), new Vector3(0.04f, 0.04f, 0.62f), Dark);
            Prims.Part("Grip", PrimitiveType.Cube, root, new Vector3(0, -0.12f, 0.0f), new Vector3(0.055f, 0.14f, 0.07f), Dark, new Vector3(-18, 0, 0));
            Prims.Part("Stock", PrimitiveType.Cube, root, new Vector3(0, -0.01f, -0.22f), new Vector3(0.07f, 0.1f, 0.18f), Trim);
            foreach (float x in new[] { -0.055f, 0.055f })
                Prims.Part("Prong", PrimitiveType.Cube, root, new Vector3(x, 0, 0.68f), new Vector3(0.03f, 0.05f, 0.46f), Dark);
            var crystal = Prims.Part("Crystal", PrimitiveType.Cube, root, new Vector3(0, 0, 0.74f), new Vector3(0.05f, 0.05f, 0.18f), glow, new Vector3(0, 0, 45), false);
            glows.Add(crystal.GetComponent<Renderer>());
            var spinners = new List<Transform>();
            for (int i = 0; i < 3; i++)
                spinners.Add(SegmentRing($"Helix{i}", root, new Vector3(0, 0, 0.52f + i * 0.14f), 0.1f, 6, 0.022f, glow, glows));
            var cell = Prims.Part("Cell", PrimitiveType.Sphere, root, new Vector3(0, 0.0f, -0.08f), Vector3.one * 0.12f, glow, default, false);
            glows.Add(cell.GetComponent<Renderer>());

            return new WeaponParts
            {
                muzzle = Point("Muzzle", root, new Vector3(0, 0, 0.92f)),
                vent = Point("Vent", root, new Vector3(0, 0.11f, 0.1f)),
                cell = cell.transform,
                spinners = spinners.ToArray(),
                glows = glows.ToArray(),
            };
        }
    }
}
