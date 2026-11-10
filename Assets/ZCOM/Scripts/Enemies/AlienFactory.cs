using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>Builds alien GameObjects from primitives. Tweak stats/looks per type here.</summary>
    public static class AlienFactory
    {
        public static Alien Create(AlienType type, Vector3 position, Quaternion rotation, bool dormant = false)
        {
            var go = new GameObject(type.ToString());
            go.transform.SetPositionAndRotation(position, rotation);
            var a = go.AddComponent<Alien>();
            a.type = type;
            var body = new GameObject("Body").transform;
            body.SetParent(go.transform, false);
            a.body = body;

            // Every alien's hit capsule spans the bullet plane (y ~1.2) regardless of its visual height.
            var col = go.AddComponent<CapsuleCollider>();

            switch (type)
            {
                case AlienType.Skitter: BuildSkitter(a, body, col); break;
                case AlienType.Spitter: BuildSpitter(a, body, col); break;
                case AlienType.Brute: BuildBrute(a, body, col); break;
            }
            a.Init(dormant);
            return a;
        }

        /// <summary>Uses the Blender model if it exists; wires legs / arms / sac / mouth for the procedural animation.</summary>
        static bool TryModel(Alien a, Transform body, string model, Dictionary<string, Material> mats)
        {
            var go = CharacterModels.Spawn(model, body, mats);
            if (!go) return false;
            body.localPosition = Vector3.zero;
            a.legs = CharacterModels.FindAll(go.transform, "Leg_");
            var arms = CharacterModels.FindAll(go.transform, "Arm_");
            if (arms.Length > 0) a.arms = arms;
            var sac = CharacterModels.Find(go.transform, "Sac");
            if (sac) a.sac = sac;
            var mouth = CharacterModels.Find(go.transform, "Mouth");
            if (mouth) a.mouth = mouth;
            return true;
        }

        /// <summary>Rigged insect model (Tools/Blender/zcom_aliens.py) with procedural IK legs.</summary>
        static bool TryXeno(Alien a, Transform body, AlienType type, Color glow, float glowIntensity)
        {
            var tex = Resources.Load<Texture2D>("Textures/Terrain/hive");
            var hiveN = Resources.Load<Texture2D>("Textures/Terrain/hive_n");
            var rockN = Resources.Load<Texture2D>("Textures/Terrain/rock_n");
            var chitin = Mats.Cached("XenoChitin", () =>
            {
                var m = Mats.Lit(new Color(0.2f, 0.11f, 0.24f), 0.35f, 0.8f);
                if (rockN) { m.SetTexture("_BumpMap", rockN); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 0.7f); }
                return m;
            });
            var flesh = Mats.Cached("XenoFlesh", () =>
            {
                var m = Mats.Lit(new Color(0.75f, 0.42f, 0.62f), 0f, 0.62f);
                if (tex) m.SetTexture("_BaseMap", tex);
                if (hiveN) { m.SetTexture("_BumpMap", hiveN); m.EnableKeyword("_NORMALMAP"); }
                return m;
            });
            var claw = Mats.Cached("XenoClaw", () => Mats.Lit(new Color(0.52f, 0.43f, 0.35f), 0.05f, 0.5f));
            var glowM = Glow("XenoGlow" + type, glow, glowIntensity);
            var go = CharacterModels.Spawn("Alien_" + type, body, new Dictionary<string, Material>
            {
                { "AlienChitin", chitin }, { "AlienFlesh", flesh }, { "AlienGlow", glowM }, { "AlienClaw", claw },
            });
            if (!go) return false;
            body.localPosition = Vector3.zero;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
            // The rig is driven by code only (IK legs, ragdoll); the importer's empty Animator has nothing to play.
            foreach (var an in go.GetComponentsInChildren<Animator>())
            {
                if (Application.isPlaying) Object.Destroy(an); else Object.DestroyImmediate(an);
            }
            a.legs = new Transform[0];
            var sac = CharacterModels.Find(go.transform, "Sac");
            if (sac) a.sac = sac;
            var mouth = CharacterModels.Find(go.transform, "Mouth");
            if (mouth) a.mouth = mouth;
            var clawL = CharacterModels.Find(go.transform, "Claw_L_A");
            var clawR = CharacterModels.Find(go.transform, "Claw_R_A");
            if (clawL && clawR) a.arms = new[] { clawL, clawR };
            a.skinMaterial = flesh;
            a.shellMaterial = chitin;
            a.glowMaterial = glowM;
            var il = InsectLegs.Attach(go, a.transform);
            switch (type)
            {
                case AlienType.Skitter: il.stepThreshold = 0.3f; il.stepDuration = 0.09f; il.stepHeight = 0.12f; break;
                case AlienType.Spitter: il.stepThreshold = 0.5f; il.stepDuration = 0.16f; il.stepHeight = 0.28f; break;
                default: il.stepThreshold = 0.75f; il.stepDuration = 0.26f; il.stepHeight = 0.32f; il.footstepVolume = 0.25f; break;
            }
            return true;
        }

        static Material Lit(string key, Color c, float metal, float smooth, Color emission = default) =>
            Mats.Cached(key, () => Mats.Lit(c, metal, smooth, emission));

        static Material Glow(string key, Color c, float intensity) =>
            Mats.Cached(key, () => Mats.Lit(c * 0.3f, 0f, 0.8f, Mats.HDR(c, intensity)));

        static void BuildSkitter(Alien a, Transform body, CapsuleCollider col)
        {
            a.maxHealth = 42f; a.moveSpeed = 6.2f; a.agentRadius = 0.45f;
            a.attackRange = 2.6f; a.attackDamage = 9f; a.telegraphTime = 0.32f; a.attackCooldown = 0.9f; a.recoverTime = 0.45f;
            a.knockbackResist = 1f; a.lungeSpeed = 15f; a.lungeTime = 0.2f;
            a.scoreValue = 10; a.healthDropChance = 0.08f;
            a.legSwing = 28f; a.stepFrequency = 16f; a.bobAmount = 0.05f; a.deathSize = 0.9f;
            a.gooColor = new Color(0.45f, 1f, 0.3f); a.glowColor = new Color(0.4f, 1f, 0.35f);

            col.center = new Vector3(0, 0.9f, 0); col.height = 1.8f; col.radius = 0.55f;

            var skin = Lit("SkitterSkin", new Color(0.24f, 0.08f, 0.32f), 0.1f, 0.75f);
            var shell = Lit("SkitterShell", new Color(0.09f, 0.04f, 0.13f), 0.35f, 0.9f);
            var eye = Glow("SkitterEye", new Color(0.4f, 1f, 0.35f), 6f);
            var sacMat = Glow("SkitterSac", new Color(1f, 0.25f, 0.85f), 2.5f);
            a.skinMaterial = skin; a.shellMaterial = shell; a.glowMaterial = sacMat;
            if (TryXeno(a, body, AlienType.Skitter, new Color(0.45f, 1f, 0.2f), 3.5f)) return;
            if (TryModel(a, body, "Skitter", new Dictionary<string, Material> { { "Skin", skin }, { "Shell", shell }, { "Glow", eye }, { "Sac", sacMat } })) return;

            body.localPosition = new Vector3(0, 0.55f, 0);
            Prims.Part("Thorax", PrimitiveType.Sphere, body, Vector3.zero, new Vector3(0.8f, 0.5f, 1.0f), skin);
            Prims.Part("Carapace", PrimitiveType.Sphere, body, new Vector3(0, 0.14f, -0.12f), new Vector3(0.72f, 0.34f, 0.7f), shell);
            Prims.Part("Head", PrimitiveType.Sphere, body, new Vector3(0, 0.05f, 0.55f), new Vector3(0.5f, 0.38f, 0.5f), shell);
            Prims.Part("EyeL", PrimitiveType.Sphere, body, new Vector3(-0.13f, 0.12f, 0.75f), Vector3.one * 0.11f, eye, default, false);
            Prims.Part("EyeR", PrimitiveType.Sphere, body, new Vector3(0.13f, 0.12f, 0.75f), Vector3.one * 0.11f, eye, default, false);
            Prims.Part("EyeC", PrimitiveType.Sphere, body, new Vector3(0, 0.18f, 0.72f), Vector3.one * 0.07f, eye, default, false);
            Prims.Part("MandibleL", PrimitiveType.Cube, body, new Vector3(-0.12f, -0.08f, 0.8f), new Vector3(0.05f, 0.05f, 0.25f), shell, new Vector3(0, -25, 0));
            Prims.Part("MandibleR", PrimitiveType.Cube, body, new Vector3(0.12f, -0.08f, 0.8f), new Vector3(0.05f, 0.05f, 0.25f), shell, new Vector3(0, 25, 0));
            Prims.Part("Sac", PrimitiveType.Sphere, body, new Vector3(0, 0.22f, -0.5f), Vector3.one * 0.36f, sacMat);

            var legs = new List<Transform>();
            for (int side = -1; side <= 1; side += 2)
                for (int row = 0; row < 3; row++)
                {
                    float z = 0.3f - row * 0.3f;
                    var pivot = new GameObject($"Leg{side}{row}").transform;
                    pivot.SetParent(body, false);
                    pivot.localPosition = new Vector3(0.3f * side, 0f, z);
                    pivot.localRotation = Quaternion.Euler(0, (row - 1) * -25f * side, 0);
                    Prims.Limb("Upper", pivot, Vector3.zero, new Vector3(0.45f * side, 0.25f, 0f), 0.08f, shell);
                    Prims.Limb("Lower", pivot, new Vector3(0.45f * side, 0.25f, 0f), new Vector3(0.65f * side, -0.55f, 0f), 0.06f, shell);
                    legs.Add(pivot);
                }
            a.legs = legs.ToArray();
        }

        static void BuildSpitter(Alien a, Transform body, CapsuleCollider col)
        {
            a.maxHealth = 60f; a.moveSpeed = 3.8f; a.agentRadius = 0.5f;
            a.attackRange = 13f; a.attackDamage = 11f; a.telegraphTime = 0.55f; a.attackCooldown = 2.1f; a.recoverTime = 0.6f;
            a.knockbackResist = 0.8f; a.preferredRange = 9.5f; a.projectileSpeed = 12f;
            a.scoreValue = 20; a.healthDropChance = 0.15f;
            a.legSwing = 25f; a.stepFrequency = 9f; a.bobAmount = 0.08f; a.deathSize = 1f;
            a.gooColor = new Color(0.75f, 1f, 0.2f); a.glowColor = new Color(0.6f, 1f, 0.2f);

            col.center = new Vector3(0, 1f, 0); col.height = 2f; col.radius = 0.5f;

            var skin = Lit("SpitterSkin", new Color(0.13f, 0.27f, 0.24f), 0.1f, 0.7f);
            var shell = Lit("SpitterShell", new Color(0.05f, 0.11f, 0.11f), 0.3f, 0.85f);
            var eye = Glow("SpitterEye", new Color(1f, 0.9f, 0.2f), 6f);
            var sacMat = Glow("SpitterSac", new Color(0.55f, 1f, 0.15f), 3f);
            a.skinMaterial = skin; a.shellMaterial = shell; a.glowMaterial = sacMat;
            if (TryXeno(a, body, AlienType.Spitter, new Color(0.55f, 1f, 0.15f), 3.2f)) return;
            if (TryModel(a, body, "Spitter", new Dictionary<string, Material> { { "Skin", skin }, { "Shell", shell }, { "Glow", eye }, { "Sac", sacMat } })) return;

            body.localPosition = Vector3.zero;
            var legs = new List<Transform>();
            for (int i = 0; i < 3; i++)
            {
                float ang = i * 120f + 60f;
                var pivot = new GameObject($"Leg{i}").transform;
                pivot.SetParent(body, false);
                pivot.localPosition = new Vector3(0, 0.8f, 0);
                pivot.localRotation = Quaternion.Euler(0, ang, 0);
                Prims.Limb("Upper", pivot, Vector3.zero, new Vector3(0f, -0.1f, 0.45f), 0.1f, shell);
                Prims.Limb("Lower", pivot, new Vector3(0f, -0.1f, 0.45f), new Vector3(0f, -0.8f, 0.6f), 0.07f, shell);
                legs.Add(pivot);
            }
            a.legs = legs.ToArray();

            Prims.Part("Torso", PrimitiveType.Capsule, body, new Vector3(0, 1.15f, 0), new Vector3(0.5f, 0.45f, 0.45f), skin);
            Prims.Part("Ribs", PrimitiveType.Sphere, body, new Vector3(0, 1.2f, 0.1f), new Vector3(0.45f, 0.6f, 0.35f), shell);
            Prims.Part("Head", PrimitiveType.Sphere, body, new Vector3(0, 1.7f, 0.12f), new Vector3(0.42f, 0.36f, 0.55f), shell);
            Prims.Part("EyeL", PrimitiveType.Sphere, body, new Vector3(-0.1f, 1.76f, 0.36f), Vector3.one * 0.09f, eye, default, false);
            Prims.Part("EyeR", PrimitiveType.Sphere, body, new Vector3(0.1f, 1.76f, 0.36f), Vector3.one * 0.09f, eye, default, false);
            Prims.Part("EyeT", PrimitiveType.Sphere, body, new Vector3(0, 1.86f, 0.32f), Vector3.one * 0.07f, eye, default, false);
            var sac = Prims.Part("Sac", PrimitiveType.Sphere, body, new Vector3(0, 1.45f, -0.32f), new Vector3(0.55f, 0.6f, 0.55f), sacMat);
            a.sac = sac.transform;
            var mouth = new GameObject("Mouth").transform;
            mouth.SetParent(body, false);
            mouth.localPosition = new Vector3(0, 1.4f, 0.55f);
            a.mouth = mouth;
        }

        static void BuildBrute(Alien a, Transform body, CapsuleCollider col)
        {
            a.maxHealth = 320f; a.moveSpeed = 2.8f; a.agentRadius = 0.85f;
            a.attackRange = 3.0f; a.attackDamage = 26f; a.telegraphTime = 0.75f; a.attackCooldown = 1.6f; a.recoverTime = 0.9f;
            a.knockbackResist = 0.2f; a.slamRadius = 3.4f;
            a.scoreValue = 60; a.healthDropChance = 0.8f;
            a.legSwing = 22f; a.stepFrequency = 6f; a.bobAmount = 0.12f; a.deathSize = 1.8f;
            a.gooColor = new Color(1f, 0.5f, 0.15f); a.glowColor = new Color(1f, 0.45f, 0.1f);

            col.center = new Vector3(0, 1.2f, 0); col.height = 2.6f; col.radius = 0.95f;

            var skin = Lit("BruteSkin", new Color(0.34f, 0.1f, 0.07f), 0.1f, 0.6f);
            var armor = Lit("BruteArmor", new Color(0.18f, 0.18f, 0.2f), 0.7f, 0.65f);
            var eye = Glow("BruteEye", new Color(1f, 0.35f, 0.1f), 7f);
            var core = Glow("BruteCore", new Color(1f, 0.45f, 0.1f), 4f);
            a.skinMaterial = skin; a.shellMaterial = armor; a.glowMaterial = core;
            var bone = Lit("BruteBone", new Color(0.85f, 0.8f, 0.7f), 0f, 0.5f);
            if (TryXeno(a, body, AlienType.Brute, new Color(1f, 0.45f, 0.1f), 3.2f)) return;
            if (TryModel(a, body, "Brute", new Dictionary<string, Material> { { "Skin", skin }, { "Shell", armor }, { "Glow", eye }, { "Sac", core }, { "Bone", bone } })) return;

            body.localPosition = Vector3.zero;
            var legs = new List<Transform>();
            for (int side = -1; side <= 1; side += 2)
            {
                var pivot = new GameObject($"Leg{side}").transform;
                pivot.SetParent(body, false);
                pivot.localPosition = new Vector3(0.45f * side, 1.0f, 0);
                Prims.Limb("Thigh", pivot, Vector3.zero, new Vector3(0.1f * side, -0.55f, 0.15f), 0.42f, skin);
                Prims.Limb("Shin", pivot, new Vector3(0.1f * side, -0.55f, 0.15f), new Vector3(0.1f * side, -1.0f, -0.05f), 0.36f, armor);
                legs.Add(pivot);
            }
            a.legs = legs.ToArray();

            Prims.Part("Torso", PrimitiveType.Sphere, body, new Vector3(0, 1.55f, 0), new Vector3(1.55f, 1.3f, 1.15f), skin);
            Prims.Part("Back", PrimitiveType.Sphere, body, new Vector3(0, 1.85f, -0.25f), new Vector3(1.3f, 0.9f, 0.9f), armor);
            Prims.Part("ShoulderL", PrimitiveType.Cube, body, new Vector3(-0.78f, 2.0f, 0), new Vector3(0.6f, 0.4f, 0.75f), armor, new Vector3(0, 0, 18));
            Prims.Part("ShoulderR", PrimitiveType.Cube, body, new Vector3(0.78f, 2.0f, 0), new Vector3(0.6f, 0.4f, 0.75f), armor, new Vector3(0, 0, -18));
            Prims.Part("Head", PrimitiveType.Sphere, body, new Vector3(0, 1.95f, 0.55f), new Vector3(0.6f, 0.48f, 0.6f), armor);
            for (int i = 0; i < 4; i++)
            {
                float x = (i - 1.5f) * 0.13f;
                Prims.Part($"Eye{i}", PrimitiveType.Sphere, body, new Vector3(x, 2.0f + (i % 2) * 0.06f, 0.83f), Vector3.one * 0.09f, eye, default, false);
            }
            Prims.Part("Core", PrimitiveType.Sphere, body, new Vector3(0, 1.45f, 0.5f), Vector3.one * 0.42f, core, default, false);

            var arms = new List<Transform>();
            for (int side = -1; side <= 1; side += 2)
            {
                var pivot = new GameObject($"Arm{side}").transform;
                pivot.SetParent(body, false);
                pivot.localPosition = new Vector3(0.95f * side, 1.85f, 0.1f);
                Prims.Limb("Upper", pivot, Vector3.zero, new Vector3(0.15f * side, -0.6f, 0.25f), 0.38f, skin);
                Prims.Limb("Fore", pivot, new Vector3(0.15f * side, -0.6f, 0.25f), new Vector3(0.1f * side, -1.25f, 0.45f), 0.42f, armor);
                Prims.Part("Fist", PrimitiveType.Sphere, pivot, new Vector3(0.1f * side, -1.35f, 0.5f), Vector3.one * 0.48f, armor);
                arms.Add(pivot);
            }
            a.arms = arms.ToArray();
        }
    }
}
