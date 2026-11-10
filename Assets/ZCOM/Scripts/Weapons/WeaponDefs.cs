using UnityEngine;

namespace Zcom
{
    public enum WeaponKind { IonCarbine, NovaScatter, HelixBeam }

    /// <summary>All tunable numbers for one energy weapon. Edit the values in <see cref="WeaponDefs"/>.</summary>
    public class WeaponDef
    {
        public WeaponKind kind;
        public string displayName;
        public string description;
        public string rarity;
        public Color rarityColor;
        public Color color;             // energy colour: bolts, beam, glow, flashes

        // Damage
        public float fireRate;          // shots per second (bolt weapons, hold to fire)
        public float damage;            // per bolt / per beam tick
        public int pellets = 1;
        public int pierce;              // extra enemies a shot passes through
        public float critChance = 0.1f;
        public float critMultiplier = 2f;
        public float range = 40f;
        public float knockback = 2.5f;

        // Energy cell (magazine)
        public int cellSize;
        public float rechargeTime;

        // Spread (degrees)
        public float baseSpread;
        public float spreadPerShot;
        public float maxSpread;
        public float spreadRecovery = 12f;
        public float moveSpread;

        // Plasma bolts
        public float boltSpeed = 120f;
        public float boltLength = 1.5f;
        public float boltWidth = 0.13f;

        // Continuous beam
        public bool beam;
        public float tickInterval = 0.08f;
        public int energyPerTick = 2;

        // Feel
        public float shake;
        public float cameraKick;
        public float kickBack;
        public float kickPitch;
        public float playerRecoil;
        public float hitStopPerShot;
        public float flashScale = 1f;
        public string sound;
        public float soundVolume = 0.45f;

        public float Dps => beam ? damage / tickInterval : fireRate * damage * pellets;
        public float ShotsPerCell => beam ? cellSize / (float)energyPerTick * tickInterval : cellSize;
    }

    public static class WeaponDefs
    {
        static WeaponDef ion, nova, helix;

        public static WeaponDef Get(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.NovaScatter: return nova ??= MakeNova();
                case WeaponKind.HelixBeam: return helix ??= MakeHelix();
                default: return ion ??= MakeIon();
            }
        }

        static WeaponDef MakeIon() => new WeaponDef
        {
            kind = WeaponKind.IonCarbine,
            displayName = "ION CARBINE",
            description = "Rapid-fire plasma carbine. Hold to spray ionised bolts.",
            rarity = "STANDARD",
            rarityColor = new Color(0.55f, 0.85f, 1f),
            color = new Color(0.25f, 0.85f, 1f),
            fireRate = 10f, damage = 13f, critChance = 0.12f, range = 38f, knockback = 2.4f,
            cellSize = 32, rechargeTime = 1.3f,
            baseSpread = 0.8f, spreadPerShot = 0.55f, maxSpread = 5.5f, spreadRecovery = 12f, moveSpread = 1.8f,
            boltSpeed = 110f, boltLength = 2.2f, boltWidth = 0.22f,
            shake = 0.035f, cameraKick = 0.05f, kickBack = 0.12f, kickPitch = 9f, playerRecoil = 0.05f,
            flashScale = 1f, sound = "ion", soundVolume = 0.4f,
        };

        static WeaponDef MakeNova() => new WeaponDef
        {
            kind = WeaponKind.NovaScatter,
            displayName = "NOVA SCATTER",
            description = "Unloads a fan of superheated plasma shards. Devastating up close, shoves aliens back.",
            rarity = "RARE",
            rarityColor = new Color(1f, 0.45f, 0.7f),
            color = new Color(1f, 0.35f, 0.65f),
            fireRate = 1.6f, damage = 9f, pellets = 8, critChance = 0.08f, range = 15f, knockback = 3.4f,
            cellSize = 6, rechargeTime = 1.6f,
            baseSpread = 8f, spreadPerShot = 0f, maxSpread = 8f, spreadRecovery = 20f, moveSpread = 1.5f,
            boltSpeed = 70f, boltLength = 1.1f, boltWidth = 0.2f,
            shake = 0.15f, cameraKick = 0.18f, kickBack = 0.24f, kickPitch = 20f, playerRecoil = 0.15f,
            hitStopPerShot = 0f, flashScale = 1.7f, sound = "nova", soundVolume = 0.6f,
        };

        static WeaponDef MakeHelix() => new WeaponDef
        {
            kind = WeaponKind.HelixBeam,
            displayName = "HELIX BEAM",
            description = "Hold for a continuous twin-helix laser that burns through up to 4 aliens in a line.",
            rarity = "EPIC",
            rarityColor = new Color(0.75f, 0.45f, 1f),
            color = new Color(0.7f, 0.35f, 1f),
            beam = true, tickInterval = 0.08f, energyPerTick = 2,
            damage = 12f, pierce = 3, critChance = 0f, range = 20f, knockback = 0.6f,
            cellSize = 100, rechargeTime = 2f,
            shake = 0.012f, cameraKick = 0.01f, kickBack = 0.02f, kickPitch = 1.5f, playerRecoil = 0.01f,
            flashScale = 1.2f, sound = "beam_start", soundVolume = 0.5f,
        };
    }
}
