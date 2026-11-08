using UnityEngine;

namespace Zcom
{
    /// <summary>3D look of every item. Used by world pickups and to render inventory icons.</summary>
    public static class ItemVisuals
    {
        public static void Build(ItemId id, Transform root)
        {
            var def = Items.Get(id);
            if (def.category == ItemCategory.Weapon)
            {
                var model = new GameObject("Model").transform;
                model.SetParent(root, false);
                model.localPosition = new Vector3(0f, 0f, -0.35f); // centre the gun on the pivot
                WeaponModels.Build(def.weapon, model);
                return;
            }

            Color tint = def.color;
            const float size = 0.3f;
            switch (id)
            {
                case ItemId.Stone:
                    {
                        var m = Mats.Cached("ResStone", () => Mats.Lit(tint * 0.8f, 0.1f, 0.25f, Mats.HDR(tint, 0.15f)));
                        Prims.Part("Rock", PrimitiveType.Cube, root, Vector3.zero, Vector3.one * size, m, new Vector3(20, 35, 10));
                        Prims.Part("Rock2", PrimitiveType.Cube, root, new Vector3(0.1f, 0.07f, 0f), Vector3.one * size * 0.6f, m, new Vector3(45, 10, 30));
                        Prims.Part("Rock3", PrimitiveType.Cube, root, new Vector3(-0.09f, -0.05f, 0.05f), Vector3.one * size * 0.5f, m, new Vector3(10, 60, 45));
                        break;
                    }
                case ItemId.Gold:
                    {
                        var m = Mats.Cached("ResGold", () => Mats.Lit(tint, 1f, 0.85f, Mats.HDR(tint, 0.6f)));
                        Prims.Part("Nugget", PrimitiveType.Sphere, root, Vector3.zero, new Vector3(1f, 0.75f, 0.85f) * size, m);
                        Prims.Part("Nugget2", PrimitiveType.Sphere, root, new Vector3(0.1f, 0.06f, 0.03f), Vector3.one * size * 0.55f, m);
                        Prims.Part("Nugget3", PrimitiveType.Sphere, root, new Vector3(-0.09f, -0.03f, 0.05f), Vector3.one * size * 0.45f, m);
                        break;
                    }
                default:
                    {
                        var m = Mats.Cached("ResXenite", () => Mats.Lit(tint * 0.3f, 0f, 0.95f, Mats.HDR(tint, 2.5f)));
                        Prims.Part("Crystal", PrimitiveType.Cube, root, Vector3.zero, new Vector3(0.6f, 1.6f, 0.6f) * size, m, new Vector3(0, 45, 12));
                        Prims.Part("Shard", PrimitiveType.Cube, root, new Vector3(0.1f, -0.05f, 0f), new Vector3(0.4f, 1f, 0.4f) * size, m, new Vector3(0, 45, -30));
                        Prims.Part("Shard2", PrimitiveType.Cube, root, new Vector3(-0.09f, -0.07f, 0.03f), new Vector3(0.35f, 0.8f, 0.35f) * size, m, new Vector3(20, 45, 35));
                        break;
                    }
            }
        }
    }
}
