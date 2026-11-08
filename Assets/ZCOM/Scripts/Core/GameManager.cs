using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Zcom
{
    /// <summary>
    /// Raid manager for the exploration map: bakes the navmesh, tracks kills / score, game over and redeploy.
    /// Aliens live in <see cref="AlienCamp"/>s placed around the map (no waves).
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        public NavMeshSurface navSurface;
        public PlayerController player;
        public string mapName = "KEPLER-7";
        [TextArea] public string mapSubtitle = "Explore the moon outpost. Loot what you can. Stay alive.";

        public int Kills { get; private set; }
        public int Score { get; private set; }
        public bool IsGameOver { get; private set; }
        public bool IsExtracted { get; private set; }
        public float RaidTime => Time.timeSinceLevelLoad;
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
        }

        void Start()
        {
            if (Hud.I) Hud.I.Banner(mapName, Extraction ? "Loot the moon, clear the camps, then reach the evac beacon to extract." : mapSubtitle);
        }

        void Update()
        {
            if ((IsGameOver || IsExtracted) && Time.unscaledTime - gameOverTime > 1.0f && PlayerInputSource.Current.restart)
                Restart();
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
            gameOverTime = Time.unscaledTime;
            ProcAudio.Play("clear", 0.7f, 0f);
            if (RaidUI.I) RaidUI.I.ShowSummary(true);
        }

        public void OnPlayerDied()
        {
            if (IsGameOver) return;
            IsGameOver = true;
            gameOverTime = Time.unscaledTime;
            if (RaidUI.I) RaidUI.I.ShowSummary(false);
            else if (Hud.I) Hud.I.ShowGameOver(Kills, Score);
        }
    }
}
