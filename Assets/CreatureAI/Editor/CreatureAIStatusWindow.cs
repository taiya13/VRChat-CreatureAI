using System;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CreatureAI.EditorTools
{
    /// <summary>
    /// Need ごとのパラメータ(Increase / Decrease / Threshold / Weight)を表で編集する専用ウィンドウ。
    /// メニュー「CreatureAI > ステータス設定」で開く。
    ///
    /// CreatureProfile の配列(increaseRate/decreaseRate/threshold/weight)を読み書きする。
    /// NeedType に列挙を足せば行も自動で増える(Water/Fun/Social 等の追加に追従)。
    /// 「適用」で保存し、再生中は Udon 変数へ直接反映する。
    /// </summary>
    public class CreatureAIStatusWindow : EditorWindow
    {
        private CreatureProfile target;
        private bool applyToAll = true;

        private float[] inc, dec, thr, wgt;
        private float moveSpeed, reserveTimeout;
        private CreatureProfile loadedFrom;
        private Vector2 scroll;

        [MenuItem("CreatureAI/ステータス設定 (Need パラメータ編集)", false, 10)]
        public static void Open()
        {
            CreatureAIStatusWindow w = GetWindow<CreatureAIStatusWindow>("Creature ステータス");
            w.minSize = new Vector2(460f, 320f);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Need ごとのパラメータ設定", EditorStyles.boldLabel);

            CreatureProfile picked = (CreatureProfile)EditorGUILayout.ObjectField(
                "対象 Profile", target, typeof(CreatureProfile), true);
            if (picked != target) { target = picked; LoadFrom(target); }

            if (target == null)
            {
                if (GUILayout.Button("シーンから Profile を自動取得"))
                {
                    target = FindFirstProfile();
                    LoadFrom(target);
                }
                EditorGUILayout.HelpBox(
                    "編集する CreatureProfile を指定するか、上のボタンでシーンから取得してください。\n" +
                    "(Cat の子 'Profile' に付いています)", MessageType.Info);
                return;
            }

            if (loadedFrom != target || inc == null) LoadFrom(target);

            applyToAll = EditorGUILayout.Toggle(
                new GUIContent("全 Cat に適用", "シーン内の全 CreatureProfile へ同じ値を適用する"), applyToAll);

            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll);

            // ヘッダ
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Need", EditorStyles.miniBoldLabel, GUILayout.Width(120));
            GUILayout.Label("Increase", EditorStyles.miniBoldLabel, GUILayout.Width(70));
            GUILayout.Label("Decrease", EditorStyles.miniBoldLabel, GUILayout.Width(70));
            GUILayout.Label("Threshold", EditorStyles.miniBoldLabel, GUILayout.Width(70));
            GUILayout.Label("Weight", EditorStyles.miniBoldLabel, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();

            string[] names = Enum.GetNames(typeof(NeedType));
            int count = names.Length;
            EnsureLen(count);

            for (int i = 0; i < count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(names[i], GUILayout.Width(120));
                inc[i] = EditorGUILayout.FloatField(inc[i], GUILayout.Width(70));
                dec[i] = EditorGUILayout.FloatField(dec[i], GUILayout.Width(70));
                thr[i] = EditorGUILayout.FloatField(thr[i], GUILayout.Width(70));
                wgt[i] = EditorGUILayout.FloatField(wgt[i], GUILayout.Width(70));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("移動・占有", EditorStyles.miniBoldLabel);
            moveSpeed = EditorGUILayout.FloatField("Move Speed", moveSpeed);
            reserveTimeout = EditorGUILayout.FloatField("Reserve Timeout", reserveTimeout);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("クイックプリセット(Increase/Decrease をまとめて設定)", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("観察向け (ゆっくり)")) { PresetSpeed(0.5f); }
            if (GUILayout.Button("ふつう")) { PresetSpeed(1.0f); }
            if (GUILayout.Button("テスト向け (はやい)")) { PresetSpeed(6.0f); }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
            if (GUILayout.Button("適用 (Apply)", GUILayout.Height(30)))
            {
                Apply();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.HelpBox(
                "Increase=増加速度 / Decrease=行動中の回復速度 / Threshold=行動開始の基準 / Weight=優先度の重み。\n" +
                "再生中に適用すると即反映されます。", MessageType.None);
        }

        // ================= 読み書き =================

        private void LoadFrom(CreatureProfile p)
        {
            loadedFrom = p;
            if (p == null) return;
            int count = Enum.GetNames(typeof(NeedType)).Length;
            inc = Resize(p.increaseRate, count, 0f);
            dec = Resize(p.decreaseRate, count, 20f);
            thr = Resize(p.threshold, count, 40f);
            wgt = Resize(p.weight, count, 1f);
            moveSpeed = p.moveSpeed;
            reserveTimeout = p.reserveTimeout;
        }

        private void Apply()
        {
            int applied = 0;
            if (applyToAll)
            {
                foreach (CreatureProfile p in UnityEngine.Object.FindObjectsOfType<CreatureProfile>())
                {
                    WriteTo(p);
                    applied++;
                }
            }
            else
            {
                WriteTo(target);
                applied = 1;
            }

            if (!Application.isPlaying) EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[CreatureAI] ステータス設定を " + applied + " 匹へ適用しました。");
        }

        private void WriteTo(CreatureProfile p)
        {
            if (p == null) return;
            try
            {
                p.increaseRate = (float[])inc.Clone();
                p.decreaseRate = (float[])dec.Clone();
                p.threshold = (float[])thr.Clone();
                p.weight = (float[])wgt.Clone();
                p.moveSpeed = moveSpeed;
                p.reserveTimeout = reserveTimeout;

                UdonSharpEditorUtility.CopyProxyToUdon(p);
                EditorUtility.SetDirty(p);

                if (Application.isPlaying)
                {
                    VRC.Udon.UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
                    if (udon != null)
                    {
                        udon.SetProgramVariable("increaseRate", p.increaseRate);
                        udon.SetProgramVariable("decreaseRate", p.decreaseRate);
                        udon.SetProgramVariable("threshold", p.threshold);
                        udon.SetProgramVariable("weight", p.weight);
                        udon.SetProgramVariable("moveSpeed", p.moveSpeed);
                        udon.SetProgramVariable("reserveTimeout", p.reserveTimeout);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[CreatureAI] 適用に失敗(" + p.name + "): " + e.Message);
            }
        }

        // ================= ヘルパー =================

        private void PresetSpeed(float baseRate)
        {
            // 既に increase が設定されている Need(=有効な Need)だけ速度を掛け替える。
            // 何も無ければ Hunger/Sleepiness を基準にする。
            bool any = false;
            for (int i = 0; i < inc.Length; i++) if (inc[i] > 0f) any = true;

            for (int i = 0; i < inc.Length; i++)
            {
                bool active = any ? (inc[i] > 0f) : (i == 0 || i == 1);
                if (active)
                {
                    inc[i] = baseRate * (i == 1 ? 0.8f : 1.0f); // 睡眠は少しだけ遅く
                    dec[i] = baseRate * 4f;
                }
            }
        }

        private void EnsureLen(int count)
        {
            inc = Resize(inc, count, 0f);
            dec = Resize(dec, count, 20f);
            thr = Resize(thr, count, 40f);
            wgt = Resize(wgt, count, 1f);
        }

        private static float[] Resize(float[] src, int count, float fallback)
        {
            float[] r = new float[count];
            for (int i = 0; i < count; i++)
                r[i] = (src != null && i < src.Length) ? src[i] : fallback;
            return r;
        }

        private static CreatureProfile FindFirstProfile()
        {
            CreatureProfile[] all = UnityEngine.Object.FindObjectsOfType<CreatureProfile>();
            return (all != null && all.Length > 0) ? all[0] : null;
        }
    }
}
