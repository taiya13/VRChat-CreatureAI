#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CreatureAI.EditorTools
{
    /// <summary>
    /// 猫モデル(FBX)を「置いた場所を問わず」自動セットアップする。
    /// メニュー: CreatureAI > 5. 猫モデルをセットアップ (Import+Controller)
    ///
    /// やること:
    ///  1. プロジェクト内から Cat_12221 / Cat_12222 の .fbx を名前で探索
    ///     (Assets 直下でも Models フォルダでも、どこでも見つける)
    ///  2. インポート設定を Generic にし、全クリップをループ・名前正規化
    ///     ("Cat_12221_Rig|Cat_Walk" -> "Cat_Walk")
    ///  3. FBX と同じフォルダに <名前>_Animator.controller を生成
    ///     int パラメータ "MotionState" (0-12, CreatureAnimator.parameterName と一致)
    ///  4. シーン内の同名モデルインスタンスに Animator + Controller を自動割り当て
    ///     (これで手動での Controller 割り当ては不要)
    /// </summary>
    public static class CatAnimationSetup
    {
        // 対象モデルのベース名。増えたらここに足すだけ。
        static readonly string[] BaseNames = { "Cat_12221", "Cat_12222" };

        // CreatureAnimator の MotionKind と値を一致させる対応表 (Idle=0 ... LookAround=12)
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

        [MenuItem("CreatureAI/5. 猫モデルをセットアップ (Import+Controller)", false, 5)]
        public static void Setup()
        {
            StringBuilder report = new StringBuilder();
            int ok = 0;

            foreach (string baseName in BaseNames)
            {
                string fbx = FindFbx(baseName);
                if (fbx == null)
                {
                    report.Append("・").Append(baseName)
                          .Append(".fbx が見つかりません(未インポート?)\n");
                    continue;
                }

                ConfigureImporter(fbx);
                AnimatorController ctrl = BuildController(fbx, baseName);
                if (ctrl == null)
                {
                    report.Append("・").Append(baseName)
                          .Append(": Controller 生成に失敗(Console参照)\n");
                    continue;
                }

                int assigned = AssignInScene(baseName, ctrl);
                report.Append("・").Append(baseName)
                      .Append(": Controller作成OK → シーンの ").Append(assigned)
                      .Append(" 体に自動割り当て").Append(assigned == 0 ? "(シーンに未配置)" : "").Append("\n");
                ok++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string head = ok > 0 ? "セットアップ完了:\n\n" : "モデルが見つかりませんでした:\n\n";
            string tail = ok > 0
                ? "\n▶ Play して Console に『[Animator] … 反映先 n件』(n≥1) が出れば成功です。\n" +
                  "シーンにまだ猫モデルを置いていない場合は、Project から\n" +
                  "Cat_12221 / Cat_12222 を Cat の子にドラッグしてから、もう一度この\n" +
                  "メニューを実行すれば Controller が自動で付きます。"
                : "Cat_12221.fbx / Cat_12222.fbx をプロジェクトに取り込んでから再実行してください。";
            EditorUtility.DisplayDialog("CreatureAI 猫セットアップ", head + report + tail, "OK");
            Debug.Log("[CreatureAI] 猫セットアップ: " + report.ToString().Replace("\n", " / "));
        }

        /// <summary>ベース名の .fbx をプロジェクト全体から探す(置き場所を問わない)。</summary>
        static string FindFbx(string baseName)
        {
            foreach (string guid in AssetDatabase.FindAssets(baseName + " t:Model"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileNameWithoutExtension(p) == baseName)
                    return p;
            }
            return null;
        }

        static void ConfigureImporter(string path)
        {
            ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                int bar = clips[i].name.IndexOf('|');
                if (bar >= 0) clips[i].name = clips[i].name.Substring(bar + 1);
                clips[i].loopTime = true; // 全クリップともループ前提で作成済み
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            Debug.Log("[CreatureAI] インポート設定: " + path + " (" + clips.Length + " clips, Generic)");
        }

        static AnimatorController BuildController(string fbxPath, string baseName)
        {
            string dir = Path.GetDirectoryName(fbxPath).Replace("\\", "/");
            string ctrlPath = dir + "/" + baseName + "_Animator.controller";

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath) != null)
                AssetDatabase.DeleteAsset(ctrlPath);

            AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            ctrl.AddParameter("MotionState", AnimatorControllerParameterType.Int);
            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;

            var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview"))
                .GroupBy(CleanName)
                .ToDictionary(g => g.Key, g => g.First());

            int found = 0;
            foreach ((string clipName, int stateIdx) in StateMap)
            {
                if (!clips.TryGetValue(clipName, out AnimationClip clip))
                {
                    Debug.LogWarning("[CreatureAI] クリップが無い(" + baseName + "): " + clipName);
                    continue;
                }
                AnimatorState st = sm.AddState(clipName.Replace("Cat_", ""),
                    new Vector3(360f, 60f * stateIdx, 0f));
                st.motion = clip;
                st.writeDefaultValues = true;

                AnimatorStateTransition tr = sm.AddAnyStateTransition(st);
                tr.hasExitTime = false;
                tr.hasFixedDuration = true;
                tr.duration = 0.2f;
                tr.canTransitionToSelf = false;
                tr.AddCondition(AnimatorConditionMode.Equals, stateIdx, "MotionState");

                if (stateIdx == 0) sm.defaultState = st;
                found++;
            }

            EditorUtility.SetDirty(ctrl);
            Debug.Log("[CreatureAI] Controller生成: " + ctrlPath + " (" + found + "/13 states)");
            return ctrl;
        }

        /// <summary>
        /// シーン内の「モデルのルート(名前=baseName、親が同名でない側)」に Animator を付け、
        /// Controller を割り当てる。CreatureAnimator が配下の Animator を自動検出するので、
        /// これでモーションが AI と繋がる。
        /// </summary>
        static int AssignInScene(string baseName, AnimatorController ctrl)
        {
            int n = 0;
#if UNITY_2020_1_OR_NEWER
            GameObject[] all = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
#else
            GameObject[] all = UnityEngine.Object.FindObjectsOfType<GameObject>();
#endif
            foreach (GameObject go in all)
            {
                if (go.name != baseName) continue;
                // 内側(メッシュ子)ではなくモデルのルートだけを対象にする。
                if (go.transform.parent != null && go.transform.parent.name == baseName) continue;

                Animator anim = go.GetComponent<Animator>();
                if (anim == null) anim = Undo.AddComponent<Animator>(go);
                anim.applyRootMotion = false;
                anim.runtimeAnimatorController = ctrl;
                EditorUtility.SetDirty(anim);
                n++;
            }
            return n;
        }

        static string CleanName(AnimationClip c)
        {
            int bar = c.name.IndexOf('|');
            return bar >= 0 ? c.name.Substring(bar + 1) : c.name;
        }
    }
}
#endif
