using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Zcom.EditorTools
{
    /// <summary>
    /// Opens the ship instance (or the raid map directly) and enters Play Mode.
    /// Menu: ZCOM > Play Vertical Slice, or from the command line:
    /// -executeMethod Zcom.EditorTools.QuickPlay.OpenAndPlay
    /// </summary>
    public static class QuickPlay
    {
        [MenuItem("ZCOM/Play From Ship", priority = 1)]
        public static void PlayShip() => Open(ShipSceneBuilder.ScenePath, ShipSceneBuilder.Build);

        [MenuItem("ZCOM/Play Vertical Slice", priority = 2)]
        public static void OpenAndPlay() => Open(SliceBuilder.ScenePath, SliceBuilder.Build);

        static void Open(string scenePath, System.Action build)
        {
            // Defer until the editor has finished loading its layout/windows.
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(scenePath)) build();
                if (EditorSceneManager.GetActiveScene().path != scenePath)
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                    EditorSceneManager.OpenScene(scenePath);
                }
                EditorApplication.EnterPlaymode();
            };
        }
    }
}
