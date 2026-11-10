using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Zcom.EditorTools
{
    /// <summary>
    /// Builds Resources/Anim/Marshal.controller from the clips baked into Resources/Models/Marshal.fbx:
    /// an Idle state, a Move state (2D freeform-directional blend of the four run directions on MoveX / MoveZ)
    /// cross-faded on Speed, and a Death state on the "Dead" trigger. Idle is not part of the run blend: a blend
    /// tree plays its children in sync, so mixing the 2.5 s idle with the 0.63 s run cycles put the legs in slow
    /// motion every time the Marshal started or stopped. Clips in
    /// Resources/Models/MarshalMoves.fbx - library animation retargeted onto the same rig by
    /// Tools/Blender/zcom_marine_mocap.py - replace the procedural ones of the same name.
    /// Also builds Resources/Materials/MarshalArmor.mat from the baked texture set in Resources/Textures/Marshal.
    /// </summary>
    public static class MarshalAnimSetup
    {
        const string ModelPath = "Assets/ZCOM/Resources/Models/Marshal.fbx";
        const string MovesPath = "Assets/ZCOM/Resources/Models/MarshalMoves.fbx";
        const string ControllerDir = "Assets/ZCOM/Resources/Anim";
        public const string ControllerPath = ControllerDir + "/Marshal.controller";
        const string TexDir = "Assets/ZCOM/Resources/Textures/Marshal";
        const string MatPath = "Assets/ZCOM/Resources/Materials/MarshalArmor.mat";

        [MenuItem("ZCOM/Rebuild Marshal Animator", priority = 20)]
        public static void EnsureController()
        {
            EnsureMaterial();
            var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            if (clips.Length == 0) { Debug.LogWarning("[ZCOM] Marshal.fbx has no clips yet."); return; }
            var moves = AssetDatabase.LoadAllAssetsAtPath(MovesPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            AnimationClip Get(string n) => moves.FirstOrDefault(c => c.name == n)
                ?? clips.FirstOrDefault(c => c.name == n) ?? clips.FirstOrDefault(c => c.name.EndsWith(n));

            if (!AssetDatabase.IsValidFolder(ControllerDir)) AssetDatabase.CreateFolder("Assets/ZCOM/Resources", "Anim");
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Dead", AnimatorControllerParameterType.Trigger);
            var sm = ctrl.layers[0].stateMachine;
            var idle = sm.AddState("Idle", new Vector3(300, 0, 0));
            idle.motion = Get("Idle");
            var state = ctrl.CreateBlendTreeInController("Move", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveZ";
            tree.AddChild(Get("Run"), new Vector2(0f, 1f));
            tree.AddChild(Get("RunBack"), new Vector2(0f, -1f));
            // Blender +X is the character's left -> Unity local -X.
            if (Get("StrafeL")) tree.AddChild(Get("StrafeL"), new Vector2(-1f, 0f));
            if (Get("StrafeR")) tree.AddChild(Get("StrafeR"), new Vector2(1f, 0f));
            sm.defaultState = idle;
            // short cross-fades, a little hysteresis so a stick at the threshold does not flicker between them
            var go = idle.AddTransition(state);
            go.AddCondition(AnimatorConditionMode.Greater, 0.18f, "Speed");
            go.hasExitTime = false;
            go.duration = 0.14f;
            var stop = state.AddTransition(idle);
            stop.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            stop.hasExitTime = false;
            stop.duration = 0.22f;
            var deathClip = Get("Death");
            if (deathClip)
            {
                var death = sm.AddState("Death", new Vector3(300, 120, 0));
                death.motion = deathClip;
                var t = sm.AddAnyStateTransition(death);
                t.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
                t.duration = 0.12f;
                t.canTransitionToSelf = false;
            }
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ZCOM] Marshal animator built with clips: {string.Join(", ", clips.Select(c => c.name))}" +
                      $" | from MarshalMoves: {string.Join(", ", moves.Select(c => $"{c.name} {c.length:0.00}s loop={c.isLooping}"))}");
        }

        static void EnsureMaterial()
        {
            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/Marshal_BaseColor.png");
            if (!baseMap) return;
            if (!AssetDatabase.IsValidFolder("Assets/ZCOM/Resources/Materials")) AssetDatabase.CreateFolder("Assets/ZCOM/Resources", "Materials");
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            bool create = !m;
            if (create) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", baseMap);
            m.SetColor("_BaseColor", Color.white);
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/Marshal_Normal.png");
            if (n) { m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            var ms = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/Marshal_MetallicSmoothness.png");
            if (ms) { m.SetTexture("_MetallicGlossMap", ms); m.SetFloat("_Smoothness", 1f); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
            var em = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/Marshal_Emission.png");
            if (em)
            {
                m.SetTexture("_EmissionMap", em);
                m.SetColor("_EmissionColor", new Color(1f, 1f, 1f) * 4f);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            if (create) AssetDatabase.CreateAsset(m, MatPath);
            EditorUtility.SetDirty(m);
        }
    }
}
