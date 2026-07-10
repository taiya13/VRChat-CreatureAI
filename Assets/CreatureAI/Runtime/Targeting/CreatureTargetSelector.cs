using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// Layer 3→4 の橋渡し: Brain が決めた CurrentGoal に対応する CreaturePoint を探し、
    /// 利用可能な最適候補を Reserve して TargetPoint として保持する。
    ///
    /// [責務] Goal → 必要な PointType へ変換 → Sensor 経由で最良候補を検索 →
    ///        Reserve(占有先取り) → TargetPoint 保持 → 変化時ログ。
    ///        移動・アニメ・行動実行は持たない(後フェーズの ActionRunner)。
    ///
    /// [占有] 予約は「Goal が定まった時点」で行う(仕様 3.5「予約は Goal 決定と同時」)。
    ///        holder には暫定的に自分(Selector)を入れる。ActionRunner 実装後は
    ///        holder を ActionRunner へ移し、解放経路を AbortCurrent() に集約する。
    ///
    /// [再選択の抑制] 同じ Goal 用の有効なターゲットを保持している間は再検索しない
    ///        (毎 Tick 予約し直して他個体と取り合う事故を防ぐ)。
    /// </summary>
    public class CreatureTargetSelector : UdonSharpBehaviour
    {
        private CreatureBrain brain;
        private CreaturePointSensor sensor;
        private CreatureActionCatalog catalog; // Goal→PointType / 表示名 の唯一の対応表

        private CreaturePoint targetPoint;
        private Goal reservedForGoal = Goal.None;

        // ログ抑制(状態が変わった時だけ出す)。
        private Goal lastLogGoal = (Goal)(-1);
        private CreaturePoint lastLogTarget = null;
        private bool debugLog = true;

        public void Initialize(CreatureBrain creatureBrain, CreaturePointSensor pointSensor, CreatureActionCatalog actionCatalog)
        {
            brain = creatureBrain;
            sensor = pointSensor;
            catalog = actionCatalog;
            CreatureCore core = GetComponent<CreatureCore>();
            if (core != null) debugLog = core.debugLog;
        }

        /// <summary>
        /// CurrentGoal に対応する TargetPoint を選び直す(低頻度 Tick から Brain.Evaluate の後に呼ぶ)。
        /// </summary>
        public void SelectTarget()
        {
            if (brain == null || sensor == null) return;

            Goal goal = brain.GetCurrentGoal();

            // 既に同じ Goal 用の有効なターゲットを保持しているなら維持(再検索しない)。
            if (targetPoint != null && reservedForGoal == goal &&
                targetPoint.isUsable && targetPoint.IsHeldBy(this))
            {
                return;
            }

            // Goal が変わった / ターゲット未保持 / 無効化 → 取り直し。
            ReleaseTarget();

            PointType need = (goal == Goal.None || catalog == null) ? PointType.None : catalog.PointTypeForGoal(goal);
            if (need != PointType.None)
            {
                CreaturePoint best = sensor.FindBest(need); // Free の候補のみ返る
                if (best != null && best.Reserve(this))
                {
                    targetPoint = best;
                    reservedForGoal = goal;
                }
            }

            ReportChange(goal, targetPoint);
        }

        /// <summary>予約を解放してターゲットを空にする(全解放経路はここを通す)。</summary>
        public void ReleaseTarget()
        {
            if (targetPoint != null && targetPoint.IsHeldBy(this))
            {
                targetPoint.Release();
            }
            targetPoint = null;
            reservedForGoal = Goal.None;
        }

        // ================= 参照 =================

        public CreaturePoint GetTargetPoint() { return targetPoint; }
        public bool HasTarget() { return targetPoint != null; }
        public string GetTargetPointName() { return targetPoint != null ? targetPoint.name : "-"; }

        // ================= ヘルパー =================

        private void ReportChange(Goal goal, CreaturePoint tp)
        {
            if (goal == lastLogGoal && tp == lastLogTarget) return;
            lastLogGoal = goal;
            lastLogTarget = tp;

            string goalName = (catalog != null) ? catalog.GoalName(goal) : "?";
            if (tp != null)
                if (debugLog) Debug.Log("[Target] " + name + " reserved '" + tp.name + "' for goal " + goalName);
            // Flee は地点を使わないので「ポイント無し」は正常。ログしない。
            else if (goal != Goal.None && goal != Goal.Flee)
                if (debugLog) Debug.Log("[Target] " + name + " goal=" + goalName + " だが利用可能なポイントが無い");
        }

        // Goal→PointType / GoalName の対応は CreatureActionCatalog に一本化した(唯一の定義)。
    }
}
