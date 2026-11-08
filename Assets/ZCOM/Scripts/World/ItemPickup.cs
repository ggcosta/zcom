using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Any item lying in the world. Alien drops are auto-collected (vacuumed in when you walk close);
    /// items you drop yourself, and weapons, need E. If the backpack is full the rest stays on the ground.
    /// </summary>
    public class ItemPickup : MonoBehaviour, IInteractable
    {
        public ItemStack stack;
        public bool autoCollect;
        public float magnetRange = 3.5f;
        public float lifetime = 40f;   // auto-collect drops only; dropped items stay

        Vector3 velocity;
        float t;
        bool grounded;
        Transform visual, glow, ring;
        float spin;
        float fullMsgT;

        ItemDef Def => Items.Get(stack.id);

        public static ItemPickup Spawn(ItemStack s, Vector3 pos, Vector3 velocity, bool autoCollect)
        {
            var go = new GameObject($"Item_{s.id}");
            go.transform.position = pos;
            var p = go.AddComponent<ItemPickup>();
            p.stack = s;
            p.autoCollect = autoCollect;
            p.velocity = velocity;
            p.spin = Random.value < 0.5f ? 140f : -140f;

            bool weapon = Items.IsWeapon(s.id);
            p.visual = new GameObject("Visual").transform;
            p.visual.SetParent(go.transform, false);
            p.visual.localScale = Vector3.one * (weapon ? 1.4f : 0.75f + Mathf.Min(s.count, 10) * 0.05f);
            ItemVisuals.Build(s.id, p.visual);

            Color tint = p.Def.color;
            float glowI = s.id == ItemId.Stone ? 0.5f : 1.3f;
            var glowMat = Mats.Cached($"ItemGlow{s.id}", () => Mats.Additive(ProcTex.Glow, Mats.HDR(tint, glowI), false));
            p.glow = Prims.Part("Glow", PrimitiveType.Quad, go.transform, Vector3.zero, Vector3.one * (weapon ? 2.2f : 1.1f), glowMat, default, false).transform;
            if (!autoCollect || weapon)
            {
                var ringMat = Mats.Cached($"ItemRing{s.id}", () => Mats.Additive(ProcTex.Ring, Mats.HDR(p.Def.rarityColor, 2f), false));
                p.ring = Prims.Part("Ring", PrimitiveType.Quad, go.transform, Vector3.zero, Vector3.one * 1.4f, ringMat, new Vector3(90, 0, 0), false).transform;
            }
            if (weapon || s.id == ItemId.Xenite)
            {
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = tint;
                l.range = weapon ? 4f : 2.5f;
                l.intensity = weapon ? 2.5f : 1.5f;
                l.shadows = LightShadows.None;
            }
            return p;
        }

        // ------------------------------------------------------------------ IInteractable

        public Vector3 PromptPosition => transform.position + Vector3.up * 0.6f;
        public string Prompt => stack.count > 1 ? $"[E]  TAKE {stack.count}x {Def.name}" : $"[E]  TAKE {Def.name}";
        public Color PromptColor => Def.rarityColor;
        public bool CanInteract => !autoCollect && t > 0.4f;

        void OnEnable() { Interactables.All.Add(this); }
        void OnDisable() { Interactables.All.Remove(this); }

        public void Interact(PlayerController player)
        {
            if (player.inventory) Collect(player.inventory, true);
        }

        void Collect(Inventory inv, bool manual)
        {
            int before = stack.count;
            int left = inv.AddItem(stack);
            int got = before - left;
            Color tint = Def.color;
            if (got > 0)
            {
                ProcAudio.Play(Items.IsWeapon(stack.id) ? "pickup" : "pickup_res", 0.45f, 0.05f,
                    stack.id == ItemId.Stone ? 0.8f : stack.id == ItemId.Gold ? 1.1f : 1.4f);
                if (Hud.I)
                {
                    if (Items.IsWeapon(stack.id)) Hud.I.Banner(Def.name, Def.description);
                    else Hud.I.WorldText(transform.position + Vector3.up * 0.6f, $"+{got} {Def.name}", tint, 24);
                }
                Vfx.Burst(transform.position, tint, 6);
            }
            if (left <= 0) { Destroy(gameObject); return; }

            stack.count = left;
            // Backpack full: stop vacuuming, leave it on the ground for later.
            if (autoCollect) { autoCollect = false; t = 0.4f; }
            if ((manual || Time.time > fullMsgT) && Hud.I)
            {
                fullMsgT = Time.time + 3f;
                Hud.I.WorldText(transform.position + Vector3.up, "BACKPACK FULL", new Color(1f, 0.4f, 0.3f), 26);
            }
        }

        // ------------------------------------------------------------------ motion

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            Vector3 pos = transform.position;
            var player = PlayerController.I;
            var inv = player ? player.inventory : null;
            float rest = Items.IsWeapon(stack.id) ? 0.9f : 0.35f;

            Vector3 target = player ? player.transform.position + Vector3.up : pos;
            float dist = Vector3.Distance(target, pos);
            if (autoCollect && inv && player.CanAct && dist < magnetRange && t > 0.5f)
            {
                velocity = Vector3.Lerp(velocity, (target - pos).normalized * 16f, 1f - Mathf.Exp(-10f * dt));
                pos += velocity * dt;
                transform.position = pos;
                if (dist < 0.7f) Collect(inv, false);
                if (!this) return;
            }
            else if (!grounded)
            {
                velocity += Physics.gravity * dt;
                pos += velocity * dt;
                if (pos.y < rest)
                {
                    pos.y = rest;
                    if (velocity.y < -2.5f) velocity = new Vector3(velocity.x * 0.5f, -velocity.y * 0.35f, velocity.z * 0.5f);
                    else { velocity = Vector3.zero; grounded = true; }
                }
                transform.position = pos;
            }
            else
            {
                pos.y = rest + 0.08f + Mathf.Sin(t * 2.6f + GetInstanceID()) * 0.08f;
                transform.position = pos;
            }

            if (visual) visual.localRotation = Quaternion.Euler(0f, t * spin * (Items.IsWeapon(stack.id) ? 0.5f : 1f), 0f);
            var cam = CameraRig.I ? CameraRig.I.Cam : Camera.main;
            if (glow && cam) glow.rotation = cam.transform.rotation;
            if (ring)
            {
                ring.position = new Vector3(pos.x, 0.06f, pos.z);
                float s = 1.2f + Mathf.Sin(t * 3f) * 0.12f;
                ring.localScale = new Vector3(s, s, s);
            }

            if (autoCollect)
            {
                if (t > lifetime - 2f) transform.localScale = Vector3.one * Mathf.Clamp01((lifetime - t) / 2f);
                if (t > lifetime) Destroy(gameObject);
            }
        }
    }

    /// <summary>Who drops what. Tune drop rates and crate contents here.</summary>
    public static class LootTable
    {
        public static void AlienDrop(AlienType type, Vector3 pos)
        {
            switch (type)
            {
                case AlienType.Skitter:
                    if (Random.value < 0.3f) Drop(Roll(0.7f, 0.25f), 1, pos);
                    break;
                case AlienType.Spitter:
                    if (Random.value < 0.5f) Drop(Roll(0.35f, 0.25f), Random.Range(1, 3), pos);
                    break;
                case AlienType.Brute:
                    Drop(ItemId.Stone, Random.Range(2, 4), pos);
                    Drop(ItemId.Gold, Random.Range(1, 3), pos);
                    Drop(ItemId.Xenite, Random.Range(1, 3), pos);
                    break;
            }
        }

        /// <summary>What a supply crate holds: optionally its weapon, plus resources by loot tier.</summary>
        public static List<ItemStack> CrateContents(LootChest crate)
        {
            var list = new List<ItemStack>();
            if (crate.containsWeapon) list.Add(new ItemStack(Items.ForWeapon(crate.weapon), 1));
            switch (crate.loot)
            {
                case CrateLoot.Gold:
                    list.Add(new ItemStack(ItemId.Gold, Random.Range(4, 8)));
                    list.Add(new ItemStack(ItemId.Stone, Random.Range(1, 4)));
                    break;
                case CrateLoot.Xenite:
                    list.Add(new ItemStack(ItemId.Xenite, Random.Range(2, 5)));
                    list.Add(new ItemStack(ItemId.Stone, Random.Range(2, 5)));
                    break;
                default:
                    list.Add(new ItemStack(ItemId.Stone, Random.Range(3, 7)));
                    list.Add(new ItemStack(ItemId.Gold, Random.Range(1, 3)));
                    if (Random.value < 0.3f) list.Add(new ItemStack(ItemId.Xenite, 1));
                    break;
            }
            return list;
        }

        static ItemId Roll(float stone, float gold)
        {
            float r = Random.value;
            return r < stone ? ItemId.Stone : r < stone + gold ? ItemId.Gold : ItemId.Xenite;
        }

        public static void Drop(ItemId id, int amount, Vector3 pos)
        {
            Vector2 c = Random.insideUnitCircle * 2.2f;
            ItemPickup.Spawn(new ItemStack(id, amount), pos + Vector3.up * 0.6f, new Vector3(c.x, Random.Range(4.5f, 6.5f), c.y), true);
        }
    }
}
