using System.IO;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// The operative's persistent progress between raids (JSON in persistentDataPath): credits, the stash, the loadout
    /// taken into the next raid, suit upgrades and raid stats. Extraction banks what you carried (backpack -> stash,
    /// weapons stay in the loadout); dying loses both. An empty loadout deploys with a standard-issue Ion Carbine.
    /// </summary>
    public static class Profile
    {
        [System.Serializable]
        class Data
        {
            public int version = 1;
            public int credits;
            public ItemStack[] stash = new ItemStack[0];
            public ItemStack[] loadout = new ItemStack[0];
            public int suit, shield, grenade;
            public int raids, extractions, deaths;
            public int lastResult;       // 0 none, 1 extracted, 2 KIA
            public int lastValue;
        }

        public class Upgrade
        {
            public string id, name, desc;
            public int[] prices;
            public Upgrade(string id, string name, string desc, params int[] prices) { this.id = id; this.name = name; this.desc = desc; this.prices = prices; }
            public int MaxLevel => prices.Length;
        }

        public const int StashSize = 30, LoadoutSize = 3;
        public const float HealthPerLevel = 20f, ShieldPerLevel = 15f;
        public static readonly Upgrade[] Upgrades =
        {
            new Upgrade("suit", "SUIT PLATING", "+20 max health per level", 400, 900, 1600),
            new Upgrade("shield", "SHIELD EMITTER", "+15 shield per level", 450, 1000, 1800),
            new Upgrade("grenade", "GRENADE RIG", "+1 grenade charge per level", 700, 1500),
        };

        /// <summary>Tests point this at their own file so they never touch the player's profile.</summary>
        public static string PathOverride;
        static string FilePath => PathOverride ?? Path.Combine(Application.persistentDataPath, "zcom_profile.json");

        static Data data;
        static ItemContainer stash, loadout;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { data = null; stash = loadout = null; }

        static Data D { get { if (data == null) Load(); return data; } }
        public static ItemContainer Stash { get { if (data == null) Load(); return stash; } }
        public static ItemContainer Loadout { get { if (data == null) Load(); return loadout; } }
        public static int Credits => D.credits;
        public static int Raids => D.raids;
        public static int Extractions => D.extractions;
        public static int Deaths => D.deaths;
        /// <summary>1 = extracted, 2 = KIA, 0 = no raid yet.</summary>
        public static int LastResult => D.lastResult;
        public static int LastValue => D.lastValue;
        public static int Version { get; private set; }

        public static int Level(string id) => id == "suit" ? D.suit : id == "shield" ? D.shield : id == "grenade" ? D.grenade : 0;
        public static float HealthBonus => D.suit * HealthPerLevel;
        public static float ShieldBonus => D.shield * ShieldPerLevel;
        public static int GrenadeBonus => D.grenade;

        static void Load()
        {
            data = null;
            try { if (File.Exists(FilePath)) data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath)); }
            catch (System.Exception e) { Debug.LogWarning($"[Profile] could not read {FilePath}: {e.Message}"); }
            if (data == null)
            {
                data = new Data();
                data.loadout = new[] { new ItemStack(ItemId.IonCarbine, 1) };
            }
            stash = new ItemContainer("STASH", StashSize);
            loadout = new ItemContainer("LOADOUT", LoadoutSize, Items.IsWeapon);
            for (int i = 0; i < data.stash.Length && i < StashSize; i++) stash.Set(i, data.stash[i]);
            for (int i = 0; i < data.loadout.Length && i < LoadoutSize; i++) loadout.Set(i, data.loadout[i]);
            Version++;
        }

        public static void Save()
        {
            var d = D;
            d.stash = (ItemStack[])stash.slots.Clone();
            d.loadout = (ItemStack[])loadout.slots.Clone();
            Version++;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(d, true));
            }
            catch (System.Exception e) { Debug.LogWarning($"[Profile] could not write {FilePath}: {e.Message}"); }
        }

        /// <summary>Wipes all progress (fresh operative).</summary>
        public static void ResetAll()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
            Load();
            Save();
        }

        // ------------------------------------------------------------------ raid lifecycle

        /// <summary>Fills a new raid's loadout from the profile (standard-issue carbine if it is empty).</summary>
        public static void FillLoadout(ItemContainer weaponSlots)
        {
            bool any = false;
            for (int i = 0; i < weaponSlots.Size && i < Loadout.Size; i++)
                if (!Loadout[i].IsEmpty) { weaponSlots.Set(i, Loadout[i]); any = true; }
            if (!any) weaponSlots.Set(0, new ItemStack(ItemId.IonCarbine, 1));
        }

        public static void BeginRaid() { D.raids++; Save(); }

        /// <summary>Extracted: weapons carried become the loadout, the backpack goes to the stash (overflow is sold).</summary>
        public static int CommitExtraction(Inventory inv)
        {
            int value = 0;
            for (int i = 0; i < Loadout.Size; i++)
                loadout.Set(i, inv && i < inv.WeaponSlots.Size ? inv.WeaponSlots[i] : ItemStack.Empty);
            if (inv)
                foreach (var st in inv.Backpack.slots)
                {
                    if (st.IsEmpty) continue;
                    value += st.Def.value * st.count;
                    int left = stash.Add(st);
                    if (left > 0) D.credits += st.Def.value * left;      // no room: the quartermaster buys it
                }
            D.extractions++;
            D.lastResult = 1;
            D.lastValue = value;
            Save();
            return value;
        }

        /// <summary>KIA: everything taken into the raid is lost.</summary>
        public static void CommitDeath(Inventory inv)
        {
            int value = 0;
            if (inv)
            {
                foreach (var st in inv.Backpack.slots) if (!st.IsEmpty) value += st.Def.value * st.count;
                foreach (var st in inv.WeaponSlots.slots) if (!st.IsEmpty) value += st.Def.value;
            }
            for (int i = 0; i < Loadout.Size; i++) loadout.Set(i, ItemStack.Empty);
            D.deaths++;
            D.lastResult = 2;
            D.lastValue = value;
            Save();
        }

        /// <summary>Test / dev helper: adds credits.</summary>
        public static void Grant(int credits) { D.credits += credits; Save(); }

        // ------------------------------------------------------------------ base actions

        public static int SellPrice(ItemStack s) => s.IsEmpty ? 0 : s.Def.value * s.count;
        public static int BuyPrice(WeaponKind k) => Items.Get(Items.ForWeapon(k)).value * 3 / 2;
        public static int UpgradePrice(Upgrade u) { int l = Level(u.id); return l < u.MaxLevel ? u.prices[l] : -1; }

        public static int Sell(ItemContainer c, int i)
        {
            var s = c[i];
            if (s.IsEmpty) return 0;
            int v = SellPrice(s);
            c.Set(i, ItemStack.Empty);
            D.credits += v;
            Save();
            return v;
        }

        public static int SellAllResources()
        {
            int v = 0;
            for (int i = 0; i < stash.Size; i++)
            {
                var s = stash[i];
                if (s.IsEmpty || Items.IsWeapon(s.id)) continue;
                v += SellPrice(s);
                stash.Set(i, ItemStack.Empty);
            }
            if (v > 0) { D.credits += v; Save(); }
            return v;
        }

        public static bool BuyWeapon(WeaponKind k)
        {
            int p = BuyPrice(k);
            if (D.credits < p || stash.FirstEmpty() < 0) return false;
            D.credits -= p;
            stash.Add(new ItemStack(Items.ForWeapon(k), 1));
            Save();
            return true;
        }

        public static bool BuyUpgrade(Upgrade u)
        {
            int p = UpgradePrice(u);
            if (p < 0 || D.credits < p) return false;
            D.credits -= p;
            if (u.id == "suit") D.suit++; else if (u.id == "shield") D.shield++; else if (u.id == "grenade") D.grenade++;
            Save();
            return true;
        }

        /// <summary>Stash weapon -> first free loadout slot (or swaps with slot 1); loadout weapon -> stash.</summary>
        public static bool ToggleEquip(ItemContainer from, int i)
        {
            var s = from[i];
            if (s.IsEmpty || !Items.IsWeapon(s.id)) return false;
            bool ok;
            if (from == loadout)
            {
                int e = stash.FirstEmpty();
                ok = e >= 0 && ItemContainer.Move(loadout, i, stash, e);
            }
            else
            {
                int e = loadout.FirstEmpty();
                ok = ItemContainer.Move(stash, i, loadout, e >= 0 ? e : 0);
            }
            if (ok) Save();
            return ok;
        }
    }
}
