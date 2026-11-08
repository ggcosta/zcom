using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>Builds the operative (armoured soldier + pulse rifle) from primitives and wires all components.</summary>
    public static class PlayerFactory
    {
        public static PlayerController Build(Vector3 position)
        {
            var armor = Mats.Lit(new Color(0.78f, 0.8f, 0.84f), 0.35f, 0.55f); armor.name = "PlayerArmor";
            var suit = Mats.Lit(new Color(0.12f, 0.13f, 0.16f), 0.5f, 0.45f); suit.name = "PlayerSuit";
            var accent = Mats.Lit(new Color(0.9f, 0.45f, 0.1f), 0.2f, 0.5f); accent.name = "PlayerAccent";
            var visor = Mats.Lit(Color.black, 0.2f, 0.95f, Mats.HDR(new Color(0.1f, 0.75f, 1f), 2.5f)); visor.name = "PlayerVisor";
            var neon = Mats.Lit(Color.black, 0f, 0.8f, Mats.HDR(new Color(0.1f, 0.75f, 1f), 2f)); neon.name = "PlayerNeon";

            var root = new GameObject("Player");
            root.transform.position = position;
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.04f;
            cc.slopeLimit = 45f;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            // Blender model (Tools/Blender/zcom_characters.py) if present, otherwise the primitive fallback below.
            var metal = Mats.Lit(new Color(0.22f, 0.23f, 0.26f), 0.8f, 0.6f); metal.name = "PlayerMetal";
            // Orbital Marshal (rigged, animated, baked PBR texture set) first, then the older static operative, then primitives.
            var marshal = SpawnMarshalModel(visual, out var gunSocket);
            Animator anim = null;
            Transform hipsBone = null, spineBone = null, chestBone = null;
            if (marshal)
            {
                anim = marshal.GetComponent<Animator>();
                hipsBone = CharacterModels.Find(marshal.transform, "Hips");
                spineBone = CharacterModels.Find(marshal.transform, "Spine");
                chestBone = CharacterModels.Find(marshal.transform, "Chest");
            }
            var bodyModel = marshal ? null : CharacterModels.Spawn("Operative", visual, new Dictionary<string, Material>
            {
                { "Armor", armor }, { "Suit", suit }, { "Accent", accent }, { "Visor", visor }, { "Neon", neon }, { "Metal", metal },
            });
            Transform legL = null, legR = null, torso = null;
            if (marshal)
            {
                torso = chestBone;
            }
            else if (bodyModel)
            {
                legL = CharacterModels.Find(bodyModel.transform, "Leg_L");
                legR = CharacterModels.Find(bodyModel.transform, "Leg_R");
                torso = CharacterModels.Find(bodyModel.transform, "Torso");
            }
            else
            {
                // Legs (pivot at hip so they swing)
                Transform Leg(string name, float x)
                {
                    var pivot = new GameObject(name).transform;
                    pivot.SetParent(visual, false);
                    pivot.localPosition = new Vector3(x, 0.88f, 0);
                    Prims.Part("Thigh", PrimitiveType.Capsule, pivot, new Vector3(0, -0.22f, 0), new Vector3(0.22f, 0.24f, 0.22f), suit);
                    Prims.Part("KneePad", PrimitiveType.Cube, pivot, new Vector3(0, -0.42f, 0.09f), new Vector3(0.2f, 0.16f, 0.1f), armor);
                    Prims.Part("Shin", PrimitiveType.Capsule, pivot, new Vector3(0, -0.6f, 0), new Vector3(0.2f, 0.22f, 0.2f), armor);
                    Prims.Part("Boot", PrimitiveType.Cube, pivot, new Vector3(0, -0.82f, 0.05f), new Vector3(0.22f, 0.12f, 0.34f), suit);
                    return pivot;
                }
                var legLp = Leg("LegL", -0.15f);
                var legRp = Leg("LegR", 0.15f);

                var torsoP = new GameObject("Torso").transform;
                torsoP.SetParent(visual, false);
                torsoP.localPosition = new Vector3(0, 0.9f, 0);
                Prims.Part("Pelvis", PrimitiveType.Cube, torsoP, new Vector3(0, 0.04f, 0), new Vector3(0.46f, 0.22f, 0.3f), suit);
                Prims.Part("Belt", PrimitiveType.Cube, torsoP, new Vector3(0, 0.12f, 0), new Vector3(0.5f, 0.07f, 0.34f), accent);
                Prims.Part("Abdomen", PrimitiveType.Cube, torsoP, new Vector3(0, 0.25f, 0), new Vector3(0.4f, 0.2f, 0.26f), suit);
                Prims.Part("Chest", PrimitiveType.Cube, torsoP, new Vector3(0, 0.48f, 0.02f), new Vector3(0.6f, 0.38f, 0.38f), armor);
                Prims.Part("ChestStripe", PrimitiveType.Cube, torsoP, new Vector3(0, 0.5f, 0.215f), new Vector3(0.32f, 0.04f, 0.02f), neon, default, false);
                Prims.Part("Backpack", PrimitiveType.Cube, torsoP, new Vector3(0, 0.46f, -0.28f), new Vector3(0.48f, 0.5f, 0.22f), suit);
                Prims.Part("PackLightL", PrimitiveType.Cube, torsoP, new Vector3(-0.14f, 0.55f, -0.395f), new Vector3(0.06f, 0.18f, 0.02f), neon, default, false);
                Prims.Part("PackLightR", PrimitiveType.Cube, torsoP, new Vector3(0.14f, 0.55f, -0.395f), new Vector3(0.06f, 0.18f, 0.02f), neon, default, false);
                Prims.Part("Antenna", PrimitiveType.Cylinder, torsoP, new Vector3(0.18f, 0.88f, -0.32f), new Vector3(0.02f, 0.2f, 0.02f), suit);
                Prims.Part("ShoulderL", PrimitiveType.Sphere, torsoP, new Vector3(-0.36f, 0.6f, 0), new Vector3(0.3f, 0.24f, 0.32f), armor);
                Prims.Part("ShoulderR", PrimitiveType.Sphere, torsoP, new Vector3(0.36f, 0.6f, 0), new Vector3(0.3f, 0.24f, 0.32f), armor);
                Prims.Part("ShoulderMarkL", PrimitiveType.Cube, torsoP, new Vector3(-0.45f, 0.62f, 0), new Vector3(0.04f, 0.08f, 0.16f), accent);

                var head = new GameObject("Head").transform;
                head.SetParent(visual, false);
                head.localPosition = new Vector3(0, 1.6f, 0.02f);
                Prims.Part("Neck", PrimitiveType.Cylinder, head, new Vector3(0, -0.12f, 0), new Vector3(0.16f, 0.06f, 0.16f), suit);
                Prims.Part("Helmet", PrimitiveType.Sphere, head, new Vector3(0, 0.05f, 0), new Vector3(0.36f, 0.38f, 0.4f), armor);
                Prims.Part("Visor", PrimitiveType.Cube, head, new Vector3(0, 0.06f, 0.17f), new Vector3(0.28f, 0.1f, 0.08f), visor, default, false);
                Prims.Part("Crest", PrimitiveType.Cube, head, new Vector3(0, 0.22f, -0.02f), new Vector3(0.05f, 0.06f, 0.3f), accent);

                // Arms reach from shoulders to the gun (static, the gun kicks within the grip).
                Prims.Limb("ArmR", visual, new Vector3(0.36f, 1.48f, 0f), new Vector3(0.22f, 1.14f, 0.28f), 0.13f, suit);
                Prims.Limb("ArmL", visual, new Vector3(-0.36f, 1.48f, 0f), new Vector3(0.18f, 1.2f, 0.66f), 0.13f, suit);
                Prims.Part("GloveR", PrimitiveType.Sphere, visual, new Vector3(0.21f, 1.12f, 0.3f), Vector3.one * 0.11f, armor);
                Prims.Part("GloveL", PrimitiveType.Sphere, visual, new Vector3(0.18f, 1.2f, 0.68f), Vector3.one * 0.11f, armor);
                legL = legLp; legR = legRp; torso = torsoP;
            }

            // Gun socket sits in front of the right shoulder; the Gun component lives here.
            var socket = gunSocket;
            if (!socket)
            {
                socket = new GameObject("GunSocket").transform;
                socket.SetParent(visual, false);
                socket.localPosition = new Vector3(0.2f, 1.22f, 0.28f);
            }
            var model = new GameObject("GunModel").transform;
            model.SetParent(socket, false);

            var hand = new GameObject("ThrowHand").transform;
            hand.SetParent(visual, false);
            hand.localPosition = new Vector3(-0.3f, 1.6f, 0.45f);

            if (!gunSocket && chestBone) socket.SetParent(chestBone, true);
            var gun = socket.gameObject.AddComponent<Gun>();
            gun.model = model;

            var health = root.AddComponent<PlayerHealth>();
            var inventory = root.AddComponent<Inventory>();
            root.AddComponent<PlayerInteractor>();
            root.AddComponent<ScanPulse>();
            root.AddComponent<GroundRing>();
            var grenades = root.AddComponent<GrenadeThrower>();
            grenades.hand = hand;

            var pc = root.AddComponent<PlayerController>();
            pc.visual = visual;
            pc.torso = torso;
            pc.legL = legL;
            pc.legR = legR;
            pc.gun = gun;
            pc.grenades = grenades;
            pc.health = health;
            pc.inventory = inventory;
            pc.animator = anim;
            pc.hips = hipsBone;
            pc.spine = spineBone;

            // Small fill light so the operative pops against the deck.
            var lightGo = new GameObject("OperativeLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0, 3f, -0.5f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.6f, 0.85f, 1f);
            l.intensity = 1.2f;
            l.range = 6f;
            l.shadows = LightShadows.None;

            return pc;
        }

        static Material armorMat, glowMat, screenMat, packSteel, packGraphite, packAmber;

        /// <summary>
        /// Spawns the rigged Orbital Marshal (Resources/Models/Marshal) under parent with its material, Animator and
        /// cape springs. gunSocket is aligned to the rig's Weapon bone (right-hand grip, facing forward).
        /// Used for the player and for the UI preview stage, so the HUD/inventory show exactly the in-game model.
        /// </summary>
        public static GameObject SpawnMarshalModel(Transform parent, out Transform gunSocket)
        {
            gunSocket = null;
            if (!armorMat)
            {
                armorMat = Resources.Load<Material>("Materials/MarshalArmor");
                if (!armorMat) { armorMat = Mats.Lit(new Color(0.3f, 0.31f, 0.33f), 0.6f, 0.5f); armorMat.name = "MarshalArmorFallback"; }
                glowMat = Mats.Lit(Color.black, 0f, 0.9f, Mats.HDR(new Color(0.25f, 0.88f, 0.94f), 4.2f)); glowMat.name = "MarshalGlow";
                screenMat = Mats.Lit(Color.black, 0f, 0.9f, Mats.HDR(new Color(0.2f, 0.75f, 0.85f), 2.2f)); screenMat.name = "MarshalScreen";
                packSteel = Mats.Lit(new Color(0.34f, 0.35f, 0.37f), 0.55f, 0.4f); packSteel.name = "PackSteel";
                packGraphite = Mats.Lit(new Color(0.1f, 0.105f, 0.115f), 0.35f, 0.35f); packGraphite.name = "PackGraphite";
                packAmber = Mats.Lit(new Color(0.85f, 0.55f, 0.18f), 0.15f, 0.4f); packAmber.name = "PackAmber";
            }
            var marshal = CharacterModels.Spawn("Marshal", parent, new Dictionary<string, Material>
            {
                { "MarshalArmor", armorMat }, { "Glow", glowMat }, { "Screen", screenMat },
                { "Steel", packSteel }, { "Graphite", packGraphite }, { "Amber", packAmber },
            });
            if (!marshal) return null;
            var anim = marshal.GetComponent<Animator>();
            if (!anim) anim = marshal.AddComponent<Animator>();
            anim.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Anim/Marshal");
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in marshal.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;

            var springs = marshal.AddComponent<SpringBones>();
            for (int c = 0; c < 3; c++)
            {
                var capeRoot = CharacterModels.Find(marshal.transform, $"Cape_{c}0");
                if (capeRoot) springs.AddChain(capeRoot);
            }
            foreach (var b in new[] { "Thigh_L", "Thigh_R" }) springs.AddCollider(CharacterModels.Find(marshal.transform, b), new Vector3(0f, 0.22f, 0f), 0.13f);
            foreach (var b in new[] { "Shin_L", "Shin_R" }) springs.AddCollider(CharacterModels.Find(marshal.transform, b), new Vector3(0f, 0.2f, 0f), 0.1f);
            springs.AddCollider(CharacterModels.Find(marshal.transform, "Hips"), new Vector3(0f, 0.02f, 0f), 0.2f);
            springs.AddCollider(CharacterModels.Find(marshal.transform, "Spine"), new Vector3(0f, 0.08f, 0f), 0.2f);

            var weaponBone = CharacterModels.Find(marshal.transform, "Weapon");
            if (weaponBone)
            {
                // bind pose: the Weapon bone sits at the grip facing forward -> align the socket to the model's forward
                gunSocket = new GameObject("GunSocket").transform;
                gunSocket.SetParent(weaponBone, false);
                gunSocket.position = weaponBone.position;
                gunSocket.rotation = marshal.transform.rotation;
            }
            return marshal;
        }
    }
}
