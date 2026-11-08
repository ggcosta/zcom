using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Zcom.EditorTools
{
    /// <summary>
    /// Opens the vertical slice and enters Play Mode.
    /// Menu: ZCOM > Play Vertical Slice, or from the command line:
    /// -executeMethod Zcom.EditorTools.QuickPlay.OpenAndPlay
    /// </summary>
    public static class QuickPlay
    {
        [MenuItem("ZCOM/Play Vertical Slice", priority = 1)]
        public static void OpenAndPlay()
        {
            // Defer until the editor has finished loading its layout/windows.
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(SliceBuilder.ScenePath)) SliceBuilder.Build();
                if (EditorSceneManager.GetActiveScene().path != SliceBuilder.ScenePath)
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                    EditorSceneManager.OpenScene(SliceBuilder.ScenePath);
                }
                EditorApplication.EnterPlaymode();
            };
        }
    }
}
