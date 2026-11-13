using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Zcom.EditorTools
{
    /// <summary>
    /// Generates the ship instance (the hub): ZCOM > Build Ship Scene. Only the KESTREL's interior (permanent cutaway)
    /// against darkness, lit by its own lamps; the operative in the cargo hold, systems in hub mode. The ship comes first
    /// in the build: the game starts aboard.
    /// </summary>
    public static class ShipSceneBuilder
    {
        public const string ScenePath = "Assets/ZCOM/Scenes/Ship.unity";
        const string GenDir = "Assets/ZCOM/GeneratedShip";

        [MenuItem("ZCOM/Build Ship Scene", priority = 1)]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        /// <summary>Batch: -executeMethod Zcom.EditorTools.ShipSceneBuilder.BuildBatch (builds the raid map too).</summary>
        public static void BuildBatch()
        {
            SliceBuilder.Build();
            Build();
            EditorApplication.Exit(0);
        }

        public static void Build()
        {
            MarshalAnimSetup.EnsureController();
            PropSetup.EnsureMaterials();
            PropSetup.EnsureShipMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.DeleteAsset(GenDir);
            SliceBuilder.EnsureFolder(GenDir + "/Materials");

            // an interior: no sun, no sky, no ground; the ship's own lamps light it and everything around is dark
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.16f, 0.18f, 0.23f);
            RenderSettings.ambientEquatorColor = new Color(0.1f, 0.105f, 0.125f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.035f);
            RenderSettings.fog = false;
            RenderSettings.sun = null;
            SliceBuilder.CreatePostFx(GenDir + "/ShipPostFX.asset");

            var world = new GameObject("ShipInstance").transform;
            var ship = Dropship.Build(world, Vector3.zero, -90f, true);
            Dropship.BuildZones(world, ship.transform);

            var player = PlayerFactory.Build(ship.SpawnWorld);
            player.transform.rotation = ship.transform.rotation;
            var cam = SliceBuilder.CreateCamera(player);
            cam.backgroundColor = new Color(0.004f, 0.005f, 0.008f);
            cam.GetComponent<CameraRig>().distance = 18f;
            var gm = SliceBuilder.CreateSystems(player);
            gm.hub = true;
            gm.mapName = "KESTREL";
            gm.mapSubtitle = "Inside your dropship. Stash loot in the crew bay; the rear door leads out to KEPLER-7.";

            SliceBuilder.PersistMaterials(GenDir);
            EditorSceneManager.SaveScene(scene, ScenePath);
            SliceBuilder.RegisterScenes();
            AssetDatabase.SaveAssets();
            Debug.Log($"[ZCOM] Ship scene built at {ScenePath}.");
        }
    }
}
