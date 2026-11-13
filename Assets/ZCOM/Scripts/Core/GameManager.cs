using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Zcom
{
    /// <summary>
    /// Scene manager for both instances. Ship (hub, <see cref="hub"/>): inside the KESTREL, no combat, the stash is open
    /// and the carry is saved as you rearrange it; the rear door deploys. Raid (KEPLER-7): bakes the navmesh, tracks
    /// kills / score, game over and extraction (holding the ship's door), and commits the outcome to the
    /// <see cref="Profile"/>. Aliens live in <see cref="AlienCamp"/>s placed around the map (no waves).
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }
        public const string ShipScene = "Ship", RaidScene = "VerticalSlice";

        [Tooltip("This scene is the inside of the ship: no combat, nothing at stake, the stash and the exit door.")]
        public bool hub;
        /// <summary>Aboard the ship (the hub instance).</summary>
        public bool PreRaid => hub;
        /// <summary>True aboard the ship (weapons, grenades and the scan stay holstered).</summary>
        public static bool InHub
        {
            get
            {
                if (!I) I = FindFirstObjectByType<GameManager>();      // the HUD asks during its Awake, before ours
                return I && I.hub;
            }
        }
        /// <summary>A scene transition is running: the operative stops taking input.</summary>
        public bool Busy { get; private set; }

        public NavMeshSurface navSurface;
        public PlayerController player;
        public string mapName = "KEPLER-7";
        [TextArea] public string mapSubtitle = "Explore the moon outpost. Loot what you can. Stay alive.";

        public int Kills { get; private set; }
        public int Score { get; private set; }
        public bool IsGameOver { get; private set; }
        public bool IsExtracted { get; private set; }
        public float RaidTime => hub ? 0f : Time.timeSinceLevelLoad;
        public ExtractionZone Extraction { get; private set; }
        public int Hostiles => Alien.All.Count;
        AlienCamp[] camps;
        public int CampsTotal => camps != null ? camps.Length : 0;
        public AlienCamp[] Camps => camps;
        public int CampsCleared
        {
            get
            {
                int n = 0;
                if (camps != null) foreach (var c in camps) if (c && c.Spawned && c.Alive == 0) n++;
                return n;
            }
        }
        public int AlertedHostiles
        {
            get
            {
                int n = 0;
                foreach (var a in Alien.All) if (a && a.IsAlerted) n++;
                return n;
            }
        }

        /// <summary>Name of the area the player is in.</summary>
        public string CurrentArea
        {
            get
            {
                var z = player ? MapZone.At(player.transform.position) : null;
                return z ? z.zoneName : "WILDS";
            }
        }

        float gameOverTime;
        bool clearedAnnounced;

        void Awake()
        {
            I = this;
            Time.timeScale = 1f;
            if (navSurface) navSurface.BuildNavMesh();
            if (!player) player = FindFirstObjectByType<PlayerController>();
            camps = FindObjectsByType<AlienCamp>(FindObjectsSortMode.None);
            Extraction = FindFirstObjectByType<ExtractionZone>();
            if (!GetComponent<AmbientAudio>()) gameObject.AddComponent<AmbientAudio>();
            ScreenFade.In(0.8f);
        }

        void Start()
        {
            // after every Awake: the inventory has loaded the carry (a raid still marked as running would forfeit it)
            if (!hub) Profile.BeginRaid();
            if (!Hud.I) return;
            if (hub) Hud.I.Banner(mapName, mapSubtitle);
            else Hud.I.Banner(mapName, "Loot the moon, clear the camps, then hold the KESTREL's door to extract.");
        }

        int savedCarry = -1, savedStash = -1;
        float saveAt = -1f;

        void Update()
        {
            if ((IsGameOver || IsExtracted) && Time.unscaledTime - gameOverTime > 1.0f && PlayerInputSource.Current.restart)
                ReturnToShip();
            if (hub) AutoSave();
        }

        /// <summary>Ship: save the carry and the stash shortly after they change.</summary>
        void AutoSave()
        {
            var inv = player ? player.inventory : null;
            if (!inv) return;
            int c = inv.Backpack.Version * 31 + inv.WeaponSlots.Version, st = Profile.Stash.Version;
            if (savedCarry < 0) { savedCarry = c; savedStash = st; }
            if (c != savedCarry || st != savedStash) { savedCarry = c; savedStash = st; saveAt = Time.unscaledTime + 0.5f; }
            if (saveAt > 0f && Time.unscaledTime >= saveAt) { saveAt = -1f; Profile.SaveCarry(inv); }
        }

        void OnDestroy()
        {
            if (hub && player && player.inventory) Profile.SaveCarry(player.inventory);
        }

        // ------------------------------------------------------------------ scene flow

        static bool CanLoad(string scene) => Application.CanStreamedLevelBeLoaded(scene);

        /// <summary>Ship -> KEPLER-7: the carry is saved, the screen fades out, a raid loads (you step off the ramp).</summary>
        public void Deploy(float delay = 0f)
        {
            if (Busy) return;
            Busy = true;
            if (player && player.inventory) { player.inventory.ClosePanel(); Profile.SaveCarry(player.inventory); }
            StartCoroutine(LoadAfter(CanLoad(RaidScene) ? RaidScene : null, delay));
        }

        /// <summary>Raid over (extracted or KIA, already committed): back inside the ship.</summary>
        public void ReturnToShip()
        {
            if (Busy) return;
            Busy = true;
            GameFeel.Paused = false;
            Time.timeScale = 1f;
            StartCoroutine(LoadAfter(CanLoad(ShipScene) ? ShipScene : null, 0f));
        }

        /// <summary>Pause menu: give up the raid. Counts as KIA (everything carried is lost).</summary>
        public void AbandonRaid()
        {
            if (hub) return;
            Profile.CommitDeath();
            ReturnToShip();
        }

        System.Collections.IEnumerator LoadAfter(string scene, float delay)
        {
            float t = 0f;
            while (t < delay) { t += Time.unscaledDeltaTime; yield return null; }
            ScreenFade.Out(0.6f);
            t = 0f;
            while (t < 0.65f) { t += Time.unscaledDeltaTime; yield return null; }
            GameFeel.Paused = false;
            Time.timeScale = 1f;
            if (scene != null) SceneManager.LoadScene(scene);
            else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void OnAlienKilled(Alien alien)
        {
            Kills++;
            Score += alien.scoreValue;
            if (alien.Camp && alien.Camp.Alive == 0 && Hud.I)
                Hud.I.Banner($"{alien.Camp.campName} CLEARED", "The area is safe to loot.");
            if (!clearedAnnounced && Alien.All.Count == 0)
            {
                clearedAnnounced = true;
                if (Hud.I) Hud.I.Banner("MAP CLEARED", "Every xeno on the moon is dead. Grab the loot.");
                ProcAudio.Play("clear", 0.6f, 0f);
            }
        }

        public void Restart()
        {
            GameFeel.Paused = false;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>Called by the extraction zone when the countdown completes: the raid ends with the loot secured.</summary>
        public void OnExtracted()
        {
            if (IsExtracted || IsGameOver) return;
            IsExtracted = true;
            Profile.CommitExtraction(player ? player.inventory : null);
            gameOverTime = Time.unscaledTime;
            ProcAudio.Play("clear", 0.7f, 0f);
            if (RaidUI.I) RaidUI.I.ShowSummary(true);
        }

        public void OnPlayerDied()
        {
            if (IsGameOver) return;
            IsGameOver = true;
            if (!hub) Profile.CommitDeath();
            gameOverTime = Time.unscaledTime;
            if (RaidUI.I) RaidUI.I.ShowSummary(false);
            else if (Hud.I) Hud.I.ShowGameOver(Kills, Score);
        }
    }
}
