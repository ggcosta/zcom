using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Duckov-style inventory: a weapon loadout (3 slots, weapons only, hotkeys 1-3) and a backpack grid.
    /// Tab / I toggles the window; crates open as a side container. Items can be dropped into the world.
    /// </summary>
    public class Inventory : MonoBehaviour
    {
        public int backpackSize = 24;
        public int weaponSlotCount = 3;
        public List<WeaponKind> startingWeapons = new List<WeaponKind> { WeaponKind.IonCarbine };
        [Tooltip("A looted crate's window closes when you walk this far away.")]
        public float containerRange = 3.5f;

        public ItemContainer Backpack { get; private set; }
        public ItemContainer WeaponSlots { get; private set; }
        public int EquippedIndex { get; private set; }
        public bool PanelOpen { get; private set; }
        /// <summary>The crate (or other container) currently shown next to the backpack, if any.</summary>
        public ItemContainer OpenContainer { get; private set; }
        public string OpenContainerName { get; private set; }
        Transform openContainerAnchor;

        PlayerController owner;

        public int Version => Backpack.Version + WeaponSlots.Version * 7919 + EquippedIndex * 104729;

        void Awake()
        {
            owner = GetComponent<PlayerController>();
            Backpack = new ItemContainer("BACKPACK", backpackSize);
            WeaponSlots = new ItemContainer("LOADOUT", weaponSlotCount, Items.IsWeapon);
            foreach (var k in startingWeapons) WeaponSlots.Add(new ItemStack(Items.ForWeapon(k), 1));
        }

        // ------------------------------------------------------------------ queries

        public int Count(ResourceType type) => Backpack.CountOf(Items.ForResource(type));

        public bool HasWeapon(WeaponKind kind)
        {
            var id = Items.ForWeapon(kind);
            return WeaponSlots.CountOf(id) > 0 || Backpack.CountOf(id) > 0;
        }

        public WeaponKind? WeaponIn(int slot)
        {
            if (slot < 0 || slot >= WeaponSlots.Size || WeaponSlots[slot].IsEmpty) return null;
            return WeaponSlots[slot].Def.weapon;
        }

        // ------------------------------------------------------------------ changes

        /// <summary>Puts an item into the inventory (weapons go to a free loadout slot first). Returns what did not fit.</summary>
        public int AddItem(ItemStack s)
        {
            if (s.IsEmpty) return 0;
            int left = s.count;
            if (Items.IsWeapon(s.id)) left = WeaponSlots.Add(new ItemStack(s.id, left));
            if (left > 0) left = Backpack.Add(new ItemStack(s.id, left));
            return left;
        }

        public void Equip(int slot)
        {
            if (WeaponIn(slot) == null) return;
            EquippedIndex = slot;
            WeaponSlots.Touch();
        }

        /// <summary>Drops a stack in front of the operative as a world item (pick it up again with E).</summary>
        public void DropToWorld(ItemStack s)
        {
            if (s.IsEmpty) return;
            Vector3 fwd = owner ? owner.AimDir : transform.forward;
            Vector2 j = Random.insideUnitCircle * 0.5f;
            Vector3 pos = transform.position + fwd * 1.3f + new Vector3(j.x, 1.2f, j.y);
            ItemPickup.Spawn(s, pos, fwd * 2f + Vector3.up * 2.5f, false);
            ProcAudio.Play("throw", 0.35f, 0.1f, 1.3f);
        }

        public void OpenPanel(ItemContainer container = null, string containerName = null, Transform anchor = null)
        {
            if (!PanelOpen) ProcAudio.Play("equip", 0.3f, 0f, 1.3f);
            PanelOpen = true;
            OpenContainer = container;
            OpenContainerName = containerName;
            openContainerAnchor = anchor;
        }

        public void ClosePanel()
        {
            if (PanelOpen) ProcAudio.Play("equip", 0.3f, 0f, 1.05f);
            PanelOpen = false;
            OpenContainer = null;
            openContainerAnchor = null;
        }

        /// <summary>Moves everything from the open container into the inventory.</summary>
        public int TakeAll()
        {
            if (OpenContainer == null) return 0;
            int moved = 0;
            for (int i = 0; i < OpenContainer.Size; i++)
            {
                var s = OpenContainer[i];
                if (s.IsEmpty) continue;
                int left = AddItem(s);
                moved += s.count - left;
                OpenContainer.Set(i, new ItemStack(s.id, left));
            }
            if (moved > 0) ProcAudio.Play("pickup_res", 0.5f, 0f, 1.1f);
            return moved;
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            var input = PlayerInputSource.Current;
            if (input.inventory)
            {
                if (PanelOpen) ClosePanel(); else OpenPanel();
            }
            else if (input.cancel && PanelOpen) ClosePanel();

            if (PanelOpen && openContainerAnchor)
            {
                Vector3 d = openContainerAnchor.position - transform.position; d.y = 0f;
                if (d.magnitude > containerRange) ClosePanel();
            }
            if (PanelOpen && OpenContainer != null && input.takeAll) TakeAll();

            if (!owner || !owner.CanAct) return;

            if (input.slot >= 0 && input.slot < WeaponSlots.Size && input.slot != EquippedIndex) Equip(input.slot);
            else if (input.cycle != 0 && !PanelOpen) CycleWeapon(input.cycle);

            SyncGun();
        }

        void CycleWeapon(int dir)
        {
            for (int k = 1; k < WeaponSlots.Size; k++)
            {
                int i = ((EquippedIndex + dir * k) % WeaponSlots.Size + WeaponSlots.Size) % WeaponSlots.Size;
                if (WeaponIn(i) != null) { Equip(i); return; }
            }
        }

        /// <summary>Keeps the held gun matching the equipped loadout slot (weapons can be dragged around).</summary>
        void SyncGun()
        {
            var gun = owner.gun;
            if (!gun) return;
            var kind = WeaponIn(EquippedIndex);
            if (kind == null)
            {
                for (int i = 0; i < WeaponSlots.Size; i++)
                    if (WeaponIn(i) != null) { EquippedIndex = i; kind = WeaponIn(i); WeaponSlots.Touch(); break; }
            }
            if (kind == null) { if (gun.Current != null) gun.Unequip(); return; }
            if (gun.Current == null || gun.Current.kind != kind.Value) gun.Equip(kind.Value, gun.Current != null);
        }

        // Legacy helpers used by HUD text.
        public static string Name(ResourceType t) => Items.Get(Items.ForResource(t)).name;
        public static Color Tint(ResourceType t) => Items.Get(Items.ForResource(t)).color;
        public static readonly ResourceType[] AllResources = { ResourceType.Stone, ResourceType.Gold, ResourceType.Xenite };
    }
}
