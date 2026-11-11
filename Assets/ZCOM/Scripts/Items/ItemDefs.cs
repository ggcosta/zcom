using System.Collections.Generic;
using UnityEngine;

namespace Zcom
{
    public enum ResourceType { Stone, Gold, Xenite }

    public enum ItemId { None, Stone, Gold, Xenite, IonCarbine, NovaScatter, HelixBeam }

    public enum ItemCategory { Resource, Weapon }

    /// <summary>Static description of an item type.</summary>
    public class ItemDef
    {
        public ItemId id;
        public string name;
        public string description;
        public ItemCategory category;
        public int maxStack = 1;
        public Color color;           // icon/glow tint
        public string rarity;
        public Color rarityColor;
        public WeaponKind weapon;     // when category == Weapon
        public float weight;          // kg
        public int value;             // credits
        public string icon;           // Resources/UI sprite name (optional)
    }

    /// <summary>A stack of items in a slot. Empty when id == None or count &lt;= 0.</summary>
    [System.Serializable]
    public struct ItemStack
    {
        public ItemId id;
        public int count;

        public ItemStack(ItemId id, int count) { this.id = id; this.count = count; }
        public bool IsEmpty => id == ItemId.None || count <= 0;
        public ItemDef Def => Items.Get(id);
        public static readonly ItemStack Empty = default;
    }

    /// <summary>Item registry. Add new items here.</summary>
    public static class Items
    {
        static Dictionary<ItemId, ItemDef> defs;

        public static ItemDef Get(ItemId id)
        {
            if (defs == null) Build();
            return defs.TryGetValue(id, out var d) ? d : defs[ItemId.Stone];
        }

        public static ItemId ForWeapon(WeaponKind k) =>
            k == WeaponKind.NovaScatter ? ItemId.NovaScatter : k == WeaponKind.HelixBeam ? ItemId.HelixBeam : ItemId.IonCarbine;

        public static ItemId ForResource(ResourceType t) =>
            t == ResourceType.Stone ? ItemId.Stone : t == ResourceType.Gold ? ItemId.Gold : ItemId.Xenite;

        public static bool IsWeapon(ItemId id) => id != ItemId.None && Get(id).category == ItemCategory.Weapon;

        static void Build()
        {
            defs = new Dictionary<ItemId, ItemDef>();
            void Res(ItemId id, string name, string desc, int stack, Color c, string rarity, Color rc, float kg, int val, string icon) =>
                defs[id] = new ItemDef { id = id, name = name, description = desc, category = ItemCategory.Resource, maxStack = stack, color = c, rarity = rarity, rarityColor = rc, weight = kg, value = val, icon = icon };

            Res(ItemId.Stone, "STONE", "Dense asteroid rock. Basic building material.", 30,
                new Color(0.62f, 0.64f, 0.68f), "COMMON", new Color(0.7f, 0.72f, 0.76f), 0.8f, 5, "item_stone");
            Res(ItemId.Gold, "GOLD", "Conductive precious metal. Valuable for upgrades and trade.", 20,
                new Color(1f, 0.78f, 0.2f), "UNCOMMON", new Color(0.45f, 0.85f, 0.4f), 0.4f, 60, "item_gold");
            Res(ItemId.Xenite, "XENITE", "Glowing alien crystal harvested from xeno tissue. Highly energetic.", 10,
                new Color(0.35f, 1f, 0.55f), "RARE", new Color(0.35f, 0.65f, 1f), 0.3f, 240, "item_xenite");

            foreach (WeaponKind k in System.Enum.GetValues(typeof(WeaponKind)))
            {
                var w = WeaponDefs.Get(k);
                var id = ForWeapon(k);
                defs[id] = new ItemDef
                {
                    id = id, name = w.displayName, description = w.description, category = ItemCategory.Weapon,
                    maxStack = 1, color = w.color, rarity = w.rarity, rarityColor = w.rarityColor, weapon = k,
                    icon = "weapon_" + k.ToString().ToLowerInvariant(),      // painted profile (Resources/UI), else a 3D render
                    weight = k == WeaponKind.HelixBeam ? 4.6f : k == WeaponKind.NovaScatter ? 3.8f : 3.1f,
                    value = k == WeaponKind.HelixBeam ? 2400 : k == WeaponKind.NovaScatter ? 1200 : 500,
                };
            }
        }
    }
}
