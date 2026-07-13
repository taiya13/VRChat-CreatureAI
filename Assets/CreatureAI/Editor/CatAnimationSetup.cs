#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CreatureAI.EditorTools
{
    /// <summary>
    /// 猫モデル(FBX)のインポート設定とAnimatorControllerを自動構築する。
    /// メニュー: CreatureAI > Setup Cat Animations
    /// - 各クリップのループ設定
    /// - テイク名 "Cat_12221_Rig|Cat_Walk" -> "Cat_Walk" へのリネーム
    /// - int パラメータ "MotionState" (0-12, CreatureAnimator.parameterName と一致) で
    ///   切り替わる AnimatorController を生成
    /// </summary>
    public static class CatAnimationSetup
    {
        const string ModelDir = "Assets/CreatureAI/Models";
        const string OutDir = "Assets/CreatureAI/Animations";

        static readonly string[] Models =
        {
            ModelDir + "/Cat_12221.fbx",
            ModelDir + "/Cat_12222.fbx",
        };

        // CreatureAnimator.MotionKind と値を一致させる対応表 (Idle=0 ... LookAround=12)
        static readonly (string clip, int state)[] StateMap =
        {
            ("Cat_Idle", 0),
            ("Cat_Walk", 1),
            ("Cat_Eat", 2),
            ("Cat_Sleep", 3),
            ("Cat_Flee", 4),
            ("Cat_Drink", 5),
            ("Cat_Play", 6),
            ("Cat_Scratch", 7),
            ("Cat_Groom", 8),
            ("Cat_Stretch", 9),
            ("Cat_Yawn", 10),
            ("Cat_Sit", 11),
            ("Cat_LookAround", 12),
        };

        [MenuItem("CreatureAI/Setup Cat Animations")]
        public static void Setup()
        {
            foreach (var model in Models)
            {
                if (!File.Exists(model))
                {
                    Debug.LogWarning($"[CreatureAI] model not found: {model}");
                    continue;
                }
                ConfigureImporter(model);
            }
            AssetDatabase.Refresh();
            foreach (var model in Models)
            {
                if (File.Exists(model))
                    BuildController(model);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[CreatureAI] Cat animation setup complete.");
        }

        static void ConfigureImporter(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;

            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                int bar = clip.name.IndexOf('|');
                if (bar >= 0)
                    clip.name = clip.name.Substring(bar + 1);
                clip.loopTime = true; // 全クリップともループ前提で作成済み
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            Debug.Log($"[CreatureAI] importer configured: {path} ({clips.Length} clips)");
        }

        static void BuildController(string modelPath)
        {
            string baseName = Path.GetFileNameWithoutExtension(modelPath);
            if (!Directory.Exists(OutDir))
                Directory.CreateDirectory(OutDir);
            string ctrlPath = $"{OutDir}/{baseName}_Animator.controller";

            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            if (existing != null)
                AssetDatabase.DeleteAsset(ctrlPath);

            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            ctrl.AddParameter("MotionState", AnimatorControllerParameterType.Int);
            var sm = ctrl.layers[0].stateMachine;

            var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview"))
                .GroupBy(c => CleanName(c.name))
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var (clipName, stateIdx) in StateMap)
            {
                if (!clips.TryGetValue(clipName, out var clip))
                {
                    Debug.LogWarning($"[CreatureAI] clip missing in {modelPath}: {clipName}");
                    continue;
                }
                var state = sm.AddState(clipName.Replace("Cat_", ""),
                    new Vector3(360f, 60f * stateIdx, 0f));
                state.motion = clip;

                var tr = sm.AddAnyStateTransition(state);
                tr.hasExitTime = false;
                tr.hasFixedDuration = true;
                tr.duration = 0.25f;
                tr.canTransitionToSelf = false;
                tr.AddCondition(AnimatorConditionMode.Equals, stateIdx, "MotionState");

                if (stateIdx == 0)
                    sm.defaultState = state;
            }

            EditorUtility.SetDirty(ctrl);
            Debug.Log($"[CreatureAI] controller built: {ctrlPath}");
        }

        static string CleanName(string name)
        {
            int bar = name.IndexOf('|');
            return bar >= 0 ? name.Substring(bar + 1) : name;
        }
    }
}
#endif
