using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// Layer 4(行動層): TargetPoint に到着したら Goal に応じた Action(Eat/Sleep 等)を実行する
    /// (仕様 3.2 ActionRunner の簡易版)。今は「対象の欲求を時間で回復させる」だけ。
    /// アニメ・移動は持たない(移動は MovementController、意思決定は Brain)。
    ///
    /// [ループ]
    ///   到着(MovementController.IsAtTarget) かつ Goal あり
    ///     → 到着した瞬間に CreaturePoint.Occupy()(Reserved→Occupied)
    ///     → 毎 Tick、対応する Need を actionRecoverRate で回復(NeedsController.Satisfy)
    ///   Brain 側のヒステリシスで、その Need が満たされると Goal が None になる
    ///     → 行動条件が崩れるので Action を終了(ログ)。占有の解放は TargetSelector が
    ///       Goal 変更を検知して行う(解放経路は Release に一本化)。
    ///
    /// [接続] 到着 → Occupy → 回復 → (満足で)Goal 解除 → Release → Brain 再評価、という流れ。
    /// </summary>
    public class ActionRunner : UdonSharpBehaviour
    {
        [Tooltip("Profile が無い場合のフォールバック回復速度(1秒あたり)。")]
        public float fallbackRecoverRate = 20f;

        private CreatureBrain brain;
        private CreatureTargetSelector targetSelector;
        private MovementController movement;
        private NeedsController needsController;
        private CreatureProfile profile;

        private bool acting = false;
        private Goal actingGoal = Goal.None;
        private float lastTime = -1f;

        public void Initialize(CreatureBrain b, CreatureTargetSelector s, MovementController m,
            NeedsController n, CreatureProfile p)
        {
            brain = b;
            targetSelector = s;
            movement = m;
            needsController = n;
            profile = p;
            lastTime = Time.time;
        }

        /// <summary>CreatureCore の Tick から毎回呼ばれる。</summary>
        public void Tick()
        {
            float now = Time.time;
            float dt = (lastTime < 0f) ? 0f : (now - lastTime);
            lastTime = now;

            if (brain == null || targetSelector == null || movement == null || needsController == null) return;

            CreaturePoint tp = targetSelector.GetTargetPoint();
            Goal goal = brain.GetCurrentGoal();

            bool canAct = (tp != null) && (goal != Goal.None) && movement.IsAtTarget();

            if (!canAct)
            {
                if (acting) EndAction();
                return;
            }

            // 途中で Goal が変わったら一旦終了して開始し直す(通常はヒステリシスで起きない)。
            if (acting && actingGoal != goal) EndAction();

            if (!acting)
            {
                acting = true;
                actingGoal = goal;
                tp.Occupy(); // Reserved → Occupied(デバッグ表示が赤になる)
                Debug.Log("[Action] " + name + " started " + GoalName(goal) + " at '" + tp.name + "'");
            }

            // 対応する欲求を時間で回復。
            float rate = (profile != null) ? profile.actionRecoverRate : fallbackRecoverRate;
            if (dt > 0f) needsController.Satisfy(NeedForGoal(goal), rate * dt);
        }

        private void EndAction()
        {
            Debug.Log("[Action] " + name + " finished " + GoalName(actingGoal));
            acting = false;
            actingGoal = Goal.None;
        }

        // ================= 参照(デバッグ表示用) =================

        public bool IsActing() { return acting; }
        public string GetActionStateName() { return acting ? GoalName(actingGoal) : "-"; }

        // ================= ヘルパー =================

        private NeedType NeedForGoal(Goal goal)
        {
            switch (goal)
            {
                case Goal.Eat: return NeedType.Hunger;
                case Goal.Sleep: return NeedType.Sleepiness;
                case Goal.Drink: return NeedType.Thirst;
                case Goal.Play: return NeedType.Playfulness;
                case Goal.SeekAffection: return NeedType.Affection;
                default: return NeedType.Hunger;
            }
        }

        private string GoalName(Goal g)
        {
            switch (g)
            {
                case Goal.Eat: return "Eat";
                case Goal.Drink: return "Drink";
                case Goal.Sleep: return "Sleep";
                case Goal.Play: return "Play";
                case Goal.SeekAffection: return "SeekAffection";
                default: return "None";
            }
        }
    }
}
