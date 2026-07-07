using System;
using System.Collections.Generic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace CreatureAI.EditorTools
{
    /// <summary>
    /// CreatureAI のセットアップ自動化メニュー。
    ///
    /// [背景]
    /// UdonSharp は「.cs ファイルを入れるだけ」では動かない。スクリプト1つにつき
    /// UdonSharpProgramAsset(実行データ)が必要で、これが無いと
    /// 「Unable to find valid U# program asset associated with script ...」となり、
    /// コンポーネントの Inspector も正しく表示されない。
    /// このツールは Program Asset の生成と、テスト用シーンオブジェクトの組み立てを
    /// メニュー2クリックで完了させる。
    /// </summary>
    public static class CreatureAISetup
    {
        // Program Asset が必要な UdonSharpBehaviour 一覧(Phase 1)。
        private static readonly Type[] BehaviourTypes =
        {
            typeof(CreatureCore),
            typeof(CreaturePoint),
            typeof(CreaturePointRegistry),
            typeof(CreaturePointSensor),
            typeof(NeedsController),
            typeof(CreatureProfile),
        };

        private const string ProgramAssetFolder = "Assets/CreatureAI/ProgramAssets";

        // ================= メニュー 1: Program Asset 生成 =================

        [MenuItem("CreatureAI/1. Program Asset を作成 (最初に1回)", false, 1)]
        public static void SetupProgramAssets()
        {
            HashSet<Type> existing = CollectTypesWithProgramAsset();

            if (!AssetDatabase.IsValidFolder(ProgramAssetFolder))
            {
                AssetDatabase.CreateFolder("Assets/CreatureAI", "ProgramAssets");
            }

            int created = 0;
            foreach (Type t in BehaviourTypes)
            {
                if (existing.Contains(t)) continue; // 二重生成の防止(再実行しても安全)

                MonoScript script = FindScript(t);
                if (script == null)
                {
                    Debug.LogError("[CreatureAI Setup] スクリプトが見つかりません: " + t.Name);
                    continue;
                }

                UdonSharpProgramAsset programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                programAsset.sourceCsScript = script;
                AssetDatabase.CreateAsset(programAsset, ProgramAssetFolder + "/" + t.Name + ".asset");
                created++;
                Debug.Log("[CreatureAI Setup] Program Asset を作成: " + t.Name);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 生成した Program Asset をコンパイルして実行データを作る。
            UdonSharpProgramAsset.CompileAllCsPrograms(true);

            EditorUtility.DisplayDialog("CreatureAI Setup",
                "Program Asset: 新規 " + created + " 個 / 既存 " + existing.Count + " 個\n\n" +
                "U# のコンパイルが数秒走ります。Console にエラーが出ていなければ、\n" +
                "メニュー『CreatureAI > 2. テスト用 Cat と FoodBowl を作成』へ進んでください。",
                "OK");
        }

        // ================= メニュー 2: テスト用オブジェクト組み立て =================

        [MenuItem("CreatureAI/2. テスト用 Cat と FoodBowl を作成", false, 2)]
        public static void CreateTestSceneObjects()
        {
            // Program Asset が揃っているか事前チェック。
            HashSet<Type> existing = CollectTypesWithProgramAsset();
            foreach (Type t in BehaviourTypes)
            {
                if (!existing.Contains(t))
                {
                    EditorUtility.DisplayDialog("CreatureAI Setup",
                        t.Name + " の Program Asset がありません。\n" +
                        "先にメニュー『CreatureAI > 1. Program Asset を作成』を実行してください。",
                        "OK");
                    return;
                }
            }

            // 古い残骸との二重生成を防止。
            if (GameObject.Find("Cat") != null || GameObject.Find("__CatAI_Registry") != null)
            {
                EditorUtility.DisplayDialog("CreatureAI Setup",
                    "シーンに既に Cat または __CatAI_Registry が存在します。\n" +
                    "以前の(壊れている可能性のある)ものを Hierarchy から削除してから、もう一度実行してください。",
                    "OK");
                return;
            }

            // --- Cat 本体 ---
            GameObject cat = new GameObject("Cat");
            Undo.RegisterCreatedObjectUndo(cat, "Create CreatureAI Cat");
            AddUdonSharp<CreatureCore>(cat);
            AddUdonSharp<NeedsController>(cat);
            AddUdonSharp<CreaturePointSensor>(cat);

            // --- 子: Profile ---
            GameObject profile = new GameObject("Profile");
            profile.transform.SetParent(cat.transform, false);
            AddUdonSharp<CreatureProfile>(profile);

            // --- 子: __CatAI_Registry (名前が Find のキーなので変更不可) ---
            GameObject registry = new GameObject("__CatAI_Registry");
            registry.transform.SetParent(cat.transform, false);
            AddUdonSharp<CreaturePointRegistry>(registry);

            // --- FoodBowl (ワールド側のテスト用ポイント) ---
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
                "▶ Play を押して、Console に以下が出れば Phase 1 成功です:\n" +
                "・Singleton に選出されました\n" +
                "・登録: FoodBowl (合計 1 個)\n" +
                "・近傍候補: 1 個",
                "OK");
        }

        // ================= 内部ヘルパー =================

        /// <summary>UdonSharp コンポーネントを追加し、裏側の UdonBehaviour も生成する。</summary>
        private static T AddUdonSharp<T>(GameObject go) where T : UdonSharpBehaviour
        {
            T proxy = Undo.AddComponent<T>(go);
            try
            {
                UdonSharpEditorUtility.CreateBehaviourForProxy(proxy);
            }
            catch (Exception e)
            {
                Debug.LogError("[CreatureAI Setup] " + typeof(T).Name + " のセットアップに失敗: " + e.Message +
                    "\nU# のコンパイル直後の場合は、完了を待ってからオブジェクトを削除し、メニュー 2 を再実行してください。");
            }
            return proxy;
        }

        /// <summary>既存の全 Program Asset を走査し、ソーススクリプトのクラス集合を返す。</summary>
        private static HashSet<Type> CollectTypesWithProgramAsset()
        {
            HashSet<Type> result = new HashSet<Type>();
            foreach (string guid in AssetDatabase.FindAssets("t:UdonSharpProgramAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UdonSharpProgramAsset asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
                if (asset != null && asset.sourceCsScript != null)
                {
                    Type cls = asset.sourceCsScript.GetClass();
                    if (cls != null) result.Add(cls);
                }
            }
            return result;
        }

        /// <summary>クラスに対応する MonoScript(.cs アセット)を検索する。</summary>
        private static MonoScript FindScript(Type t)
        {
            foreach (string guid in AssetDatabase.FindAssets(t.Name + " t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == t) return script;
            }
            return null;
        }
    }
}
