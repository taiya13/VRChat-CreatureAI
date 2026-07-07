using System;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CreatureAI.EditorTools
{
    /// <summary>
    /// CreatureAI のセットアップ自動化メニュー。
    ///
    /// UdonSharp は「.cs を入れるだけ」では動かず、スクリプト1つにつき
    /// UdonSharpProgramAsset(実行データ)が必要。これが無いと
    /// 「Unable to find valid U# program asset」となり Inspector も壊れる。
    ///
    /// また、ソース未設定(sourceCsScript=None)の壊れた ProgramAsset が
    /// プロジェクトに1つでも残っていると、UdonSharp が内部の「クラス→アセット」辞書を
    /// 作る際に null キーで落ち(Value cannot be null. Parameter name: key)、
    /// 以後すべてのコンポーネント追加が失敗する。メニュー1でこれを自動掃除する。
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
            typeof(NeedsData),
            typeof(CreatureBrain),
            typeof(CreatureTargetSelector),
            typeof(CreatureStatusDisplay),
            typeof(CreatureProfile),
        };

        private const string ProgramAssetFolder = "Assets/CreatureAI_ProgramAssets";

        private static string PathFor(Type t) { return ProgramAssetFolder + "/" + t.Name + ".asset"; }

        // ================= メニュー 0: 診断 =================

        [MenuItem("CreatureAI/0. 状態を確認 (診断)", false, 0)]
        public static void Diagnose()
        {
            Debug.Log("===== CreatureAI 診断開始 =====");
            Debug.Log("ProgramAssets フォルダ存在: " + AssetDatabase.IsValidFolder(ProgramAssetFolder));
            foreach (Type t in BehaviourTypes)
            {
                MonoScript script = FindScript(t);
                string scriptState = script != null ? "OK" : "★.cs が見つからない★";
                UdonSharpProgramAsset pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(PathFor(t));
                string paState = pa == null ? "★無し★"
                    : (pa.sourceCsScript == null ? "★sourceCsScript=None★" : "OK");
                Debug.Log("[" + t.Name + "] .cs=" + scriptState + " / ProgramAsset=" + paState);
            }
            int broken = CountBrokenProgramAssets(true);
            Debug.Log("プロジェクト全体の壊れ(ソース未設定)Program Asset: " + broken + " 個");
            Debug.Log("===== CreatureAI 診断終了 =====");
            EditorUtility.DisplayDialog("CreatureAI 診断",
                "結果を Console に出力しました。\n壊れ Program Asset が 0 でないなら、" +
                "メニュー1(または『壊れた Program Asset を掃除』)を実行してください。", "OK");
        }

        // ================= メニュー 1: Program Asset 生成 =================

        [MenuItem("CreatureAI/1. Program Asset を作成 (最初に1回)", false, 1)]
        public static void SetupProgramAssets()
        {
            // ① 壊れアセットを先に掃除(これが「Value cannot be null. key」の根本対策)。
            int cleaned = CleanupBrokenProgramAssets(true);

            if (!AssetDatabase.IsValidFolder(ProgramAssetFolder))
                AssetDatabase.CreateFolder("Assets", "CreatureAI_ProgramAssets");

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
                }
                else if (pa.sourceCsScript == null || pa.sourceCsScript != script)
                {
                    pa.sourceCsScript = script;
                    EditorUtility.SetDirty(pa);
                    repaired++;
                }
                else ok++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            UdonSharpProgramAsset.CompileAllCsPrograms(true);

            string msg = "壊れアセット削除 " + cleaned + "\n" +
                         "Program Asset: 新規 " + created + " / 修復 " + repaired +
                         " / 既存OK " + ok + " / 失敗 " + failed + "\n\n";
            msg += (failed > 0)
                ? "★ 失敗があります。Console の赤エラーを確認してください。"
                : "U# コンパイル後、Console にエラーが無ければ\n『CreatureAI > 2. テスト用の猫を作成』へ。";
            EditorUtility.DisplayDialog("CreatureAI Setup", msg, "OK");
        }

        // ================= メニュー 2: テスト用オブジェクト組み立て =================

        [MenuItem("CreatureAI/2. テスト用の猫を作成", false, 2)]
        public static void CreateTestSceneObjects()
        {
            // 念のため壊れアセットを黙って掃除(辞書汚染で AddComponent が落ちるのを防ぐ)。
            CleanupBrokenProgramAssets(false);

            System.Text.StringBuilder missing = new System.Text.StringBuilder();
            foreach (Type t in BehaviourTypes)
            {
                UdonSharpProgramAsset pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(PathFor(t));
                if (pa == null || pa.sourceCsScript == null) missing.Append("・").Append(t.Name).Append("\n");
            }
            if (missing.Length > 0)
            {
                EditorUtility.DisplayDialog("CreatureAI Setup",
                    "次の Program Asset がまだありません:\n" + missing +
                    "\n先に『CreatureAI > 1. Program Asset を作成』を実行してください。", "OK");
                return;
            }

            if (GameObject.Find("Cat") != null || GameObject.Find("__CatAI_Registry") != null)
            {
                EditorUtility.DisplayDialog("CreatureAI Setup",
                    "シーンに既に Cat または __CatAI_Registry があります。\n" +
                    "以前のものを削除してから再実行してください。", "OK");
                return;
            }

            // --- Cat 本体 ---
            GameObject cat = new GameObject("Cat");
            Undo.RegisterCreatedObjectUndo(cat, "Create CreatureAI Cat");
            AddUdonSharp<CreatureCore>(cat);
            AddUdonSharp<NeedsController>(cat);
            AddUdonSharp<NeedsData>(cat);
            AddUdonSharp<CreatureBrain>(cat);
            AddUdonSharp<CreatureTargetSelector>(cat);
            AddUdonSharp<CreaturePointSensor>(cat);

            GameObject profile = new GameObject("Profile");
            profile.transform.SetParent(cat.transform, false);
            AddUdonSharp<CreatureProfile>(profile);

            GameObject registry = new GameObject("__CatAI_Registry");
            registry.transform.SetParent(cat.transform, false);
            AddUdonSharp<CreaturePointRegistry>(registry);

            // --- ワールド側ポイント: 餌 と ベッド ---
            CreatePoint("FoodBowl", PointType.Food, new Vector3(2f, 0f, 0f));
            CreatePoint("Bed", PointType.Bed, new Vector3(-2f, 0f, 1f));

            // --- 頭上の状態表示ボード ---
            BuildStatusBoard(cat);

            Selection.activeGameObject = cat;
            EditorUtility.DisplayDialog("CreatureAI Setup",
                "Cat 一式・FoodBowl・Bed・状態表示ボードを作成しました。\n\n" +
                "▶ Play で、頭上ボードに Hunger/Sleepiness のバーと Goal/Target が\n" +
                "表示され、Console に [Brain]/[Target] ログが出れば成功です。", "OK");
        }

        // ================= メニュー 9: 壊れアセット掃除(単体) =================

        [MenuItem("CreatureAI/9. 壊れた Program Asset を掃除", false, 20)]
        public static void CleanupMenu()
        {
            int n = CleanupBrokenProgramAssets(true);
            EditorUtility.DisplayDialog("CreatureAI Setup",
                "ソース未設定の壊れた Program Asset を " + n + " 個削除しました。", "OK");
        }

        // ================= 欲求の速さプリセット =================
        // シーン内の全 Cat(CreatureProfile)の食欲・睡眠の増加速度をまとめて変更する。
        // 再生中なら即反映、停止中なら次の Play から反映。

        [MenuItem("CreatureAI/欲求の速さ/のんびり (観察向け)", false, 40)]
        public static void SpeedRelaxed() { ApplyGrowthRates(0.5f, 0.4f, "のんびり"); }

        [MenuItem("CreatureAI/欲求の速さ/ふつう (既定値)", false, 41)]
        public static void SpeedNormal() { ApplyGrowthRates(1.0f, 0.8f, "ふつう"); }

        [MenuItem("CreatureAI/欲求の速さ/はやい (テスト向け)", false, 42)]
        public static void SpeedFast() { ApplyGrowthRates(5f, 4f, "はやい"); }

        [MenuItem("CreatureAI/欲求の速さ/ばくそく (デバッグ向け)", false, 43)]
        public static void SpeedTurbo() { ApplyGrowthRates(20f, 16f, "ばくそく"); }

        /// <summary>シーン内の全 CreatureProfile に食欲/睡眠の増加速度を適用する。</summary>
        private static void ApplyGrowthRates(float hunger, float sleep, string label)
        {
            CreatureProfile[] profiles = UnityEngine.Object.FindObjectsOfType<CreatureProfile>();
            if (profiles == null || profiles.Length == 0)
            {
                EditorUtility.DisplayDialog("CreatureAI",
                    "シーンに Cat(CreatureProfile)がありません。\n先に『2. テスト用の猫を作成』で猫を用意してください。", "OK");
                return;
            }

            int n = 0;
            foreach (CreatureProfile p in profiles)
            {
                try
                {
                    // ① 編集時の値(次の Play 以降で有効)を書き換えて保存対象にする。
                    p.hungerGrowthRate = hunger;
                    p.sleepinessGrowthRate = sleep;
                    UdonSharpEditorUtility.CopyProxyToUdon(p);
                    EditorUtility.SetDirty(p);

                    // ② 再生中なら、動いている Udon 変数を直接書き換えて即反映する。
                    if (Application.isPlaying)
                    {
                        VRC.Udon.UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
                        if (udon != null)
                        {
                            udon.SetProgramVariable("hungerGrowthRate", hunger);
                            udon.SetProgramVariable("sleepinessGrowthRate", sleep);
                        }
                    }
                    n++;
                }
                catch (Exception e)
                {
                    Debug.LogError("[CreatureAI] 速度適用に失敗: " + e.Message);
                }
            }

            if (!Application.isPlaying) EditorSceneManager.MarkAllScenesDirty();

            Debug.Log("[CreatureAI] 欲求の速さ = 「" + label + "」 (食欲 " + hunger +
                "/秒, 睡眠 " + sleep + "/秒) を " + n + " 匹に適用");
            EditorUtility.DisplayDialog("CreatureAI",
                "欲求の速さ:「" + label + "」を " + n + " 匹に適用しました。\n" +
                "食欲 " + hunger + " /秒、睡眠 " + sleep + " /秒\n\n" +
                (Application.isPlaying ? "再生中なので即反映されます。" : "▶ Play で反映されます。"), "OK");
        }

        // ================= オブジェクト生成ヘルパー =================

        private static void CreatePoint(string name, PointType type, Vector3 pos)
        {
            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create CreatureAI Point");
            go.transform.position = pos;
            CreaturePoint p = AddUdonSharp<CreaturePoint>(go);
            if (p != null) { p.pointType = type; EditorUtility.SetDirty(p); }
        }

        /// <summary>猫の頭上に World Space Canvas + UI.Text の状態ボードを組み立てる。</summary>
        private static void BuildStatusBoard(GameObject cat)
        {
            GameObject board = new GameObject("StatusBoard", typeof(Canvas), typeof(CanvasScaler));
            board.transform.SetParent(cat.transform, false);
            board.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            Canvas canvas = board.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform crt = (RectTransform)board.transform;
            crt.sizeDelta = new Vector2(520f, 260f);
            crt.localScale = new Vector3(0.003f, 0.003f, 0.003f);

            // 背景(半透明の黒)
            GameObject bg = new GameObject("BG", typeof(RectTransform));
            bg.transform.SetParent(board.transform, false);
            Image img = bg.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.6f);
            StretchFull((RectTransform)bg.transform, 0f);

            // テキスト
            GameObject textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(board.transform, false);
            Text text = textGO.AddComponent<Text>();
            Font font = (Font)Resources.GetBuiltinResource(typeof(Font), "LegacyRuntime.ttf");
            if (font == null) font = (Font)Resources.GetBuiltinResource(typeof(Font), "Arial.ttf");
            text.font = font;
            text.fontSize = 22;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = "Cat";
            StretchFull((RectTransform)textGO.transform, 14f);

            CreatureStatusDisplay disp = AddUdonSharp<CreatureStatusDisplay>(board);
            if (disp != null)
            {
                disp.targetText = text;
                disp.displayName = "Cat";
                EditorUtility.SetDirty(disp);
            }
        }

        private static void StretchFull(RectTransform rt, float padding)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        private static T AddUdonSharp<T>(GameObject go) where T : UdonSharpBehaviour
        {
            T proxy = Undo.AddComponent<T>(go);
            try { UdonSharpEditorUtility.CreateBehaviourForProxy(proxy); }
            catch (Exception e)
            {
                Debug.LogError("[CreatureAI Setup] " + typeof(T).Name + " のセットアップに失敗: " + e.Message +
                    "\n→ メニュー1(壊れアセット掃除込み)を実行後、もう一度お試しください。");
            }
            return proxy;
        }

        // ================= Program Asset ヘルパー =================

        /// <summary>ソース未設定の壊れた ProgramAsset を削除して数を返す。</summary>
        private static int CleanupBrokenProgramAssets(bool log)
        {
            int removed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:UdonSharpProgramAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UdonSharpProgramAsset pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
                if (pa != null && pa.sourceCsScript == null)
                {
                    if (log) Debug.LogWarning("[CreatureAI Setup] 壊れた Program Asset を削除: " + path);
                    AssetDatabase.DeleteAsset(path);
                    removed++;
                }
            }
            if (removed > 0) { AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); }
            return removed;
        }

        private static int CountBrokenProgramAssets(bool log)
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:UdonSharpProgramAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UdonSharpProgramAsset pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
                if (pa != null && pa.sourceCsScript == null)
                {
                    if (log) Debug.LogWarning("[CreatureAI 診断] 壊れ: " + path);
                    n++;
                }
            }
            return n;
        }

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
