using System;
using System.Reflection;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Animations;
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
            typeof(ActionRunner),
            typeof(ThreatEvaluator),
            typeof(CreatureAnimator),
            typeof(CreatureActionCatalog),
            typeof(CreaturePersonality),
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
            ResetUdonSharpCaches(); // コンパイル後にキャッシュを作り直しておく

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
            // 壊れアセットを掃除(辞書汚染で AddComponent が「Value cannot be null. key」で
            // 落ちるのを防ぐ)。削除したものはログに出す。
            int cleaned = CleanupBrokenProgramAssets(true);
            if (cleaned > 0)
                Debug.Log("[CreatureAI Setup] 組み立て前に壊れ Program Asset を " + cleaned + " 個掃除しました。");

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

            // 直前に UdonSharp の型ルックアップキャッシュを作り直す。
            // 新規スクリプトを足したフェーズで「Value cannot be null. key」が出るのは、
            // 新アセットが未登録の古いキャッシュを使ってしまうため。ここでリセットすれば根絶できる。
            ResetUdonSharpCaches();

            // 複数匹対応: 既存の猫の数だけ名前と位置をずらして作る(重ならないように)。
            int existingCats = UnityEngine.Object.FindObjectsOfType<CreatureCore>().Length;
            string catName = (existingCats == 0) ? "Cat" : ("Cat " + (existingCats + 1));
            Vector3 catPos = new Vector3(existingCats * 2.5f, 0f, 0f);

            // --- Cat 本体 ---
            GameObject cat = new GameObject(catName);
            cat.transform.position = catPos;
            Undo.RegisterCreatedObjectUndo(cat, "Create CreatureAI Cat");

            AddUdonSharp<CreatureCore>(cat);
            AddUdonSharp<NeedsController>(cat);
            AddUdonSharp<NeedsData>(cat);
            AddUdonSharp<CreatureBrain>(cat);
            AddUdonSharp<CreatureTargetSelector>(cat);
            AddUdonSharp<MovementController>(cat);
            AddUdonSharp<ActionRunner>(cat);
            AddUdonSharp<ThreatEvaluator>(cat);
            CreatureAnimator catAnimator = AddUdonSharp<CreatureAnimator>(cat);
            AddUdonSharp<CreatureActionCatalog>(cat); // Goal⇔Need⇔Point⇔Motion⇔Name の対応表
            AddUdonSharp<CreaturePersonality>(cat);
            AddUdonSharp<CreaturePointSensor>(cat);

            // 見える体(差し替え可能: この Body を消して好きなモデルを Cat の子に置けばよい)。
            AddVisual(cat, "Body", PrimitiveType.Capsule,
                new Vector3(0f, 0.35f, 0f), new Vector3(0.35f, 0.35f, 0.35f),
                new Color(0.95f, 0.6f, 0.2f));

            // Animator と自動生成の AnimatorController を用意して割り当てる。
            AnimatorController controller = EnsureAnimatorController();
            Animator anim = Undo.AddComponent<Animator>(cat);
            anim.applyRootMotion = false;
            if (controller != null) anim.runtimeAnimatorController = controller;
            if (catAnimator != null)
            {
                catAnimator.animator = anim;
                EditorUtility.SetDirty(catAnimator);
            }

            GameObject profile = new GameObject("Profile");
            profile.transform.SetParent(cat.transform, false);
            AddUdonSharp<CreatureProfile>(profile);

            GameObject registry = new GameObject("__CatAI_Registry");
            registry.transform.SetParent(cat.transform, false);
            AddUdonSharp<CreaturePointRegistry>(registry);

            // --- ワールド側ポイント(全猫で共有。無ければ作る) ---
            // 餌・水・ベッド・爪とぎ。制作中のアセット(FoodBowl/WaterBowl/Bed/ScratchSpot)の
            // 置き場所を、この仮メッシュごと差し替えれば使える。PointType を Inspector で選ぶだけで
            // どのアセットも「利用可能な地点」になる(コード変更不要)。
            if (GameObject.Find("FoodBowl") == null)
                CreatePoint("FoodBowl", PointType.Food, new Vector3(3f, 0f, 0f),
                    PrimitiveType.Cylinder, new Vector3(0.5f, 0.08f, 0.5f), new Color(0.7f, 0.45f, 0.2f));
            if (GameObject.Find("WaterBowl") == null)
                CreatePoint("WaterBowl", PointType.Water, new Vector3(3f, 0f, 1.5f),
                    PrimitiveType.Cylinder, new Vector3(0.5f, 0.06f, 0.5f), new Color(0.3f, 0.6f, 0.9f));
            if (GameObject.Find("Bed") == null)
                CreatePoint("Bed", PointType.Bed, new Vector3(-3f, 0f, 1.5f),
                    PrimitiveType.Cube, new Vector3(1.0f, 0.15f, 1.3f), new Color(0.35f, 0.5f, 0.85f));
            if (GameObject.Find("ScratchSpot") == null)
                CreatePoint("ScratchSpot", PointType.ScratchPost, new Vector3(-3f, 0f, -1.5f),
                    PrimitiveType.Cylinder, new Vector3(0.28f, 0.6f, 0.28f), new Color(0.55f, 0.4f, 0.25f));

            // --- 頭上の状態表示ボード ---
            BuildStatusBoard(cat);

            Selection.activeGameObject = cat;
            EditorUtility.DisplayDialog("CreatureAI Setup",
                "「" + catName + "」を作成しました(既存の猫: " + existingCats + " 匹)。\n\n" +
                "もう一度このメニューを押すと、位置をずらして2匹目・3匹目を追加できます。\n" +
                "餌・ベッドは全猫で共有され、占有(予約)で取り合いになりません。\n\n" +
                "▶ Play で動作を確認してください。※ Body / Mesh は差し替え可能です。", "OK");
        }

        // ================= メニュー 9: 壊れアセット掃除(単体) =================

        [MenuItem("CreatureAI/9. 壊れた Program Asset を掃除", false, 20)]
        public static void CleanupMenu()
        {
            int n = CleanupBrokenProgramAssets(true);
            EditorUtility.DisplayDialog("CreatureAI Setup",
                "ソース未設定の壊れた Program Asset を " + n + " 個削除しました。", "OK");
        }


        // ================= 性格プリセット =================
        // シーン内の全 Cat の性格(CreaturePersonality)をまとめて設定する。
        // 数値: 臆病さ / 好奇心 / 活発さ / のんびりさ(各 0〜1)。

        [MenuItem("CreatureAI/性格/平均 (ふつう)", false, 100)]
        public static void PersoAverage() { ApplyPersonality(0.5f, 0.5f, 0.5f, 0.5f, "平均"); }

        [MenuItem("CreatureAI/性格/臆病な猫", false, 101)]
        public static void PersoTimid() { ApplyPersonality(0.9f, 0.2f, 0.4f, 0.5f, "臆病"); }

        [MenuItem("CreatureAI/性格/元気な猫 (活発)", false, 102)]
        public static void PersoActive() { ApplyPersonality(0.3f, 0.6f, 0.9f, 0.2f, "元気"); }

        [MenuItem("CreatureAI/性格/のんびり猫", false, 103)]
        public static void PersoRelaxed() { ApplyPersonality(0.4f, 0.3f, 0.2f, 0.9f, "のんびり"); }

        [MenuItem("CreatureAI/性格/好奇心旺盛な猫", false, 104)]
        public static void PersoCurious() { ApplyPersonality(0.3f, 0.9f, 0.6f, 0.3f, "好奇心旺盛"); }

        /// <summary>全 CreaturePersonality に性格値を適用する。再生中は即反映。</summary>
        private static void ApplyPersonality(float timid, float curious, float active, float relax, string label)
        {
            CreaturePersonality[] arr = UnityEngine.Object.FindObjectsOfType<CreaturePersonality>();
            if (arr == null || arr.Length == 0)
            {
                EditorUtility.DisplayDialog("CreatureAI",
                    "シーンに Cat(CreaturePersonality)がありません。\n先に『2. テスト用の猫を作成』で猫を用意してください。", "OK");
                return;
            }

            int n = 0;
            foreach (CreaturePersonality p in arr)
            {
                try
                {
                    p.timidity = timid; p.curiosity = curious; p.activeness = active; p.relaxedness = relax;
                    UdonSharpEditorUtility.CopyProxyToUdon(p);
                    EditorUtility.SetDirty(p);
                    if (Application.isPlaying)
                    {
                        VRC.Udon.UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
                        if (udon != null)
                        {
                            udon.SetProgramVariable("timidity", timid);
                            udon.SetProgramVariable("curiosity", curious);
                            udon.SetProgramVariable("activeness", active);
                            udon.SetProgramVariable("relaxedness", relax);
                        }
                    }
                    n++;
                }
                catch (Exception e) { Debug.LogError("[CreatureAI] 性格適用に失敗: " + e.Message); }
            }

            Debug.Log("[CreatureAI] 性格 = 「" + label + "」(臆病" + timid + " 好奇" + curious +
                " 活発" + active + " のんびり" + relax + ") を " + n + " 匹に適用");
            EditorUtility.DisplayDialog("CreatureAI",
                "性格:「" + label + "」を " + n + " 匹に適用しました。\n" +
                "臆病" + timid + " / 好奇心" + curious + " / 活発" + active + " / のんびり" + relax + "\n\n" +
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

        // ================= AnimatorController の自動生成 =================

        private const string GenFolder = "Assets/CreatureAI_Generated";
        private const string ControllerPath = "Assets/CreatureAI_Generated/CreatureAnimator.controller";

        /// <summary>
        /// MotionState(整数パラメータ)+ AnyState 遷移 の AnimatorController を用意する。
        /// 状態は下の「モーション対応表」1 箇所で定義する。新しいモーションを足すときは、
        /// MotionKind に値を足して(enum)、この表に 1 行足すだけでよい(値=MotionState の整数)。
        /// 各状態には「動いて見える」プレースホルダのモーションを入れておく(差し替え前提)。
        /// 既に Controller があれば再利用する(ユーザーの編集を壊さない。作り直したいときは
        /// Assets/CreatureAI_Generated/CreatureAnimator.controller を削除してから実行)。
        /// </summary>
        private static AnimatorController EnsureAnimatorController()
        {
            try
            {
                if (!AssetDatabase.IsValidFolder(GenFolder))
                    AssetDatabase.CreateFolder("Assets", "CreatureAI_Generated");

                AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                if (existing != null) return existing; // 再利用

                AnimatorController ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
                ac.AddParameter("MotionState", AnimatorControllerParameterType.Int);
                AnimatorStateMachine sm = ac.layers[0].stateMachine;

                // ===== モーション対応表(唯一の定義箇所) =====
                // ここに { 状態名, MotionState 値, プレースホルダ Motion } を 1 行足すだけで、
                // 新しいモーションが Animator に載る。値は MotionKind の値と一致させること。
                int row = 0;
                AnimatorState idle =
                BuildMotionState(sm, "Idle", (int)MotionKind.Idle,
                    MakeClip("Idle_ph", "localPosition.y", Bob(2.0f, 0.35f, 0.37f), true), row++);
                BuildMotionState(sm, "Walk", (int)MotionKind.Walk,
                    MakeClip("Walk_ph", "localPosition.y", Bob(0.4f, 0.33f, 0.46f), true), row++);
                BuildMotionState(sm, "Eat", (int)MotionKind.Eat,
                    MakeClip("Eat_ph", "localPosition.y", Const(0.20f), true), row++);
                BuildMotionState(sm, "Sleep", (int)MotionKind.Sleep,
                    MakeClip2("Sleep_ph", "localPosition.y", Const(0.18f), "localScale.y", Const(0.18f), true), row++);
                BuildMotionState(sm, "Flee", (int)MotionKind.Flee,
                    MakeClip("Flee_ph", "localPosition.x", Shake(0.18f, 0f, 0.06f), true), row++);
                BuildMotionState(sm, "Drink", (int)MotionKind.Drink,
                    MakeClip("Drink_ph", "localPosition.y", Const(0.16f), true), row++);
                BuildMotionState(sm, "Play", (int)MotionKind.Play,
                    MakeClip("Play_ph", "localPosition.y", Bob(0.5f, 0.32f, 0.55f), true), row++);
                BuildMotionState(sm, "Scratch", (int)MotionKind.Scratch,
                    MakeClip("Scratch_ph", "localPosition.x", Shake(0.14f, 0f, 0.04f), true), row++);
                BuildMotionState(sm, "Groom", (int)MotionKind.Groom,
                    MakeClip("Groom_ph", "localPosition.y", Bob(0.9f, 0.33f, 0.38f), true), row++);
                BuildMotionState(sm, "Stretch", (int)MotionKind.Stretch,
                    MakeClip2("Stretch_ph", "localScale.z", Const(1.35f), "localPosition.y", Const(0.30f), true), row++);

                sm.defaultState = idle;

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return ac;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CreatureAI Setup] AnimatorController の自動生成に失敗(手動で割り当ててください): " + e.Message);
                return null;
            }
        }

        /// <summary>1つのモーション状態を作り、AnyState→この状態(MotionState==value)の遷移も張る。</summary>
        private static AnimatorState BuildMotionState(AnimatorStateMachine sm, string name, int value, Motion clip, int row)
        {
            AnimatorState s = AddAnimState(sm, name, clip, new Vector3(300, row * 55, 0));
            AddAnyTransition(sm, s, value);
            return s;
        }

        private static AnimatorState AddAnimState(AnimatorStateMachine sm, string name, Motion clip, Vector3 pos)
        {
            AnimatorState s = sm.AddState(name, pos);
            s.motion = clip;
            s.writeDefaultValues = true; // アニメしない項目は既定値に戻す(状態間で干渉しない)
            return s;
        }

        private static void AddAnyTransition(AnimatorStateMachine sm, AnimatorState to, int value)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(to);
            t.AddCondition(AnimatorConditionMode.Equals, value, "MotionState");
            t.hasExitTime = false;
            t.duration = 0.12f;
            t.canTransitionToSelf = false;
        }

        private static AnimationClip MakeClip(string name, string prop, AnimationCurve curve, bool loop)
        {
            AnimationClip c = new AnimationClip();
            c.SetCurve("Body", typeof(Transform), prop, curve);
            SetLoop(c, loop);
            AssetDatabase.CreateAsset(c, GenFolder + "/" + name + ".anim");
            return c;
        }

        private static AnimationClip MakeClip2(string name, string p1, AnimationCurve c1, string p2, AnimationCurve c2, bool loop)
        {
            AnimationClip c = new AnimationClip();
            c.SetCurve("Body", typeof(Transform), p1, c1);
            c.SetCurve("Body", typeof(Transform), p2, c2);
            SetLoop(c, loop);
            AssetDatabase.CreateAsset(c, GenFolder + "/" + name + ".anim");
            return c;
        }

        private static void SetLoop(AnimationClip c, bool loop)
        {
            AnimationClipSettings s = AnimationUtility.GetAnimationClipSettings(c);
            s.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(c, s);
        }

        private static AnimationCurve Const(float v)
        {
            return new AnimationCurve(new Keyframe(0f, v), new Keyframe(0.5f, v));
        }

        private static AnimationCurve Bob(float period, float lo, float hi)
        {
            return new AnimationCurve(
                new Keyframe(0f, lo), new Keyframe(period * 0.5f, hi), new Keyframe(period, lo));
        }

        private static AnimationCurve Shake(float period, float baseV, float amp)
        {
            return new AnimationCurve(
                new Keyframe(0f, baseV),
                new Keyframe(period * 0.25f, baseV + amp),
                new Keyframe(period * 0.5f, baseV),
                new Keyframe(period * 0.75f, baseV - amp),
                new Keyframe(period, baseV));
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
                    "\n→ メニュー1を実行してコンパイル完了を待ってから、もう一度お試しください。");
            }
            return proxy;
        }

        /// <summary>
        /// UdonSharp の内部キャッシュ(クラス→ProgramAsset 辞書)をリフレクションで作り直す。
        /// 新規スクリプトを足したフェーズで CreateBehaviourForProxy が古い辞書を使い、
        /// 新アセットを見つけられず「Value cannot be null. key」になるのを防ぐ。
        /// internal メソッドのため反射で呼ぶ。存在しない/失敗しても安全にスキップする。
        /// </summary>
        private static void ResetUdonSharpCaches()
        {
            try
            {
                MethodInfo m = typeof(UdonSharpEditorUtility).GetMethod(
                    "ResetCaches", BindingFlags.NonPublic | BindingFlags.Static);
                if (m != null) m.Invoke(null, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CreatureAI Setup] キャッシュリセットをスキップ: " + e.Message);
            }
        }

        // ================= Program Asset ヘルパー =================

        /// <summary>
        /// 壊れた ProgramAsset を削除して数を返す。
        /// 「ソース未設定(sourceCsScript=None)」だけでなく「ソースはあるがクラス解決が null」
        /// も対象にする。どちらも UdonSharp の内部辞書(クラス→アセット)を null キーで
        /// 汚染し、以後の全 AddComponent を「Value cannot be null. key」で失敗させるため。
        /// </summary>
        private static int CleanupBrokenProgramAssets(bool log)
        {
            int removed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:UdonSharpProgramAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UdonSharpProgramAsset pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
                if (pa == null) continue;

                bool broken = (pa.sourceCsScript == null) || (pa.sourceCsScript.GetClass() == null);
                if (broken)
                {
                    if (log) Debug.LogWarning("[CreatureAI Setup] 壊れた Program Asset を削除: " + path +
                        (pa.sourceCsScript == null ? " (source=None)" : " (class=null)"));
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
                if (pa != null && (pa.sourceCsScript == null || pa.sourceCsScript.GetClass() == null))
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
