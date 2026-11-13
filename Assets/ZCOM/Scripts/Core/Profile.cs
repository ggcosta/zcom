using System.IO;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// The operative's persistent state (JSON in persistentDataPath): the ship's stash and what they carry (loadout +
    /// backpack). The ship saves the carry as you rearrange it; a raid only commits it when it ends: extracting keeps
    /// everything you carried, dying (or abandoning the raid, or quitting mid-raid) loses it all. An empty loadout is
    /// refilled with a standard-issue Ion Carbine so you never deploy unarmed.
    /// </summary>
    public static class Profile
    {
        [System.Serializable]
        class Data
        {
            public int version = 1;
            public ItemStack[] stash = new ItemStack[0];
            public ItemStack[] loadout = new ItemStack[0];
            public ItemStack[] backpack = new ItemStack[0];
            public bool inRaid;
            public int raids, extractions, deaths;
        }

        public const int StashSize = 25;

        /// <summary>Tests point this at their own file so they never touch the player's profile.</summary>
        public static string PathOverride;
        static string FilePath => PathOverride ?? Path.Combine(Application.persistentDataPath, "zcom_profile.json");

        static Data data;
        static ItemContainer stash;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { data = null; stash = null; }

        static Data D { get { if (data == null) Load(); return data; } }
        public static ItemContainer Stash { get { if (data == null) Load(); return stash; } }
        public static int Raids => D.raids;
        public static int Extractions => D.extractions;
        public static int Deaths => D.deaths;

        static void Load()
        {
            data = null;
            try { if (File.Exists(FilePath)) data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath)); }
            catch (System.Exception e) { Debug.LogWarning($"[Profile] could not read {FilePath}: {e.Message}"); }
            if (data == null) data = new Data { loadout = new[] { new ItemStack(ItemId.IonCarbine, 1) } };
            stash = new ItemContainer("STASH", StashSize) { isStash = true };
            for (int i = 0; i < data.stash.Length && i < StashSize; i++) stash.Set(i, data.stash[i]);
            // The game was closed (or crashed) during a raid: that raid counts as lost.
            if (data.inRaid) LoseCarry();
        }

        public static void Save()
        {
            var d = D;
            d.stash = (ItemStack[])stash.slots.Clone();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(d, true));
            }
            catch (System.Exception e) { Debug.LogWarning($"[Profile] could not write {FilePath}: {e.Message}"); }
        }

        // ------------------------------------------------------------------ carry (loadout + backpack)

        /// <summary>Fills a fresh inventory with what the operative carries.</summary>
        public static void LoadCarry(Inventory inv)
        {
            var d = D;
            // a new scene while the last raid is still running (restart / abandon mid-raid): that raid is lost
            if (d.inRaid) { LoseCarry(); Save(); }
            bool armed = false;
            for (int i = 0; i < d.loadout.Length && i < inv.WeaponSlots.Size; i++)
                if (!d.loadout[i].IsEmpty && Items.IsWeapon(d.loadout[i].id)) { inv.WeaponSlots.Set(i, d.loadout[i]); armed = true; }
            for (int i = 0; i < d.backpack.Length && i < inv.Backpack.Size; i++) inv.Backpack.Set(i, d.backpack[i]);
            if (!armed) inv.WeaponSlots.Set(0, new ItemStack(ItemId.IonCarbine, 1));      // standard issue
        }

        public static void SaveCarry(Inventory inv)
        {
            if (!inv) return;
            D.loadout = (ItemStack[])inv.WeaponSlots.slots.Clone();
            D.backpack = (ItemStack[])inv.Backpack.slots.Clone();
            Save();
        }

        static void LoseCarry()
        {
            data.loadout = new ItemStack[0];
            data.backpack = new ItemStack[0];
            data.inRaid = false;
            data.deaths++;
        }

        // ------------------------------------------------------------------ raid lifecycle

        public static bool InRaid => D.inRaid;

        /// <summary>A raid scene started: from now on the carry is at stake.</summary>
        public static void BeginRaid()
        {
            if (D.inRaid) return;
            D.inRaid = true;
            D.raids++;
            Save();
        }

        /// <summary>Extracted: everything carried comes home.</summary>
        public static void CommitExtraction(Inventory inv)
        {
            D.inRaid = false;
            D.extractions++;
            SaveCarry(inv);
        }

        /// <summary>KIA or abandoned: everything carried is lost.</summary>
        public static void CommitDeath()
        {
            if (!D.inRaid) return;
            LoseCarry();
            Save();
        }

        /// <summary>Wipes all progress (fresh operative). Dev / test helper.</summary>
        public static void ResetAll()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
            Load();
            Save();
        }
    }
}
