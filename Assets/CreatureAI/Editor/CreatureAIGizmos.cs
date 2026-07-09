using System;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace CreatureAI.EditorTools
{
    /// <summary>
    /// Scene ビュー用のデバッグ可視化(Phase 3 の動作確認用)。
    ///
    /// UdonSharp は Play 中、実行値が Udon VM 側にあり C# プロキシのフィールドは
    /// 更新されない。そこで Editor 側から「裏の UdonBehaviour のプログラム変数」を
    /// GetProgramVariable で読み取り、Scene 上に色分け Gizmo とラベルで表示する。
    ///
    ///   CreaturePoint : 状態(Free/Reserved/Occupied 色分け)・種類・評価値・
    ///                   予約者・最寄りの猫までの距離・検索半径
    ///   CreatureCore  : 現在の Goal と判断根拠(最優先 Need とスコア)・
    ///                   選択中 Target・近傍候補数・Target への線
    /// </summary>
    public static class CreatureAIGizmos
    {
        // ---- 状態カラー ----
        private static readonly Color FreeColor = new Color(0.35f, 1f, 0.45f);
        private static readonly Color ReservedColor = new Color(1f, 0.85f, 0.2f);
        private static readonly Color OccupiedColor = new Color(1f, 0.35f, 0.35f);
        private static readonly Color CatColor = new Color(0.4f, 0.7f, 1f);

        // ================= CreaturePoint =================

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
        private static void DrawPoint(CreaturePoint p, GizmoType gizmoType)
        {
            Vector3 pos = p.transform.position;

            int occ = 0;
            string holder = "-";
            if (Application.isPlaying)
            {
                object o = GetVar(p, "occupancy");
                if (o != null) occ = SafeInt(o);
                Component hc = GetVar(p, "holder") as Component;
                if (hc != null) holder = hc.gameObject.name;
            }

            Color col = (occ == 2) ? OccupiedColor : (occ == 1) ? ReservedColor : FreeColor;

            // マーカー + 検索半径
            Gizmos.color = col;
            Gizmos.DrawWireSphere(pos, 0.25f);
            Gizmos.color = new Color(col.r, col.g, col.b, 0.18f);
            Gizmos.DrawWireSphere(pos, Mathf.Max(0.5f, p.searchRadius));

            string state = (occ == 2) ? "Occupied" : (occ == 1) ? "Reserved" : "Free";
            float dist = NearestCatDistance(pos);

            string label = p.name +
                "\n[" + p.pointType + "]  eval=" + p.evaluationValue + (p.isUsable ? "" : "  (使用不可)") +
                "\nstate=" + state + ((occ >= 1) ? ("  by " + holder) : "") +
                ((dist >= 0f) ? ("\ndist=" + dist.ToString("F1") + "m") : "");

            DrawLabel(pos + Vector3.up * 0.35f, label, col);
        }

        // ================= CreatureCore(猫) =================

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
        private static void DrawCat(CreatureCore c, GizmoType gizmoType)
        {
            Vector3 pos = c.transform.position;
            Gizmos.color = CatColor;
            Gizmos.DrawWireSphere(pos, 0.2f);

            string goal = "None";
            string reason = "-";
            string target = "-";
            string agentState = "-";
            string motion = "-";
            int cands = 0;
            Vector3 targetPos = pos;
            bool hasTarget = false;
            string threatLine = "";

            // 危険範囲(threatDistance)の可視化。config 値は編集時も読める。
            ThreatEvaluator te = c.GetComponent<ThreatEvaluator>();
            if (te != null)
            {
                bool threatened = false;
                if (Application.isPlaying)
                {
                    object thv = GetVar(te, "threatened");
                    threatened = (thv != null) && (bool)thv;
                }
                Gizmos.color = threatened ? OccupiedColor : new Color(1f, 0.5f, 0.5f, 0.25f);
                Gizmos.DrawWireSphere(pos, Mathf.Max(0.1f, te.threatDistance));
                threatLine = "\nThreat: " + (threatened ? "YES" : "no") + " (r=" + te.threatDistance + ")";
            }

            if (Application.isPlaying)
            {
                CreatureBrain brain = c.GetComponent<CreatureBrain>();
                CreatureTargetSelector sel = c.GetComponent<CreatureTargetSelector>();
                CreaturePointSensor sen = c.GetComponent<CreaturePointSensor>();
                ActionRunner act = c.GetComponent<ActionRunner>();
                CreatureAnimator anm = c.GetComponent<CreatureAnimator>();

                object stv = GetVar(act, "state");
                if (stv != null) agentState = AgentStateName(SafeInt(stv));

                object mkv = GetVar(anm, "currentKind");
                if (mkv != null) motion = MotionName(SafeInt(mkv));

                object gv = GetVar(brain, "currentGoal");
                if (gv != null) goal = GoalName(SafeInt(gv));

                object bn = GetVar(brain, "lastBestNeed");
                object bs = GetVar(brain, "lastBestScore");
                if (bn != null && bs != null) reason = NeedName(SafeInt(bn)) + " " + SafeFloat(bs).ToString("F1");

                Component tc = GetVar(sel, "targetPoint") as Component;
                if (tc != null) { target = tc.gameObject.name; targetPos = tc.transform.position; hasTarget = true; }

                object cc = GetVar(sen, "candidateCount");
                if (cc != null) cands = SafeInt(cc);
            }

            string label = c.name + "   [" + agentState + " / " + motion + "]" +
                "\nGoal: " + goal + "   (top: " + reason + ")" +
                "\nTarget: " + target +
                "\nCandidates: " + cands +
                threatLine;
            DrawLabel(pos + Vector3.up * 0.6f, label, CatColor);

            if (hasTarget)
            {
                Gizmos.color = ReservedColor;
                Gizmos.DrawLine(pos, targetPos);
            }
        }

        // ================= ヘルパー =================

        private static object GetVar(Component proxy, string name)
        {
            UdonSharpBehaviour usb = proxy as UdonSharpBehaviour;
            if (usb == null) return null;
            VRC.Udon.UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(usb);
            if (udon == null) return null;
            try { return udon.GetProgramVariable(name); }
            catch { return null; }
        }

        private static int SafeInt(object o)
        {
            try { return Convert.ToInt32(o); } catch { return 0; }
        }

        private static float SafeFloat(object o)
        {
            try { return Convert.ToSingle(o); } catch { return 0f; }
        }

        private static float NearestCatDistance(Vector3 pos)
        {
            CreatureCore[] cats = UnityEngine.Object.FindObjectsOfType<CreatureCore>();
            float best = -1f;
            foreach (CreatureCore c in cats)
            {
                float d = Vector3.Distance(pos, c.transform.position);
                if (best < 0f || d < best) best = d;
            }
            return best;
        }

        private static void DrawLabel(Vector3 pos, string text, Color accent)
        {
            GUIStyle st = new GUIStyle(EditorStyles.helpBox);
            st.normal.textColor = Color.white;
            st.fontSize = 11;
            st.richText = true;
            Handles.Label(pos, text, st);
        }

        private static string GoalName(int g)
        {
            switch (g)
            {
                case 1: return "Eat";
                case 2: return "Drink";
                case 3: return "Sleep";
                case 4: return "Play";
                case 5: return "SeekAffection";
                case 6: return "Flee";
                default: return "None";
            }
        }

        private static string AgentStateName(int s)
        {
            switch (s)
            {
                case 1: return "Moving";
                case 2: return "Acting";
                default: return "Idle";
            }
        }

        private static string MotionName(int m)
        {
            switch (m)
            {
                case 1: return "Walk";
                case 2: return "Eat";
                case 3: return "Sleep";
                case 4: return "Flee";
                default: return "Idle";
            }
        }

        private static string NeedName(int n)
        {
            switch (n)
            {
                case 0: return "Hunger";
                case 1: return "Sleepiness";
                case 2: return "Thirst";
                case 3: return "Playfulness";
                case 4: return "Affection";
                default: return "?";
            }
        }
    }
}
