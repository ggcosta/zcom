using System.IO;
using UnityEditor;
using UnityEngine;

namespace Zcom.EditorTools
{
    /// <summary>
    /// Builds Resources/Materials/Props/{Name}.mat (URP Lit: albedo, normal, metallic-smoothness, emission) for every
    /// generated prop model Resources/Models/Prop_{Name}.fbx from its baked textures in Resources/Textures/Props.
    /// </summary>
    public static class PropSetup
    {
        const string ModelDir = "Assets/ZCOM/Resources/Models";
        const string TexDir = "Assets/ZCOM/Resources/Textures/Props";
        const string MatDir = "Assets/ZCOM/Resources/Materials/Props";
        const string ShipTexDir = "Assets/ZCOM/Resources/Textures/Ship";
        const string ShipMatDir = "Assets/ZCOM/Resources/Materials/Ship";

        [MenuItem("ZCOM/Rebuild Prop Materials", priority = 21)]
        public static void EnsureMaterials()
        {
            if (!AssetDatabase.IsValidFolder("Assets/ZCOM/Resources/Materials")) AssetDatabase.CreateFolder("Assets/ZCOM/Resources", "Materials");
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/ZCOM/Resources/Materials", "Props");
            int n = 0;
            foreach (var path in Directory.GetFiles(ModelDir, "Prop_*.fbx"))
            {
                string name = Path.GetFileNameWithoutExtension(path).Substring(5);
                var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}_BaseColor.png");
                if (!baseMap) continue;
                string mp = $"{MatDir}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                bool create = !m;
                if (create) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetTexture("_BaseMap", baseMap);
                m.SetColor("_BaseColor", Color.white);
                var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}_Normal.png");
                if (nrm) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
                var ms = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}_MetallicSmoothness.png");
                if (ms) { m.SetTexture("_MetallicGlossMap", ms); m.SetFloat("_Smoothness", 1f); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                var em = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}_Emission.png");
                if (em)
                {
                    m.SetTexture("_EmissionMap", em);
                    m.SetColor("_EmissionColor", Color.white * (name.StartsWith("Hive") || name.StartsWith("Egg") ? 3.5f : 2.5f));
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                if (create) AssetDatabase.CreateAsset(m, mp);
                EditorUtility.SetDirty(m);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ZCOM] prop materials: {n}");
        }

        /// <summary>Resources/Materials/Ship/{Piece}.mat (URP Lit) from Resources/Textures/Ship/{Piece}_*.png.</summary>
        [MenuItem("ZCOM/Rebuild Ship Materials", priority = 22)]
        public static void EnsureShipMaterials()
        {
            SliceBuilder.EnsureFolder(ShipMatDir);
            if (!AssetDatabase.IsValidFolder(ShipTexDir)) return;
            int n = 0;
            foreach (var path in Directory.GetFiles(ShipTexDir, "*_BaseColor.png"))
            {
                string name = Path.GetFileNameWithoutExtension(path).Replace("_BaseColor", "");
                var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ShipTexDir}/{name}_BaseColor.png");
                if (!baseMap) continue;
                string mp = $"{ShipMatDir}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                bool create = !m;
                if (create) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetTexture("_BaseMap", baseMap);
                m.SetColor("_BaseColor", Color.white);
                var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ShipTexDir}/{name}_Normal.png");
                if (nrm) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
                var ms = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ShipTexDir}/{name}_MetallicSmoothness.png");
                if (ms) { m.SetTexture("_MetallicGlossMap", ms); m.SetFloat("_Smoothness", 1f); m.SetFloat("_Metallic", 1f); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                var em = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ShipTexDir}/{name}_Emission.png");
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (em)
                {
                    m.SetTexture("_EmissionMap", em);
                    float k = name.StartsWith("Interior") ? 2.6f : name == "Engine" ? 1.3f : 3.2f;
                    m.SetColor("_EmissionColor", Color.white * k);
                }
                else m.SetColor("_EmissionColor", Color.black);
                if (create) AssetDatabase.CreateAsset(m, mp);
                EditorUtility.SetDirty(m);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ZCOM] ship materials: {n}");
        }

    }
}
