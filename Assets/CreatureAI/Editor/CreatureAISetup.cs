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
            typeof(MovementController),
            typeof(CreatureStatusDisplay),
            typeof(CreaturePointStatusDisplay),
            typeof(Billboard),
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
            AddUdonSharp<MovementController>(cat);
            AddUdonSharp<CreaturePointSensor>(cat);

            // 見える体(差し替え可能: この Body を消して好きなモデルを Cat の子に置けばよい)。
            AddVisual(cat, "Body", PrimitiveType.Capsule,
                new Vector3(0f, 0.35f, 0f), new Vector3(0.35f, 0.35f, 0.35f),
                new Color(0.95f, 0.6f, 0.2f));

            GameObject profile = new GameObject("Profile");
            profile.transform.SetParent(cat.transform, false);
            AddUdonSharp<CreatureProfile>(profile);

            GameObject registry = new GameObject("__CatAI_Registry");
            registry.transform.SetParent(cat.transform, false);
            AddUdonSharp<CreaturePointRegistry>(registry);

            // --- ワールド側ポイント: 餌 と ベッド(見えるオブジェクト付き・差し替え可能) ---
            CreatePoint("FoodBowl", PointType.Food, new Vector3(3f, 0f, 0f),
                PrimitiveType.Cylinder, new Vector3(0.5f, 0.08f, 0.5f), new Color(0.7f, 0.45f, 0.2f));
            CreatePoint("Bed", PointType.Bed, new Vector3(-3f, 0f, 1.5f),
                PrimitiveType.Cube, new Vector3(1.0f, 0.15f, 1.3f), new Color(0.35f, 0.5f, 0.85f));

            // --- 頭上の状態表示ボード ---
            BuildStatusBoard(cat);

            Selection.activeGameObject = cat;
            EditorUtility.DisplayDialog("CreatureAI Setup",
                "Cat(体つき)・FoodBowl・Bed(見えるオブジェクト付き)・状態表示を作成しました。\n\n" +
                "▶ Play すると猫が Target まで歩いて到着で停止し、\n" +
                "各ポイント上に Free/Reserved/Occupied が表示されます。\n" +
                "Console に [Target]/[Move arrived] ログが出れば Phase 4 成功です。\n\n" +
                "※ Body / Mesh は差し替え可能(消して好きなモデルを置けます)。", "OK");
        }

        // ================= メニュー 9: 壊れアセット掃除(単体) =================

        [MenuItem("CreatureAI/9. 壊れた Program Asset を掃除", false, 20)]
        public static void CleanupMenu()
        {
            int n = CleanupBrokenProgramAssets(true);
            EditorUtility.DisplayDialog("CreatureAI Setup",
                "ソース未設定の壊れた Program Asset を " + n + " 個削除しました。", "OK");
        }

        // ================= 欲求の速さプリセット(食欲/睡眠を個別に変更) =================
        // シーン内の全 Cat(CreatureProfile)の対象欲求の増加速度を変更する。
        // 食欲だけ速く・睡眠だけ遅く、のように個別調整できる。
        // 再生中なら即反映、停止中なら次の Play から反映。

        private const string HungerVar = "hungerGrowthRate";
        private const string SleepVar = "sleepinessGrowthRate";

        // --- 食欲 ---
        [MenuItem("CreatureAI/欲求の速さ/食欲/のんびり (0.5)", false, 40)]
        public static void HungerRelaxed() { ApplyRate(true, 0.5f, "食欲", "のんびり"); }
        [MenuItem("CreatureAI/欲求の速さ/食欲/ふつう (1.0)", false, 41)]
        public static void HungerNormal() { ApplyRate(true, 1.0f, "食欲", "ふつう"); }
        [MenuItem("CreatureAI/欲求の速さ/食欲/はやい (5)", false, 42)]
        public static void HungerFast() { ApplyRate(true, 5f, "食欲", "はやい"); }
        [MenuItem("CreatureAI/欲求の速さ/食欲/ばくそく (20)", false, 43)]
        public static void HungerTurbo() { ApplyRate(true, 20f, "食欲", "ばくそく"); }

        // --- 睡眠 ---
        [MenuItem("CreatureAI/欲求の速さ/睡眠/のんびり (0.4)", false, 60)]
        public static void SleepRelaxed() { ApplyRate(false, 0.4f, "睡眠", "のんびり"); }
        [MenuItem("CreatureAI/欲求の速さ/睡眠/ふつう (0.8)", false, 61)]
        public static void SleepNormal() { ApplyRate(false, 0.8f, "睡眠", "ふつう"); }
        [MenuItem("CreatureAI/欲求の速さ/睡眠/はやい (4)", false, 62)]
        public static void SleepFast() { ApplyRate(false, 4f, "睡眠", "はやい"); }
        [MenuItem("CreatureAI/欲求の速さ/睡眠/ばくそく (16)", false, 63)]
        public static void SleepTurbo() { ApplyRate(false, 16f, "睡眠", "ばくそく"); }

        /// <summary>
        /// シーン内の全 CreatureProfile の、指定した欲求(食欲 or 睡眠)の増加速度だけを変更する。
        /// isHunger=true なら食欲、false なら睡眠。もう片方の値は変更しない。
        /// </summary>
        private static void ApplyRate(bool isHunger, float rate, string needLabel, string speedLabel)
        {
            CreatureProfile[] profiles = UnityEngine.Object.FindObjectsOfType<CreatureProfile>();
            if (profiles == null || profiles.Length == 0)
            {
                EditorUtility.DisplayDialog("CreatureAI",
                    "シーンに Cat(CreatureProfile)がありません。\n先に『2. テスト用の猫を作成』で猫を用意してください。", "OK");
                return;
            }

            string varName = isHunger ? HungerVar : SleepVar;
            int n = 0;
            foreach (CreatureProfile p in profiles)
            {
                try
                {
                    // ① 編集時の値(次の Play 以降で有効)を、対象の欲求だけ書き換える。
                    if (isHunger) p.hungerGrowthRate = rate;
                    else p.sleepinessGrowthRate = rate;
                    UdonSharpEditorUtility.CopyProxyToUdon(p);
                    EditorUtility.SetDirty(p);

                    // ② 再生中なら、動いている Udon 変数を直接書き換えて即反映する。
                    if (Application.isPlaying)
                    {
                        VRC.Udon.UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
                        if (udon != null) udon.SetProgramVariable(varName, rate);
                    }
                    n++;
                }
                catch (Exception e)
                {
                    Debug.LogError("[CreatureAI] 速度適用に失敗: " + e.Message);
                }
            }

            if (!Application.isPlaying) EditorSceneManager.MarkAllScenesDirty();

            Debug.Log("[CreatureAI] " + needLabel + " の速さ = 「" + speedLabel + "」(" + rate +
                "/秒) を " + n + " 匹に適用");
            EditorUtility.DisplayDialog("CreatureAI",
                needLabel + " の速さ:「" + speedLabel + "」(" + rate + " /秒) を " + n + " 匹に適用しました。\n" +
                "(もう片方の欲求は変更していません)\n\n" +
                (Application.isPlaying ? "再生中なので即反映されます。" : "▶ Play で反映されます。"), "OK");
        }

        // ================= オブジェクト生成ヘルパー =================

        /// <summary>
        /// ポイントを作る。構造は「(スケール無しの)ルート = CreaturePoint」
        /// ＋「見える Mesh 子(差し替え可)」＋「状態表示ラベル子」。
        /// ルートを無スケールにするのは、子の Canvas / メッシュがスケールで歪まないようにするため。
        /// </summary>
        private static void CreatePoint(string name, PointType type, Vector3 pos,
            PrimitiveType prim, Vector3 meshScale, Color color)
        {
            GameObject root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, "Create CreatureAI Point");
            root.transform.position = pos;

            CreaturePoint p = AddUdonSharp<CreaturePoint>(root);
            if (p != null) { p.pointType = type; EditorUtility.SetDirty(p); }

            // 見える本体(不要なら消して好きなモデルを Mesh の代わりにルート下へ置けばよい)。
            AddVisual(root, "Mesh", prim, Vector3.zero, meshScale, color);

            // 頭上の状態表示ラベル。
            BuildPointLabel(root, p);
        }

        /// <summary>プリミティブの見た目を子として付ける(コライダーは除去、色を付ける)。</summary>
        private static GameObject AddVisual(GameObject parent, string name, PrimitiveType prim,
            Vector3 localPos, Vector3 localScale, Color color)
        {
            GameObject vis = GameObject.CreatePrimitive(prim);
            vis.name = name;
            vis.transform.SetParent(parent.transform, false);
            vis.transform.localPosition = localPos;
            vis.transform.localScale = localScale;

            Collider col = vis.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col); // 移動の邪魔・当たり判定を避ける

            SetColor(vis, color);
            return vis;
        }

        /// <summary>ポイントの上に、占有状態を表示する World Space ラベル(＋ビルボード)を作る。</summary>
        private static void BuildPointLabel(GameObject pointRoot, CreaturePoint point)
        {
            GameObject board = new GameObject("StatusLabel", typeof(Canvas), typeof(CanvasScaler));
            board.transform.SetParent(pointRoot.transform, false);
            board.transform.localPosition = new Vector3(0f, 0.9f, 0f);

            Canvas canvas = board.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform crt = (RectTransform)board.transform;
            crt.sizeDelta = new Vector2(300f, 140f);
            crt.localScale = new Vector3(0.004f, 0.004f, 0.004f);

            GameObject bg = new GameObject("BG", typeof(RectTransform));
            bg.transform.SetParent(board.transform, false);
            Image img = bg.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.55f);
            StretchFull((RectTransform)bg.transform, 0f);

            GameObject textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(board.transform, false);
            Text text = textGO.AddComponent<Text>();
            text.font = GetFont();
            text.fontSize = 30;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.text = pointRoot.name;
            StretchFull((RectTransform)textGO.transform, 8f);

            CreaturePointStatusDisplay disp = AddUdonSharp<CreaturePointStatusDisplay>(board);
            if (disp != null)
            {
                disp.targetText = text;
                disp.point = point;
                EditorUtility.SetDirty(disp);
            }
            AddUdonSharp<Billboard>(board); // 常にこちらを向く
        }

        private static void SetColor(GameObject go, Color color)
        {
            Renderer r = go.GetComponent<Renderer>();
            if (r == null) return;
            Shader sh = Shader.Find("Standard");
            Material m = new Material(sh != null ? sh : r.sharedMaterial.shader);
            m.color = color;
            r.sharedMaterial = m;
        }

        private static Font GetFont()
        {
            Font font = (Font)Resources.GetBuiltinResource(typeof(Font), "LegacyRuntime.ttf");
            if (font == null) font = (Font)Resources.GetBuiltinResource(typeof(Font), "Arial.ttf");
            return font;
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
            text.font = GetFont();
            text.fontSize = 22;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.text = "Cat";
            StretchFull((RectTransform)textGO.transform, 14f);

            CreatureStatusDisplay disp = AddUdonSharp<CreatureStatusDisplay>(board);
            if (disp != null)
            {
                disp.targetText = text;
                disp.displayName = "Cat";
                EditorUtility.SetDirty(disp);
            }
            AddUdonSharp<Billboard>(board); // 頭上ボードも常にこちらを向く
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
