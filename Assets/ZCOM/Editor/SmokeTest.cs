using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Zcom.EditorTools
{
    /// <summary>
    /// Dev tool: enters Play Mode, lets an autopilot fight a few waves (move, shoot, dash, grenades),
    /// captures screenshots and logs runtime errors, then exits. Usable from batch mode:
    ///   Unity -batchmode -projectPath . -executeMethod Zcom.EditorTools.SmokeTest.Run -zcomOut /some/dir
    /// </summary>
    [InitializeOnLoad]
    public static class SmokeTest
    {
        const string KeyActive = "zcom_smoke_active";
        const string KeyOut = "zcom_smoke_out";
        const string KeyErrors = "zcom_smoke_errors";
        const string KeyDuration = "zcom_smoke_duration";
        const string KeyPassive = "zcom_smoke_passive";
        const string KeyShowcase = "zcom_smoke_showcase";
        const string KeyTour = "zcom_smoke_tour";
        const string KeyUi = "zcom_smoke_ui";   // "A" = tac-link, "B" = field kit, "" = the saved preference
        const string KeyVideo = "zcom_smoke_video";
        const string KeyShip = "zcom_smoke_ship";     // -zcomShip: start aboard the ship, use the stash, deploy, check the carry
        static bool ShipMode => SessionState.GetBool(KeyShip, false);
        static string TestProfilePath => Path.Combine(SessionState.GetString(KeyOut, "."), "profile.json");   // -zcomVideo: record every frame at 30 fps (FrameRecorder) instead of screenshots
        const int VideoFps = 30;
        static int videoStartFrame = -1;
        static bool Video => SessionState.GetBool(KeyVideo, false);
        /// <summary>Seconds since play started: real time normally, frame-locked game time when recording a video.</summary>
        static float Clock() => Video ? (videoStartFrame < 0 ? 0f : (Time.frameCount - videoStartFrame) / (float)VideoFps) : Time.realtimeSinceStartup - playStart;
        static int tourIndex;
        static float lineupShotAt = -1f, runShotAt = -1f;
        static int actStage = -1; static float actAt;
        static int corpseSeen, corpseShots; static float corpseShotA = -1f, corpseShotB = -1f;
        static float showStart = -1f; static int showAmmoSeen = -1; static float showShotAt = -1f; static int showDone = -1;
        static bool sawGameOver, sawRestart;
        static float gameOverSeenAt = -1f;

        static float playStart = -1f;
        static float lastShot;
        static int shotIndex;
        static float nextDash, grenadeHoldUntil, nextGrenade;
        static bool grenadeWasLive;
        static float captureBoomAt = -1f;
        static bool aimShotPending;
        static float aimShotAt;
        static int aimShots;
        static RenderTexture rt;
        static float nextInteract, nextCycle = 8f;
        static bool invShotDone;
        static readonly System.Collections.Generic.HashSet<WeaponKind> showcased = new System.Collections.Generic.HashSet<WeaponKind>();
        static float weaponFiringFor;
        static WeaponKind lastFiringKind;
        static float containerSeenAt = -1f;
        static bool tookAll, containerShot, uiScriptDone;
        static int uiStep = -1;
        static float uiStepT;
        static bool uiStepFirst;
        static int dragSrc = -1, dragDst = -1;

        static int WeaponCount(Inventory inv)
        {
            int n = 0;
            for (int i = 0; i < inv.WeaponSlots.Size; i++) if (!inv.WeaponSlots[i].IsEmpty) n++;
            return n;
        }

        static int FindBackpack(Inventory inv, System.Func<ItemStack, bool> pred)
        {
            for (int i = 0; i < inv.Backpack.Size; i++) if (pred(inv.Backpack[i])) return i;
            return -1;
        }

        /// <summary>Drives the mouse through the inventory window like a player would.</summary>
        static void RunUiScript(ref PlayerInputFrame f, Inventory inv)
        {
            var ui = InventoryUI.I;
            float dt = Time.unscaledDeltaTime;
            uiStepT += dt;
            bool first = uiStepFirst;
            uiStepFirst = false;
            f.move = Vector2.zero;
            void Next() { uiStep++; uiStepT = 0f; uiStepFirst = true; }
            Vector2 Slot(ItemContainer c, int i) => ui ? ui.SlotScreenPos(c, i) : Vector2.zero;

            switch (uiStep)
            {
                case 0: // open
                    if (first) f.inventory = true;
                    if (uiStepT > 0.5f) Next();
                    break;
                case 1: // press on a resource stack (shift = split)
                    dragSrc = FindBackpack(inv, s => !s.IsEmpty && !Items.IsWeapon(s.id) && s.count > 1);
                    if (dragSrc < 0) dragSrc = FindBackpack(inv, s => !s.IsEmpty);
                    dragDst = FindBackpack(inv, s => s.IsEmpty);
                    if (dragSrc < 0 || dragDst < 0) { uiStep = 99; break; }
                    f.pointer = Slot(inv.Backpack, dragSrc);
                    f.click = true;
                    f.shift = true;
                    Next();
                    break;
                case 2: // drag across to an empty slot
                    {
                        float k = Mathf.Clamp01(uiStepT / 0.5f);
                        f.pointer = Vector2.Lerp(Slot(inv.Backpack, dragSrc), Slot(inv.Backpack, dragDst), k);
                        f.fire = true; // left button held
                        if (k > 0.6f && !invShotDone) { invShotDone = true; Capture("inventory_split_drag"); }
                        if (k >= 1f) Next();
                        break;
                    }
                case 3: // release: split half into the empty slot
                    f.pointer = Slot(inv.Backpack, dragDst);
                    if (first) f.clickUp = true;
                    if (uiStepT > 0.3f) Next();
                    break;
                case 4: // RMB loadout slot 1 -> backpack
                    f.pointer = Slot(inv.WeaponSlots, 1);
                    if (first) f.rightClick = true;
                    if (uiStepT > 0.4f) Next();
                    break;
                case 5: // hover it to show the tooltip, screenshot, then RMB it back to the loadout
                    {
                        int w = FindBackpack(inv, s => Items.IsWeapon(s.id));
                        if (w >= 0) f.pointer = Slot(inv.Backpack, w);
                        if (uiStepT > 0.35f && first == false && uiStepT - Time.unscaledDeltaTime <= 0.35f) Capture("inventory_tooltip");
                        if (uiStepT > 0.5f) { if (w >= 0) f.rightClick = true; Next(); }
                        break;
                    }
                case 6: // drag the split stack out of the window to drop it on the deck
                    f.pointer = Slot(inv.Backpack, dragDst);
                    if (first) f.click = true;
                    if (uiStepT > 0.05f) Next();
                    break;
                case 7:
                    {
                        float k = Mathf.Clamp01(uiStepT / 0.5f);
                        f.pointer = Vector2.Lerp(Slot(inv.Backpack, dragDst), ui ? ui.OutsideScreenPos() : Vector2.zero, k);
                        f.fire = true;
                        if (k > 0.8f && uiStepT - Time.unscaledDeltaTime <= 0.4f) Capture("inventory_drop_drag");
                        if (k >= 1f) Next();
                        break;
                    }
                case 8:
                    f.pointer = ui ? ui.OutsideScreenPos() : Vector2.zero;
                    if (first) f.clickUp = true;
                    if (uiStepT > 0.4f) Next();
                    break;
                default: // close
                    f.inventory = inv && inv.PanelOpen;
                    uiStep = -1;
                    break;
            }
        }
        static int lootShots;

        static SmokeTest()
        {
            if (!SessionState.GetBool(KeyActive, false)) return;
            Profile.PathOverride = TestProfilePath;      // never touch the player's own profile
            InventoryUI.FocusBlur = false;      // captures render to a texture, where the blur would also hit the UI
            string ui = SessionState.GetString(KeyUi, "");
            if (ui == "A") UiStyle.Override = UiDesign.TacLink;
            else if (ui == "B") UiStyle.Override = UiDesign.FieldKit;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        [MenuItem("ZCOM/Dev/Run Smoke Test (autopilot)", priority = 100)]
        public static void RunMenu()
        {
            SessionState.SetBool(KeyPassive, false);
            SessionState.SetBool(KeyShowcase, false);
            SessionState.SetBool(KeyTour, false);
            SessionState.SetBool(KeyShip, false);
            Start(Path.GetFullPath("Logs/ZcomSmoke"), 30f);
        }

        public static void Run()
        {
            string outDir = Path.GetFullPath("Logs/ZcomSmoke");
            float duration = 55f;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-zcomOut") outDir = args[i + 1];
                if (args[i] == "-zcomDuration") float.TryParse(args[i + 1], out duration);
                if (args[i] == "-zcomUi") SessionState.SetString(KeyUi, args[i + 1]);
            }
            SessionState.SetBool(KeyPassive, System.Array.IndexOf(args, "-zcomPassive") >= 0);
            SessionState.SetBool(KeyShowcase, System.Array.IndexOf(args, "-zcomShowcase") >= 0);
            SessionState.SetBool(KeyTour, System.Array.IndexOf(args, "-zcomTour") >= 0);
            SessionState.SetBool(KeyVideo, System.Array.IndexOf(args, "-zcomVideo") >= 0);
            SessionState.SetBool(KeyShip, System.Array.IndexOf(args, "-zcomShip") >= 0);
            Start(outDir, duration);
        }

        static void Start(string outDir, float duration)
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "errors.txt"), "");
            SessionState.SetBool(KeyActive, true);
            SessionState.SetString(KeyOut, outDir);
            SessionState.SetInt(KeyErrors, 0);
            SessionState.SetFloat(KeyDuration, duration);
            if (File.Exists(TestProfilePath)) File.Delete(TestProfilePath);     // every run starts with a fresh operative
            if (!File.Exists(SliceBuilder.ScenePath)) SliceBuilder.Build();
            if (ShipMode && !File.Exists(ShipSceneBuilder.ScenePath)) ShipSceneBuilder.Build();
            EditorSceneManager.OpenScene(ShipMode ? ShipSceneBuilder.ScenePath : SliceBuilder.ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.EnterPlaymode();
        }

        static bool nanReported;
        static void OnLog(string msg, string stack, LogType type)
        {
            if (!nanReported && msg.Contains("invalid MinMaxAABB"))
            {
                nanReported = true;
                bool Bad(Vector3 v) => float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y);
                foreach (var rt in Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None))
                    if (Bad(rt.localPosition) || Bad(rt.localScale) || Bad(rt.sizeDelta) || Bad(rt.localEulerAngles))
                    {
                        string path = rt.name; var t = rt.parent; while (t) { path = t.name + "/" + path; t = t.parent; }
                        Debug.Log($"[ZCOM NAN] {path} pos={rt.localPosition} scale={rt.localScale} size={rt.sizeDelta}");
                    }
            }
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            SessionState.SetInt(KeyErrors, SessionState.GetInt(KeyErrors, 0) + 1);
            string dir = SessionState.GetString(KeyOut, "");
            if (dir != "") File.AppendAllText(Path.Combine(dir, "errors.txt"), $"[{type}] {msg}\n{stack}\n");
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (playStart < 0f)
            {
                playStart = Time.realtimeSinceStartup;
                Setup();
            }
            float t = Clock();
            float duration = SessionState.GetFloat(KeyDuration, 32f);
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (sceneName != lastScene) { if (lastScene != null) Setup(); lastScene = sceneName; }
            if (ShipMode)
            {
                ShipTick(t);
                if (t >= SessionState.GetFloat(KeyDuration, 32f)) Finish(t);
                return;
            }

            // Opening shot, then combat shots whenever enough aliens are on screen.
            if (shotIndex == 0 && t >= 1.5f) { Capture("t01_start"); CaptureOverview(); shotIndex++; lastShot = t; }
            else if (shotIndex > 0 && shotIndex < 12 && t - lastShot > 3.5f && (GameManager.I ? GameManager.I.AlertedHostiles : 0) >= 2)
            {
                Capture($"t{t:00}_combat");
                shotIndex++;
                lastShot = t;
            }
            if (aimShotPending && t >= aimShotAt) { aimShotPending = false; Capture($"t{t:00}_grenade_aim"); }

            // The first few carcasses: one shot mid-tumble, one at rest.
            AlienCorpse.LogSettle = true;
            if (AlienCorpse.Spawned > corpseSeen)
            {
                corpseSeen = AlienCorpse.Spawned;
                if (corpseShots < 3 && corpseShotA < 0f && corpseShotB < 0f) { corpseShots++; corpseShotA = t + 0.3f; corpseShotB = t + 2.4f; }
            }
            if (corpseShotA > 0f && t >= corpseShotA) { corpseShotA = -1f; Capture($"corpse{corpseShots}_tumble"); }
            if (corpseShotB > 0f && t >= corpseShotB) { corpseShotB = -1f; Capture($"corpse{corpseShots}_rest"); }

            if (SessionState.GetBool(KeyPassive, false) && GameManager.I)
            {
                if (GameManager.I.IsGameOver && !sawGameOver) { sawGameOver = true; gameOverSeenAt = t; }
                if (sawGameOver && gameOverSeenAt > 0f && t - gameOverSeenAt > 0.8f) { gameOverSeenAt = -1f; Capture("gameover"); }
                if (sawGameOver && !sawRestart && !GameManager.I.IsGameOver && gameOverSeenAt < 0f)
                {
                    sawRestart = true;
                    Capture("restarted");
                    SessionState.SetFloat(KeyDuration, t + 3f);
                }
            }


            if (SessionState.GetBool(KeyShowcase, false)) ShowcaseTick(t);
            if (SessionState.GetBool(KeyTour, false)) TourTick(t);

            // One showcase screenshot per weapon while it is firing at something.
            var sg = PlayerController.I ? PlayerController.I.gun : null;
            if (sg && sg.Current != null && Alien.All.Count >= 1 && PlayerInputSource.Current.fire && !sg.IsReloading && !sg.IsSwapping)
            {
                var k = sg.Current.kind;
                weaponFiringFor = k == lastFiringKind ? weaponFiringFor + Time.unscaledDeltaTime : 0f;
                lastFiringKind = k;
                if (weaponFiringFor > 0.4f && !showcased.Contains(k)) { showcased.Add(k); Capture($"weapon_{k}"); }
            }

            if (captureBoomAt > 0f && t >= captureBoomAt)
            {
                captureBoomAt = -1f;
                Capture($"boom_{t:00.0}");
            }

            if (t >= duration) Finish(t);
        }

        static string lastScene;

        static void Finish(float t)
        {
            {
                var gm = GameManager.I;
                string summary = $"duration={t:0.0}s areasVisited={visitedZones.Count} kills={(gm ? gm.Kills : -1)} " +
                                 $"aliveAliens={Alien.All.Count} playerHP={(PlayerController.I ? PlayerController.I.health.Health : -1)} " +
                                 $"errors={SessionState.GetInt(KeyErrors, 0)} recordedSfx={ProcAudio.RecordedClips} corpses={AlienCorpse.Spawned} " + LootSummary() +
                                 (ShipMode ? " " + shipReport : "");
                File.WriteAllText(Path.Combine(SessionState.GetString(KeyOut, "."), "summary.txt"), summary);
                Debug.Log("[ZCOM SMOKE] " + summary);
                int errors = SessionState.GetInt(KeyErrors, 0);
                SessionState.SetBool(KeyActive, false);
                PlayerInputSource.Override = null;
                AudioListener.volume = 1f;
                if (!Application.isBatchMode) EditorUtility.audioMasterMute = false;
                EditorApplication.update -= Tick;
                if (Application.isBatchMode) EditorApplication.Exit(errors > 0 ? 1 : 0);
                else EditorApplication.ExitPlaymode();
            }
        }


        // ------------------------------------------------------------------ ship (-zcomShip): the dropship loop
        // start aboard -> stash loot -> walk down the ramp (raid starts) -> board, hold the cargo bay (extract)
        // -> next raid starts aboard with the carry kept -> walk out -> die -> next raid: carry lost, stash kept
        static int shipStage;
        static float shipStageT;
        static bool shipStageFirst = true;
        static string shipReport = "ship=incomplete";
        static int shipRouteK;

        static void ShipNext()
        {
            var p = PlayerController.I;
            if (p && Dropship.I) Debug.Log($"[ZCOM SMOKE] ship stage {shipStage} -> {shipStage + 1} at {shipStageT:0.0}s, operative at ship-local {Dropship.I.transform.InverseTransformPoint(p.transform.position)} preRaid={(GameManager.I ? GameManager.I.PreRaid : false)}");
            shipStage++; shipStageT = 0f; shipStageFirst = true;
        }

        static Vector3 ShipPoint(float x, float y, float z) => Dropship.I ? Dropship.I.transform.TransformPoint(new Vector3(x, y, z)) : Vector3.zero;

        static void ShipShots(string prefix)
        {
            CaptureFrom(prefix + "_hero", ShipPoint(-15f, 8f, 16f), ShipPoint(0f, 2.6f, 0f));
            CaptureFrom(prefix + "_rear", ShipPoint(10f, 6.5f, -24f), ShipPoint(0f, 2f, -8f));
            CaptureFrom(prefix + "_side", ShipPoint(-20f, 3.5f, -1f), ShipPoint(0f, 2.8f, 0f));
            CaptureFrom(prefix + "_top", ShipPoint(-12f, 26f, -12f), ShipPoint(0f, 2f, -1f));
        }

        static void ShipTick(float t)
        {
            var p = PlayerController.I;
            var inv = p ? p.inventory : null;
            var gm = GameManager.I;
            if (shipStage == 0 && t >= 1.2f && inv && gm)
            {
                if (!gm.hub) Debug.LogError("[ZCOM SMOKE] the game should start inside the ship");
                inv.AddItem(new ItemStack(ItemId.Gold, 12));
                inv.AddItem(new ItemStack(ItemId.Xenite, 4));
                inv.AddItem(new ItemStack(ItemId.Stone, 25));
                inv.AddItem(new ItemStack(ItemId.NovaScatter, 1));
                Capture("ship_inside");
                CaptureFrom("ship_inside_wide", ShipPoint(-16f, 26f, -22f), ShipPoint(0f, 1.7f, 1f));
                CaptureFrom("ship_stash_close", ShipPoint(1.0f, 4.0f, 7.2f), ShipPoint(4.95f, 3.0f, 8.5f));
                CaptureFrom("ship_hold_close", ShipPoint(-2f, 7f, -1f), ShipPoint(0f, 2f, -10f));
                CaptureFrom("ship_flightdeck", ShipPoint(-2.5f, 6.5f, 10.5f), ShipPoint(0f, 2f, 15.5f));
                CaptureFrom("ship_midbay", ShipPoint(-6f, 9f, -5f), ShipPoint(0f, 1.7f, 0.5f));
                ShipNext();
            }
            if (shipStage == 6 && gm && !gm.hub && shipStageT > 1.5f && shipStageT - Time.unscaledDeltaTime <= 1.5f && inv)
            {
                Capture("raid_start");
                ShipShots("ship");
                bool carried = inv.Backpack.CountOf(ItemId.Stone) == 25 && inv.HasWeapon(WeaponKind.NovaScatter) && inv.Backpack.CountOf(ItemId.Gold) == 0;
                if (!Profile.InRaid || !carried) Debug.LogError($"[ZCOM SMOKE] deploy check failed: inRaid={Profile.InRaid} carried={carried}");
            }
            if (shipStage == 7 && shipStageT > 3f && shipStageT - Time.unscaledDeltaTime <= 3f) Capture("ship_boarding");
            if (shipStage == 8 && gm && gm.IsExtracted && shipStageT > 1.5f && shipStageT - Time.unscaledDeltaTime <= 1.5f) Capture("ship_extracted");
            if (shipStage == 9 && gm && gm.hub && shipStageT > 1.5f && inv)
            {
                int stone = inv.Backpack.CountOf(ItemId.Stone), gold = inv.Backpack.CountOf(ItemId.Gold);
                bool nova = inv.HasWeapon(WeaponKind.NovaScatter);
                int stashGold = Profile.Stash.CountOf(ItemId.Gold), stashX = Profile.Stash.CountOf(ItemId.Xenite);
                bool ok = stone == 25 && gold == 0 && nova && stashGold == 12 && stashX == 4 && !Profile.InRaid && Profile.Extractions == 1;
                shipReport = $"extract={(ok ? "OK" : "FAIL")} keptStone={stone} keptNova={nova} stashGold={stashGold} stashXenite={stashX} extractions={Profile.Extractions}";
                if (!ok) Debug.LogError("[ZCOM SMOKE] extraction flow check failed: " + shipReport);
                Capture("ship_back_inside");
                ShipNext();
            }
            if (shipStage == 11 && gm && !gm.hub && !gm.IsGameOver && p && p.health.IsAlive && shipStageT > 1.5f)
            {
                for (int i = 0; i < 6 && p.health.IsAlive; i++)
                    p.health.TakeDamage(new DamageInfo { amount = 400f, point = p.transform.position, direction = Vector3.forward });
            }
            if (shipStage == 11 && gm && gm.IsGameOver && shipStageT > 3.5f && shipStageT - Time.unscaledDeltaTime <= 3.5f) Capture("raid_kia");
            if (shipStage == 12 && gm && gm.hub && shipStageT > 1.5f && inv)
            {
                bool lost = inv.Backpack.IsEmpty && inv.WeaponSlots.CountOf(ItemId.NovaScatter) == 0 && inv.WeaponSlots.CountOf(ItemId.IonCarbine) == 1;
                bool kept = Profile.Stash.CountOf(ItemId.Gold) == 12 && Profile.Stash.CountOf(ItemId.Xenite) == 4 && !Profile.InRaid;
                shipReport += $" death={(lost && kept ? "OK" : "FAIL")} carryLost={lost} stashKept={kept} deaths={Profile.Deaths}";
                if (!(lost && kept)) Debug.LogError("[ZCOM SMOKE] death flow check failed: " + shipReport);
                SessionState.SetFloat(KeyDuration, t + 0.5f);
                ShipNext();
            }
        }

        static PlayerInputFrame ShipInput(PlayerInputFrame f)
        {
            var p = PlayerController.I;
            var cam = CameraRig.I ? CameraRig.I.Cam : null;
            var gm = GameManager.I;
            if (!p || !cam || !gm) return f;
            var inv = p.inventory;
            float dt = Time.unscaledDeltaTime;
            shipStageT += dt;
            bool first = shipStageFirst;
            shipStageFirst = false;
            Vector3 fwd = cam.transform.forward; fwd.y = 0; fwd.Normalize();
            Vector3 right = cam.transform.right; right.y = 0; right.Normalize();
            Vector2 MoveTo(Vector3 target)
            {
                Vector3 d = target - p.transform.position; d.y = 0f;
                if (d.magnitude < 0.35f) return Vector2.zero;
                d.Normalize();
                return new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, fwd));
            }
            bool Walk(params Vector3[] route)       // waypoint by waypoint (never back); true at the end
            {
                if (first) shipRouteK = 0;
                while (shipRouteK < route.Length - 1)
                {
                    Vector3 d = route[shipRouteK] - p.transform.position; d.y = 0f;
                    if (d.magnitude > 0.6f) break;
                    shipRouteK++;
                }
                f.move = MoveTo(route[shipRouteK]);
                return shipRouteK == route.Length - 1 && f.move == Vector2.zero;
            }
            f.pointer = new Vector2(Screen.width * 0.6f, Screen.height * 0.6f);
            var stash = Object.FindFirstObjectByType<StashLocker>();
            var ui = InventoryUI.I;
            Vector3 door = Dropship.I ? Dropship.I.DoorWorld : p.transform.position;
            bool DoorFocus() => p.GetComponent<PlayerInteractor>().Focus is ShipDoor;
            switch (shipStage)
            {
                case 1: // walk up to the stash
                    {
                        Vector3 spot = stash ? stash.PromptPosition : p.transform.position; spot.y = 0f;
                        if (Walk(ShipPoint(0f, 0f, -4.5f), ShipPoint(0f, 0f, 4f), ShipPoint(1.5f, 0f, 6.5f), spot) || shipStageT > 15f) ShipNext();
                    }
                    break;
                case 2: // open it
                    if (first && p.GetComponent<PlayerInteractor>().Focus is StashLocker) f.interact = true;
                    else if (shipStageT > 0.2f && shipStageT < 0.4f && !(inv.PanelOpen && inv.OpenContainer == Profile.Stash)) f.interact = true;
                    if (shipStageT > 1.4f) { Capture("ship_stash_open"); ShipNext(); }
                    break;
                case 3: // RMB gold, then xenite, into the stash
                    {
                        int gi = FindBackpack(inv, s => s.id == ItemId.Gold);
                        int xi = FindBackpack(inv, s => s.id == ItemId.Xenite);
                        if (shipStageT < 0.5f) { if (gi >= 0 && ui) f.pointer = ui.SlotScreenPos(inv.Backpack, gi); if (first) f.rightClick = true; }
                        else if (shipStageT < 1.0f) { if (xi >= 0 && ui) f.pointer = ui.SlotScreenPos(inv.Backpack, xi); if (shipStageT - dt < 0.5f) f.rightClick = true; }
                        if (shipStageT > 1.6f) { Capture("ship_stash_stored"); ShipNext(); }
                    }
                    break;
                case 4: // close
                    if (first) f.cancel = true;
                    if (shipStageT > 0.8f) ShipNext();
                    break;
                case 5: // to the rear door, E: out onto KEPLER-7
                case 10:
                    if (gm.hub)
                    {
                        bool there = shipStage == 5 ? Walk(ShipPoint(1.5f, 0f, 6.5f), ShipPoint(0f, 0f, 4f), ShipPoint(0f, 0f, -4.5f), door) : Walk(door);
                        if ((there || DoorFocus()) && Mathf.Repeat(shipStageT, 0.5f) < dt) f.interact = DoorFocus();
                        if (DoorFocus() && shipStageT > 0.5f && Mathf.Repeat(shipStageT, 1f) < dt) Capture(shipStage == 5 ? "ship_door" : "ship_door2");
                    }
                    else ShipNext();
                    if (shipStageT > 25f) { Debug.LogError("[ZCOM SMOKE] could not leave the ship"); SessionState.SetFloat(KeyDuration, 0f); }
                    break;
                case 6: // outside: walk away from the ship, then back up the ramp to its door
                    if (shipStageT > 2.5f && Walk(ShipPoint(0f, 0f, -27f), ShipPoint(0f, 0f, -16f), door)) ShipNext();
                    else if (shipStageT <= 2.5f) shipRouteK = 0;
                    if (shipStageT > 30f) ShipNext();
                    break;
                case 7: // hold the door until extracted
                    f.move = MoveTo(door);
                    if (gm.IsExtracted) ShipNext();
                    if (shipStageT > 20f) { Debug.LogError("[ZCOM SMOKE] boarding at the door did not extract"); ShipNext(); }
                    break;
                case 8: // summary -> R: back inside the ship
                    f.restart = gm.IsExtracted && shipStageT > 2.5f && shipStageT < 2.7f;
                    if (gm.hub) ShipNext();
                    if (shipStageT > 15f) { Debug.LogError("[ZCOM SMOKE] did not return to the ship after extracting"); ShipNext(); }
                    break;
                case 11: // (killed by ShipTick) KIA screen -> R
                    f.restart = gm.IsGameOver && shipStageT > 4.5f && shipStageT < 4.7f;
                    if (gm.hub) ShipNext();
                    if (shipStageT > 20f) { Debug.LogError("[ZCOM SMOKE] did not return to the ship after dying"); SessionState.SetFloat(KeyDuration, 0f); }
                    break;
            }
            return f;
        }

        // ------------------------------------------------------------------ area tour (-zcomTour): teleport + screenshot each area

        static readonly (string name, Vector3 pos)[] TourStops =
        {
            ("tour_landing", new Vector3(-30f, 0f, -21f)), ("tour_outpost", new Vector3(-9f, 0f, -6f)),
            ("tour_crash", new Vector3(18f, 0f, -17f)), ("tour_nest", new Vector3(21f, 0f, 12f)),
            ("tour_mining", new Vector3(-22f, 0f, 15f)), ("tour_edge", new Vector3(38f, 0f, 28f)),
            ("tour_aliens", new Vector3(-36f, 0f, 2f)), ("tour_aliens_walk", new Vector3(-36f, 0f, 2f)),
            ("tour_evac", new Vector3(3f, 0f, 29f)), ("tour_evac2", new Vector3(3f, 0f, 29f)), ("tour_evac3", new Vector3(3f, 0f, 29f)),
            ("tour_evac4", new Vector3(3f, 0f, 29f)), ("tour_summary", new Vector3(3f, 0f, 29f)),
        };
        static readonly System.Collections.Generic.List<Alien> tourAliens = new System.Collections.Generic.List<Alien>();

        static void TourTick(float t)
        {
            var p = PlayerController.I;
            if (!p) return;
            int stop = Mathf.FloorToInt((t - 2f) / 2.5f);
            if (stop < 0 || stop >= TourStops.Length) return;
            float local = (t - 2f) - stop * 2.5f;
            if (stop != tourIndex - 1 && local < 1f)
            {
                var cc = p.GetComponent<CharacterController>();
                cc.enabled = false;
                var pos = TourStops[stop].pos;
                pos.y = MapBuilder.Height(pos.x, pos.z) + 0.1f;
                bool evac = TourStops[stop].name.StartsWith("tour_evac") || TourStops[stop].name == "tour_summary";
                if (evac && Dropship.I) pos = Dropship.I.DoorWorld + Vector3.up * 0.1f;
                p.transform.position = pos;
                cc.enabled = true;
                foreach (var a in Alien.All) if (a) a.aggroRange = 0f;
                tourIndex = stop + 1;
                if (TourStops[stop].name == "tour_aliens")
                {
                    if (CameraRig.I) CameraRig.I.distance = 9f;
                    var types = new[] { AlienType.Skitter, AlienType.Spitter, AlienType.Brute };
                    for (int i = 0; i < 3; i++)
                    {
                        var cam = CameraRig.I ? CameraRig.I.Cam.transform : null;
                        Vector3 r = cam ? Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized : Vector3.right;
                        Vector3 f = cam ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward;
                        var at = pos + r * (-3.4f + i * 3.4f) + f * 2.2f;
                        at.y = MapBuilder.Height(at.x, at.z);
                        var al = AlienFactory.Create(types[i], at, Quaternion.LookRotation(-f), dormant: true);
                        al.aggroRange = 0f;
                        tourAliens.Add(al);
                    }
                }
                if (TourStops[stop].name == "tour_aliens_walk")
                    foreach (var al in tourAliens)
                        if (al && al.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var ag) && ag.isOnNavMesh) ag.SetDestination(al.transform.position + new Vector3(4f, 0f, 0f));
            }
            else if (stop == tourIndex - 1 && local > 2.0f && local - Time.unscaledDeltaTime <= 2.0f)
            {
                Capture(TourStops[stop].name);
                if (TourStops[stop].name == "tour_landing") CaptureProps();
            }
        }

        /// <summary>One close-up per environment prop (first instance of each), from the game camera's direction.</summary>
        static void CaptureProps()
        {
            var cam = CameraRig.I ? CameraRig.I.Cam : null;
            if (!cam) return;
            Vector3 view = cam.transform.forward;
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.InstanceID))
            {
                var root = mf.transform;
                while (root.parent && !root.name.StartsWith("Prop_")) root = root.parent;
                string n = root.name.Replace("(Clone)", "").Trim();
                if (!n.StartsWith("Prop_") || !seen.Add(n)) continue;
                var rs = root.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                float d = b.extents.magnitude * 3.2f;
                Vector3 dir = Vector3.Slerp(view, Vector3.ProjectOnPlane(view, Vector3.up).normalized, 0.35f).normalized;
                CaptureFrom("prop_" + n.Substring(5), b.center - dir * d, b.center);
            }
        }

        // ------------------------------------------------------------------ weapon showcase (-zcomShowcase)
        // Gives all weapons, then fires each one into open space for 2.5s and screenshots it mid-fire.

        static int ShowcaseWeapon(float t) => showStart < 0f ? -1 : Mathf.FloorToInt((t - showStart) / 2.5f);

        static void ShowcaseTick(float t)
        {
            var p = PlayerController.I;
            if (!p || !p.inventory) return;
            if (showStart < 0f && t > 1f)
            {
                p.inventory.AddItem(new ItemStack(ItemId.NovaScatter, 1));
                p.inventory.AddItem(new ItemStack(ItemId.HelixBeam, 1));
                showStart = t;
                // Close-up model lineup: one of each alien standing calmly in front of the operative.
                if (CameraRig.I) { CameraRig.I.distance = 9.5f; CameraRig.I.lookAhead = 0f; }
                Vector3 fwd = new Vector3(1f, 0f, 0.35f).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                var types = new[] { AlienType.Skitter, AlienType.Brute, AlienType.Spitter };
                for (int i = 0; i < 3; i++)
                {
                    Vector3 at = p.transform.position + fwd * 5.5f + side * ((i - 1) * 3.2f);
                    var a = AlienFactory.Create(types[i], at, Quaternion.LookRotation(-fwd), dormant: true);
                    a.aggroRange = 0f;
                    a.guardRadius = 0.1f;
                }
                lineupShotAt = t + 0.6f;
            }
            if (lineupShotAt > 0f && t >= lineupShotAt) { lineupShotAt = -1f; Capture("closeup_marshal_idle"); runShotAt = t + 0.9f; }
            if (runShotAt > 0f && t >= runShotAt) { runShotAt = -1f; Capture("closeup_marshal_run"); if (CameraRig.I) CameraRig.I.distance = 14f; }
            int w = ShowcaseWeapon(t);
            if (w < 0) return;
            if (w > 2) { ActingTick(t, p); return; }
            float local = (t - showStart) - w * 2.5f;
            var gun = p.gun;
            if (!gun || gun.Current == null) return;
            if (showDone == w) return;
            if (gun.Current.beam)
            {
                if (local > 1.4f) { showDone = w; Capture($"showcase_{gun.Current.kind}"); }
            }
            else
            {
                if (showAmmoSeen >= 0 && gun.Ammo < showAmmoSeen && local > 0.7f) showShotAt = t;
                showAmmoSeen = gun.Ammo;
                float wait = gun.Current.pellets > 1 ? 0.07f : 0.05f;
                if (showShotAt > 0f && t - showShotAt >= wait) { showDone = w; showShotAt = -1f; showAmmoSeen = -1; Capture($"showcase_{gun.Current.kind}"); }
            }
        }

        // After the weapons: the hero's own acting, one close-up per action (reload, grenade throw, dash, hit).
        static void ActNext(int stage, float t) { actStage = stage; actAt = t; }

        static void ActingTick(float t, PlayerController p)
        {
            var gun = p.gun;
            if (!gun) return;
            if (actStage < 0) { ActNext(0, t); if (CameraRig.I) CameraRig.I.distance = 6.5f; }
            float a = t - actAt;
            switch (actStage)
            {
                case 0: if (gun.IsReloading) ActNext(1, t); else if (a > 4f) ActNext(3, t); break;
                case 1:
                    if (gun.ReloadProgress > 0.49f) { Capture("hero_reload_belt"); ActNext(2, t); }
                    else if (!gun.IsReloading) ActNext(3, t);
                    break;
                case 2: if (gun.ReloadProgress > 0.71f || !gun.IsReloading) { Capture("hero_reload_slam"); ActNext(3, t); } break;
                case 3: if (!gun.IsReloading && a > 1f) ActNext(4, t); break;
                case 4: if (a > 0.55f) { Capture("hero_throw_windup"); ActNext(5, t); } break;      // grenade held
                case 5: if (a > 0.13f) { Capture("hero_throw_release"); ActNext(6, t); } break;     // released
                case 6: if (a > 1f) ActNext(7, t); break;
                case 7: if (a > 0.1f) { Capture("hero_dash"); ActNext(8, t); } break;               // dash pressed
                case 8: if (a > 0.9f) { p.HitReact(-p.AimDir); ActNext(9, t); } break;
                case 9: if (a > 0.1f) { Capture("hero_hit"); ActNext(30, t); } break;
                // Run cycle: jog towards the camera (stages 31-34 hold the stick), four phases of the stride.
                case 30:
                    if (a > 0.6f)
                    {
                        // Weapon grip close-ups from eye level (front-left, right side, front-right).
                        Vector3 f = p.visual ? p.visual.forward : p.transform.forward;
                        f.y = 0f; f.Normalize();
                        Vector3 r = Vector3.Cross(Vector3.up, f);
                        Vector3 chest = p.transform.position + Vector3.up * 1.15f;
                        CaptureFrom("grip_front", chest + (f * 2.4f - r * 0.9f) + Vector3.up * 0.25f, chest);
                        CaptureFrom("grip_side", chest + r * 2.6f + Vector3.up * 0.2f, chest);
                        CaptureFrom("grip_34", chest + (f * 1.8f + r * 1.8f) + Vector3.up * 0.6f, chest);
                        CaptureFrom("grip_top", chest + (f * 0.6f - r * 0.4f) + Vector3.up * 2.4f, chest);
                        ActNext(31, t);
                    }
                    break;
                case 31: if (a > 0.7f) { Capture("hero_run_a"); ActNext(32, t); } break;
                case 32: if (a > 0.12f) { Capture("hero_run_b"); ActNext(33, t); } break;
                case 33: if (a > 0.12f) { Capture("hero_run_c"); ActNext(34, t); } break;
                case 34: if (a > 0.12f) { Capture("hero_run_d"); ActNext(10, t); } break;
                case 10:
                    if (a > 0.6f)
                    {
                        // Ragdoll close-up: a fresh row of aliens in front of the camera, shot dead across the screen.
                        if (CameraRig.I) CameraRig.I.distance = 12f;
                        ScreenAxes(out Vector3 right, out Vector3 up);
                        var types = new[] { AlienType.Skitter, AlienType.Brute, AlienType.Spitter, AlienType.Skitter };
                        ragAliens.Clear();
                        for (int i = 0; i < types.Length; i++)
                        {
                            Vector3 at = p.transform.position + up * (0.4f + (i % 2) * 2.1f) + right * (-7.2f + i * 2.7f);
                            var al = AlienFactory.Create(types[i], at, Quaternion.LookRotation(-up), dormant: true);
                            al.aggroRange = 0f;
                            al.guardRadius = 0.1f;
                            ragAliens.Add(al);
                        }
                        ActNext(11, t);
                    }
                    break;
                case 11:
                    if (a > 0.5f)
                    {
                        Capture("ragdoll_0");
                        ScreenAxes(out Vector3 right, out Vector3 up);
                        foreach (var al in ragAliens)
                            if (al) al.TakeDamage(new DamageInfo { amount = 99999f, point = al.transform.position + Vector3.up * 0.5f, direction = right, knockback = 2f });
                        ActNext(12, t);
                    }
                    break;
                case 12: if (a > 0.22f) { Capture("ragdoll_a"); ActNext(13, t); } break;
                case 13: if (a > 0.4f) { Capture("ragdoll_b"); ActNext(14, t); } break;
                case 14: if (a > 0.9f) { Capture("ragdoll_c"); ActNext(15, t); } break;
                case 15: if (a > 2.4f) { Capture("ragdoll_d"); ActNext(16, t); } break;
                case 16:
                    if (a > 0.3f)
                    {
                        // The Marshal's own death, close up: no corpses in the way, no summary screen over it.
                        if (CameraRig.I) CameraRig.I.distance = 7.5f;
                        foreach (var c in Object.FindObjectsByType<AlienCorpse>(FindObjectsSortMode.None))
                            foreach (var r in c.GetComponentsInChildren<Renderer>()) r.enabled = false;
                        var hp = p.GetComponent<PlayerHealth>();
                        if (hp) hp.TakeDamage(new DamageInfo { amount = 99999f, point = p.transform.position + Vector3.up, direction = -p.AimDir, explosive = true });
                        ActNext(17, t);
                    }
                    break;
                case 17: if (a > 0.55f) { HideUi(); Capture("hero_death_a"); ActNext(18, t); } break;
                case 18: if (a > 0.4f) { HideUi(); Capture("hero_death_b"); ActNext(19, t); } break;
                case 19: if (a > 0.45f) { HideUi(); Capture("hero_death_c"); ActNext(20, t); } break;
                case 20: if (a > 1.8f) { HideUi(); Capture("hero_death_d"); ActNext(21, t); } break;
                default: SessionState.SetFloat(KeyDuration, Mathf.Min(SessionState.GetFloat(KeyDuration, 30f), t + 0.3f)); break;
            }
        }

        static readonly System.Collections.Generic.List<Alien> ragAliens = new System.Collections.Generic.List<Alien>();

        static void HideUi()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
        }

        // Ground directions that read as right / up on screen.
        static void ScreenAxes(out Vector3 right, out Vector3 up)
        {
            var cam = CameraRig.I ? CameraRig.I.Cam : Camera.main;
            right = cam ? cam.transform.right : Vector3.right; right.y = 0f; right.Normalize();
            up = Vector3.Cross(right, Vector3.up);
        }

        static PlayerInputFrame ShowcaseInput(PlayerInputFrame f, PlayerController p, Camera cam)
        {
            float t = Time.realtimeSinceStartup - playStart;
            int w = ShowcaseWeapon(t);
            Vector3 target = p.transform.position + new Vector3(1f, 0f, 0.35f).normalized * 14f;
            target.y = p.aimHeight;
            Vector3 sp = cam.WorldToScreenPoint(target);
            f.pointer = new Vector2(sp.x, sp.y);
            if (w > 2 && actStage >= 0)
            {
                float a = t - actAt;
                // Acting is shot from the front-left, so the off hand (which does the work) is in view.
                ScreenAxes(out Vector3 sr, out Vector3 su);
                Vector3 face = p.transform.position + (-su - sr * 0.55f).normalized * 6f;
                face.y = p.aimHeight;
                Vector3 fp = cam.WorldToScreenPoint(face);
                f.pointer = new Vector2(fp.x, fp.y);
                if (actStage == 0)
                {
                    if (a < 0.1f) f.slot = 0;
                    f.fire = a > 0.5f && a < 0.75f;     // spend a few rounds so there is something to reload
                    f.reload = a > 0.9f;
                }
                f.grenade = actStage == 4;
                if (actStage == 7) { f.move = new Vector2(1f, -0.35f); f.dash = a < 0.05f; }
                if (actStage >= 31 && actStage <= 34) f.move = new Vector2(-0.55f, -1f).normalized;
                return f;
            }
            if (w < 0 || w > 2) return f;
            float local = (t - showStart) - w * 2.5f;
            if (local < 0.1f) f.slot = w;
            if (w == 0 && local > 0.6f && local < 1.6f) f.move = new Vector2(-1f, 0.35f);
            f.fire = local > (w == 0 ? 1.7f : 0.6f) && local < 2.3f;
            return f;
        }

        static readonly System.Collections.Generic.HashSet<IInteractable> skipTargets = new System.Collections.Generic.HashSet<IInteractable>();
        static Vector3 stuckPos;
        static float stuckT;
        static readonly System.Collections.Generic.HashSet<string> visitedZones = new System.Collections.Generic.HashSet<string>();
        static readonly UnityEngine.AI.NavMeshPath navPath = new UnityEngine.AI.NavMeshPath();

        /// <summary>Closest area we haven't visited yet (then the nearest remaining alien camp, so fights happen).</summary>
        static MapZone NextZone(Vector3 pos)
        {
            MapZone best = null;
            float bd = float.MaxValue;
            foreach (var z in MapZone.All)
            {
                if (visitedZones.Contains(z.zoneName)) continue;
                float d = (z.transform.position - pos).sqrMagnitude;
                if (d < bd) { bd = d; best = z; }
            }
            if (best == null && Alien.All.Count > 0)
            {
                visitedZones.Clear(); // second lap: go back to camps that still have aliens
                foreach (var z in MapZone.All)
                {
                    bool hasAliens = false;
                    foreach (var a in Alien.All) if (a && (a.transform.position - z.transform.position).magnitude < z.radius) hasAliens = true;
                    if (!hasAliens) visitedZones.Add(z.zoneName);
                }
            }
            return best;
        }

        /// <summary>Next corner along the navmesh towards goal (so the autopilot walks around buildings and rocks).</summary>
        static Vector3 PathStep(Vector3 from, Vector3 goal)
        {
            if (!UnityEngine.AI.NavMesh.SamplePosition(from, out var a, 2f, UnityEngine.AI.NavMesh.AllAreas)) return goal;
            if (!UnityEngine.AI.NavMesh.SamplePosition(goal, out var b, 3f, UnityEngine.AI.NavMesh.AllAreas)) return goal;
            if (!UnityEngine.AI.NavMesh.CalculatePath(a.position, b.position, UnityEngine.AI.NavMesh.AllAreas, navPath) || navPath.corners.Length < 2) return goal;
            var c = navPath.corners;
            for (int i = 1; i < c.Length; i++)
                if ((new Vector3(c[i].x, from.y, c[i].z) - from).sqrMagnitude > 0.5f) return c[i];
            return goal;
        }

        /// <summary>Top-down orthographic shot of the whole map.</summary>
        static void CaptureOverview()
        {
            var go = new GameObject("OverviewCam");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = MapBuilder.HalfZ + 4f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 80f, 0f), Quaternion.Euler(90f, 0f, 0f));
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.farClipPlane = 200f;
            var rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(SessionState.GetString(KeyOut, "."), "map_overview.png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
        }

        static string LootSummary()
        {
            var inv = PlayerController.I ? PlayerController.I.inventory : null;
            if (!inv) return "inventory=none";
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < inv.WeaponSlots.Size; i++) names.Add(inv.WeaponSlots[i].IsEmpty ? "-" : inv.WeaponSlots[i].id.ToString());
            var w = string.Join(",", names);
            int used = 0;
            foreach (var st in inv.Backpack.slots) if (!st.IsEmpty) used++;
            return $"weapons=[{w}] equipped={inv.EquippedIndex} stone={inv.Count(ResourceType.Stone)} gold={inv.Count(ResourceType.Gold)} xenite={inv.Count(ResourceType.Xenite)} backpackSlotsUsed={used} groundItems={Object.FindObjectsByType<ItemPickup>(FindObjectsSortMode.None).Length}";
        }

        static void Setup()
        {
            PlayerInputSource.Override = Autopilot;
            // test runs are silent (the editor would otherwise play music and SFX through the speakers)
            EditorUtility.audioMasterMute = true;
            AudioListener.volume = 0f;
            var cam = CameraRig.I ? CameraRig.I.Cam : Camera.main;
            if (Application.isBatchMode && cam)
            {
                rt = Video ? new RenderTexture(1920, 1080, 24) : new RenderTexture(1600, 900, 24);
                cam.targetTexture = rt;
            }
            if (Video && rt)
            {
                Time.captureFramerate = VideoFps;
                videoStartFrame = Time.frameCount;
                FrameRecorder.Begin(rt, Path.Combine(SessionState.GetString(KeyOut, "."), "frames"));
            }
            if (Hud.I && cam)
            {
                Hud.I.Canvas.renderMode = RenderMode.ScreenSpaceCamera;
                Hud.I.Canvas.worldCamera = cam;
                Hud.I.Canvas.planeDistance = 2f;
            }
        }

        /// <summary>Renders the game camera from an arbitrary pose (close-ups), then lets the rig take it back.</summary>
        static void CaptureFrom(string name, Vector3 pos, Vector3 lookAt)
        {
            var cam = CameraRig.I ? CameraRig.I.Cam : null;
            if (!cam) return;
            var tr = cam.transform;
            Vector3 p0 = tr.position; Quaternion r0 = tr.rotation; float fov = cam.fieldOfView;
            tr.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos));
            cam.fieldOfView = 35f;
            Capture(name);
            tr.SetPositionAndRotation(p0, r0);
            cam.fieldOfView = fov;
        }

        static void Capture(string name)
        {
            if (Video) return;          // the recorder grabs every frame instead
            var cam = CameraRig.I ? CameraRig.I.Cam : null;
            if (!cam) return;
            var target = cam.targetTexture;
            bool temp = target == null;
            if (temp) { target = new RenderTexture(1600, 900, 24); cam.targetTexture = target; }
            // "_ui" twin without post-processing: in these texture captures the overlay UI goes through bloom / grain /
            // SMAA, which it does not on screen, so this is the honest view of the interface
            if (name.Contains("combat") || name.Contains("inventory") || name.Contains("crate") || name.Contains("start"))
            {
                var cd = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
                bool pp = cd.renderPostProcessing;
                var aa = cd.antialiasing;
                cd.renderPostProcessing = false;
                cd.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
                cam.Render();
                SaveTarget(target, name + "_ui");
                cd.renderPostProcessing = pp;
                cd.antialiasing = aa;
            }
            cam.Render();
            SaveTarget(target, name);
            if (temp) { cam.targetTexture = null; Object.DestroyImmediate(target); }
        }

        static void SaveTarget(RenderTexture target, string name)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = target;
            var tex = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(SessionState.GetString(KeyOut, "."), name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static PlayerInputFrame Autopilot()
        {
            var f = new PlayerInputFrame { slot = -1 };
            if (ShipMode) return ShipInput(f);
            if (SessionState.GetBool(KeyTour, false)) return f;
            if (SessionState.GetBool(KeyPassive, false))
            {
                f.restart = GameManager.I && GameManager.I.IsGameOver;
                return f;
            }
            var p = PlayerController.I;
            var cam = CameraRig.I ? CameraRig.I.Cam : null;
            if (!p || !cam) return f;
            if (SessionState.GetBool(KeyShowcase, false)) return ShowcaseInput(f, p, cam);
            float now = Time.time;
            Vector3 pos = p.transform.position;
            var inv = p.inventory;
            float tReal = Clock();
            Vector3 fwd = cam.transform.forward; fwd.y = 0; fwd.Normalize();
            Vector3 right = cam.transform.right; right.y = 0; right.Normalize();
            Vector2 MoveTo(Vector3 dir) { dir.y = 0f; if (dir.sqrMagnitude < 0.01f) return Vector2.zero; dir.Normalize(); return new Vector2(Vector3.Dot(dir, right), Vector3.Dot(dir, fwd)); }

            // Scripted mouse session in the inventory window (drag / split / quick move / drop).
            if (uiStep >= 0) { RunUiScript(ref f, inv); return f; }

            // A crate's window is open: look at it, take everything, close.
            if (inv && inv.PanelOpen && inv.OpenContainer != null)
            {
                if (containerSeenAt < 0f) containerSeenAt = tReal;
                if (tReal - containerSeenAt > 0.5f && lootShots < 4 && !containerShot) { containerShot = true; lootShots++; Capture($"t{tReal:00}_crate_window"); }
                if (tReal - containerSeenAt > 0.7f && !tookAll) { f.takeAll = true; tookAll = true; }
                if (tReal - containerSeenAt > 1.0f) { f.inventory = true; containerSeenAt = -1f; tookAll = false; containerShot = false; }
                return f;
            }
            if (inv && inv.PanelOpen) { f.inventory = true; return f; }

            // Threats = aliens that noticed us (or any alien very close).
            Alien threat = null;
            float best = float.MaxValue;
            Vector3 centroid = Vector3.zero;
            int near = 0;
            foreach (var a in Alien.All)
            {
                if (!a || !a.IsAlive) continue;
                float d = (a.transform.position - pos).sqrMagnitude;
                if (!a.IsAlerted && d > 8f * 8f) continue;
                if (d < best) { best = d; threat = a; }
                if (d < 14f * 14f) { centroid += a.transform.position; near++; }
            }

            bool grenadeLive = Object.FindAnyObjectByType<Grenade>() != null;
            if (grenadeWasLive && !grenadeLive) captureBoomAt = tReal + 0.03f;
            grenadeWasLive = grenadeLive;

            Vector3 aimWorld = pos + p.AimDir * 6f;
            if (threat)
            {
                // Fight: aim, shoot, grenade clusters, kite away from melee.
                if (now < grenadeHoldUntil && near > 0) { aimWorld = centroid / near; f.grenade = true; }
                else
                {
                    aimWorld = threat.transform.position;
                    f.fire = Mathf.Sqrt(best) < 22f;
                    if (near >= 3 && now > nextGrenade && p.grenades && p.grenades.Charges > 0)
                    {
                        grenadeHoldUntil = now + 0.5f;
                        nextGrenade = now + 5f;
                        if (aimShots++ < 2) { aimShotPending = true; aimShotAt = tReal + 0.3f; }
                    }
                }
                float dist = Mathf.Sqrt(best);
                Vector3 away = pos - threat.transform.position;
                Vector3 side = Vector3.Cross(Vector3.up, away.normalized) * (Mathf.Sin(now * 0.7f) > 0f ? 1f : -1f);
                bool los = !Physics.Linecast(p.Chest, threat.transform.position + Vector3.up * 1.1f, out var losHit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                           || losHit.collider.GetComponentInParent<Alien>() != null;
                if (dist > 14f || !los) f.move = MoveTo(PathStep(pos, threat.transform.position) - pos);
                else f.move = MoveTo(dist < 6f ? away.normalized * 2f + side : side);
                f.fire &= los;
                if (dist < 4f && now > nextDash) { f.dash = true; nextDash = now + 2.5f; }
                if (inv && WeaponCount(inv) > 1 && now > nextCycle) { f.cycle = 1; nextCycle = now + 6f; }
            }
            else
            {
                // Loot anything close, otherwise explore the next area.
                IInteractable loot = null;
                float lb = 14f * 14f;
                foreach (var it in Interactables.All)
                {
                    if (Interactables.IsGone(it) || !it.CanInteract || skipTargets.Contains(it) || it is StashLocker) continue;
                    float d = (it.PromptPosition - pos).sqrMagnitude;
                    if (d < lb) { lb = d; loot = it; }
                }
                Vector3 goal;
                if (loot != null)
                {
                    goal = loot.PromptPosition;
                    Vector3 to = goal - pos; to.y = 0f;
                    if (to.magnitude < 2.2f && now > nextInteract) { f.interact = true; nextInteract = now + 0.8f; }
                    if (to.magnitude < 1.5f) goal = pos;
                }
                else
                {
                    var zone = NextZone(pos);
                    goal = zone ? zone.transform.position : pos;
                    if (zone && (zone.transform.position - pos).magnitude < 4f) { visitedZones.Add(zone.zoneName); Capture($"t{tReal:00}_area_{zone.zoneName.Replace(' ', '_')}"); }
                }
                f.move = MoveTo(PathStep(pos, goal) - pos);
                if (goal != pos) aimWorld = pos + (goal - pos).normalized * 6f;

                // Stuck detector: if we haven't moved for a while, give up on this target.
                if (f.move.sqrMagnitude > 0.1f)
                {
                    if ((pos - stuckPos).magnitude > 0.6f) { stuckPos = pos; stuckT = 0f; }
                    stuckT += Time.deltaTime;
                    if (stuckT > 3f)
                    {
                        stuckT = 0f;
                        if (loot != null) skipTargets.Add(loot);
                        else { var z = NextZone(pos); if (z) visitedZones.Add(z.zoneName); }
                        f.dash = true;
                    }
                }

                // Once we own 2+ weapons and some resources, run the inventory mouse script once.
                if (inv && !uiScriptDone && WeaponCount(inv) >= 2 && inv.Count(ResourceType.Stone) > 0)
                {
                    uiStep = 0; uiStepT = 0f; uiStepFirst = true; uiScriptDone = true;
                }
            }

            aimWorld.y = p.aimHeight;
            Vector3 sp = cam.WorldToScreenPoint(aimWorld);
            f.pointer = new Vector2(sp.x, sp.y);
            return f;
        }
    }
}
