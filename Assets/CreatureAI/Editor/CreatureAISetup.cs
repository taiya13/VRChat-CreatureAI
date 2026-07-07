using System;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace CreatureAI.EditorTools
{
    /// <summary>
    /// CreatureAI のセットアップ自動化メニュー。
    ///
    /// UdonSharp は「.cs を入れるだけ」では動かず、スクリプト1つにつき
    /// UdonSharpProgramAsset(実行データ)が必要。これが無いと
    /// 「Unable to find valid U# program asset」となり Inspector も壊れる。
    /// このツールが Program Asset の生成とテスト用オブジェクトの組み立てを行う。
    ///
    /// 判定は「固定パス Assets/CreatureAI/ProgramAssets/<型名>.asset」で行い、
    /// GetClass() のタイミング依存を避ける。何度実行しても安全(冪等)。
    /// </summary>
    public static class CreatureAISetup
    {
        private static readonly Type[] BehaviourTypes =
        {
            typeof(CreatureCore),
            typeof(CreaturePoint),
            typeof(CreaturePointRegistry),
            typeof(CreaturePointSensor),
            typeof(NeedsController),
            typeof(CreatureProfile),
        };

        // Program Asset の置き場所。スクリプトの実際の配置場所に依存しないよう、
        // Assets 直下の固定フォルダに置く(Program Asset はどこにあってもスクリプトを
        // GUID 参照するので位置は自由)。.cs 本体は FindScript が全プロジェクトから探す。
        private const string ProgramAssetFolder = "Assets/CreatureAI_ProgramAssets";

        private static string PathFor(Type t)
        {
            return ProgramAssetFolder + "/" + t.Name + ".asset";
        }

        // ================= メニュー 0: 状態確認(診断用) =================

        [MenuItem("CreatureAI/0. 状態を確認 (診断)", false, 0)]
        public static void Diagnose()
        {
            Debug.Log("===== CreatureAI 診断開始 =====");
            Debug.Log("ProgramAssets フォルダ存在: " + AssetDatabase.IsValidFolder(ProgramAssetFolder));
            foreach (Type t in BehaviourTypes)
            {
                MonoScript script = FindScript(t);
                string scriptState = script != null ? ("見つかった (" + AssetDatabase.GetAssetPath(script) + ")") : "★見つからない★";

                UdonSharpProgramAsset pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(PathFor(t));
                string paState;
                if (pa == null) paState = "★無し★";
                else if (pa.sourceCsScript == null) paState = "有るが sourceCsScript=None";
                else paState = "OK (source=" + pa.sourceCsScript.name + ")";

                Debug.Log("[" + t.Name + "] .cs=" + scriptState + " / ProgramAsset=" + paState);
            }
            Debug.Log("===== CreatureAI 診断終了 =====");
            EditorUtility.DisplayDialog("CreatureAI 診断",
                "結果を Console に出力しました。\n各行の .cs と ProgramAsset の状態を確認してください。",
                "OK");
        }

        // ================= メニュー 1: Program Asset 生成 =================

        [MenuItem("CreatureAI/1. Program Asset を作成 (最初に1回)", false, 1)]
        public static void SetupProgramAssets()
        {
            if (!AssetDatabase.IsValidFolder(ProgramAssetFolder))
            {
                // Assets は常に存在するので、その直下に固定フォルダを作る(場所非依存)。
                AssetDatabase.CreateFolder("Assets", "CreatureAI_ProgramAssets");
            }

            int created = 0, repaired = 0, ok = 0, failed = 0;

            foreach (Type t in BehaviourTypes)
            {
                MonoScript script = FindScript(t);
                if (script == null)
                {
                    Debug.LogError("[CreatureAI Setup] .cs が見つからない: " + t.Name +
                        " → コンパイルが通っているか確認してください。");
                    failed++;
                    continue;
                }

                string path = PathFor(t);
                UdonSharpProgramAsset pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);

                if (pa == null)
                {
                    pa = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                    pa.sourceCsScript = script;
                    AssetDatabase.CreateAsset(pa, path);
                    created++;
                    Debug.Log("[CreatureAI Setup] 作成: " + path);
                }
                else if (pa.sourceCsScript == null || pa.sourceCsScript != script)
                {
                    pa.sourceCsScript = script;
                    EditorUtility.SetDirty(pa);
                    repaired++;
                    Debug.Log("[CreatureAI Setup] 修復(sourceCsScript を再設定): " + path);
                }
                else
                {
                    ok++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 実行データをコンパイル。
            UdonSharpProgramAsset.CompileAllCsPrograms(true);

            string msg = "Program Asset: 新規 " + created + " / 修復 " + repaired +
                         " / 既存OK " + ok + " / 失敗 " + failed + "\n\n";
            if (failed > 0)
                msg += "★ 失敗が " + failed + " 件あります。Console の赤いエラー(.cs が見つからない等)を確認してください。";
            else
                msg += "U# コンパイルが走ります。Console にエラーが無ければ\n『CreatureAI > 2. テスト用 Cat と FoodBowl を作成』へ。";

            EditorUtility.DisplayDialog("CreatureAI Setup", msg, "OK");
        }

        // ================= メニュー 2: テスト用オブジェクト組み立て =================

        [MenuItem("CreatureAI/2. テスト用 Cat と FoodBowl を作成", false, 2)]
        public static void CreateTestSceneObjects()
        {
            // 固定パスで Program Asset の有無を確認(GetClass 非依存)。
            System.Text.StringBuilder missing = new System.Text.StringBuilder();
            foreach (Type t in BehaviourTypes)
            {
                UdonSharpProgramAsset pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(PathFor(t));
                if (pa == null || pa.sourceCsScript == null)
                {
                    missing.Append("・").Append(t.Name).Append("\n");
                }
            }
            if (missing.Length > 0)
            {
                EditorUtility.DisplayDialog("CreatureAI Setup",
                    "次の Program Asset がまだありません:\n" + missing +
                    "\n先に『CreatureAI > 1. Program Asset を作成』を実行してください。\n" +
                    "それでも出る場合は『0. 状態を確認』を押して Console を確認してください。",
                    "OK");
                return;
            }

            if (GameObject.Find("Cat") != null || GameObject.Find("__CatAI_Registry") != null)
            {
                EditorUtility.DisplayDialog("CreatureAI Setup",
                    "シーンに既に Cat または __CatAI_Registry があります。\n" +
                    "以前の(壊れている可能性のある)ものを削除してから再実行してください。", "OK");
                return;
            }

            GameObject cat = new GameObject("Cat");
            Undo.RegisterCreatedObjectUndo(cat, "Create CreatureAI Cat");
            AddUdonSharp<CreatureCore>(cat);
            AddUdonSharp<NeedsController>(cat);
            AddUdonSharp<CreaturePointSensor>(cat);

            GameObject profile = new GameObject("Profile");
            profile.transform.SetParent(cat.transform, false);
            AddUdonSharp<CreatureProfile>(profile);

            GameObject registry = new GameObject("__CatAI_Registry");
            registry.transform.SetParent(cat.transform, false);
            AddUdonSharp<CreaturePointRegistry>(registry);

            GameObject bowl = new GameObject("FoodBowl");
            Undo.RegisterCreatedObjectUndo(bowl, "Create CreatureAI FoodBowl");
            bowl.transform.position = new Vector3(2f, 0f, 0f);
            CreaturePoint point = AddUdonSharp<CreaturePoint>(bowl);
            if (point != null)
            {
                point.pointType = PointType.Food;
                EditorUtility.SetDirty(point);
            }

            Selection.activeGameObject = cat;

            EditorUtility.DisplayDialog("CreatureAI Setup",
                "Cat(+Profile, __CatAI_Registry)と FoodBowl を作成しました。\n\n" +
                "▶ Play で Console に『Singleton に選出』『登録: FoodBowl』『近傍候補: 1 個』が\n" +
                "出れば Phase 1 成功です。", "OK");
        }

        // ================= 内部ヘルパー =================

        private static T AddUdonSharp<T>(GameObject go) where T : UdonSharpBehaviour
        {
            T proxy = Undo.AddComponent<T>(go);
            try
            {
                UdonSharpEditorUtility.CreateBehaviourForProxy(proxy);
            }
            catch (Exception e)
            {
                Debug.LogError("[CreatureAI Setup] " + typeof(T).Name + " のセットアップに失敗: " + e.Message);
            }
            return proxy;
        }

        /// <summary>
        /// 型に対応する .cs (MonoScript) を、ファイルパス末尾 "/<型名>.cs" で厳密一致検索する。
        /// (GetClass に依存しないので、部分名一致 CreaturePoint/CreaturePointSensor の混同も避けられる)
        /// </summary>
        private static MonoScript FindScript(Type t)
        {
            string suffix = "/" + t.Name + ".cs";
            foreach (string guid in AssetDatabase.FindAssets(t.Name + " t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(suffix, StringComparison.Ordinal))
                {
                    MonoScript ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    if (ms != null) return ms;
                }
            }
            return null;
        }
    }
}
