using UnityEditor;
using UnityEngine;

namespace Zcom.EditorTools
{
    /// <summary>Import settings for the Blender-generated character FBX files in Resources/Models.</summary>
    public class ModelImportSettings : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("ZCOM/Resources/Models/")) return;
            var m = (ModelImporter)assetImporter;
            m.globalScale = 1f;
            m.useFileScale = true;
            m.importCameras = false;
            m.importLights = false;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool animated = file.StartsWith("Marshal");
            // The insect rigs carry no clips (legs are IK, deaths are ragdolls), but with the rig type set to None Unity
            // drops the skin and imports a rigid mesh that ignores its bones.
            bool rigged = animated || file.StartsWith("Alien_");
            m.importAnimation = animated;
            m.animationType = rigged ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            if (rigged && !animated) m.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            if (animated)
            {
                m.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                m.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
                m.resampleCurves = true;
            }
            m.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            m.importNormals = ModelImporterNormals.Import;
            m.meshCompression = ModelImporterMeshCompression.Off;
            m.isReadable = System.IO.Path.GetFileNameWithoutExtension(assetPath).StartsWith("Prop_");   // mesh colliders
        }
    }

    /// <summary>UI art in Resources/UI (Codex-generated icons, portraits) imports as high-quality sprites.</summary>
    public class UiTextureImportSettings : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("ZCOM/Resources/UI/")) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Sprite;
            t.spriteImportMode = SpriteImportMode.Single;
            t.alphaIsTransparency = true;
            t.mipmapEnabled = true;
            t.filterMode = FilterMode.Trilinear;
            t.textureCompression = TextureImporterCompression.Uncompressed;
            t.wrapMode = TextureWrapMode.Clamp;
        }
    }

    /// <summary>Baked character textures (Resources/Textures): normal maps and linear mask maps.</summary>
    public class CharacterTextureImportSettings : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("ZCOM/Resources/Textures/")) return;
            var t = (TextureImporter)assetImporter;
            string n = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            t.maxTextureSize = 2048;
            if (assetPath.Contains("/Terrain/")) t.wrapMode = TextureWrapMode.Repeat;
            t.mipmapEnabled = true;
            t.anisoLevel = 4;
            t.textureCompression = TextureImporterCompression.CompressedHQ;
            if (n.EndsWith("_Normal") || n.EndsWith("_n")) t.textureType = TextureImporterType.NormalMap;
            else if (n.Contains("Metallic")) { t.textureType = TextureImporterType.Default; t.sRGBTexture = false; }
            else { t.textureType = TextureImporterType.Default; t.sRGBTexture = true; }
        }
    }

    /// <summary>Makes every clip in the animated character FBX loop (except Death).</summary>
    public class AnimationImportSettings : AssetPostprocessor
    {
        void OnPreprocessAnimation()
        {
            if (!assetPath.Replace('\\', '/').Contains("ZCOM/Resources/Models/Marshal")) return;
            var m = (ModelImporter)assetImporter;
            var clips = m.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = c.takeName.Contains("|") ? c.takeName.Substring(c.takeName.LastIndexOf('|') + 1) : c.takeName;
                c.loopTime = c.name != "Death";
                c.loopPose = false;
            }
            m.clipAnimations = clips;
        }
    }

    public static class ModelDiagnostics
    {
        /// <summary>Batch: -executeMethod Zcom.EditorTools.ModelDiagnostics.Run</summary>
        public static void Run()
        {
            AssetDatabase.Refresh();
            foreach (var name in new[] { "Marshal", "Skitter", "Spitter", "Brute" })
            {
                var go = Resources.Load<GameObject>("Models/" + name);
                if (!go) { Debug.Log($"[ZCOM MODEL] {name}: NOT FOUND"); continue; }
                var inst = Object.Instantiate(go);
                var b = new Bounds();
                bool first = true;
                foreach (var r in inst.GetComponentsInChildren<Renderer>())
                {
                    if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                }
                var sb = new System.Text.StringBuilder();
                sb.Append($"[ZCOM MODEL] {name}: rootRot={inst.transform.rotation.eulerAngles} bounds center={b.center} size={b.size}\n");
                foreach (Transform c in inst.transform)
                {
                    var r = c.GetComponent<Renderer>();
                    string mats = r ? string.Join(",", System.Array.ConvertAll(r.sharedMaterials, m => m ? m.name : "null")) : "-";
                    sb.Append($"   {c.name} pos={c.localPosition} rot={c.localRotation.eulerAngles} scale={c.localScale} mats=[{mats}]\n");
                }
                var anim = inst.GetComponent<Animator>();
                sb.Append($"   animator={(anim ? "yes" : "no")} avatar={(anim && anim.avatar ? anim.avatar.name : "-")} skinned={inst.GetComponentsInChildren<SkinnedMeshRenderer>().Length}\n");
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath($"Assets/ZCOM/Resources/Models/{name}.fbx"))
                    if (clip is AnimationClip ac && !ac.name.StartsWith("__preview__")) sb.Append($"   clip {ac.name} len={ac.length:0.00}s loop={ac.isLooping}\n");
                foreach (var bn in new[] { "Hips", "Spine", "Chest", "Head" })
                {
                    var t = CharacterModels.Find(inst.transform, bn);
                    if (t) sb.Append($"   bone {bn} worldPos={t.position}\n");
                }
                Debug.Log(sb.ToString());
                Object.DestroyImmediate(inst);
            }
            MarshalAnimSetup.EnsureController();
            EditorApplication.Exit(0);
        }
    }
}
