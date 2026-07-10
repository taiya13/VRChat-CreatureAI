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
    ///     → 毎 Tick、対応する Need を Profile.decreaseRate[Need] で回復(NeedsController.Satisfy)
    ///   Brain 側のヒステリシスで、その Need が満たされると Goal が None になる
    ///     → 行動条件が崩れるので Action を終了(ログ)。占有の解放は TargetSelector が
    ///       Goal 変更を検知して行う(解放経路は Release に一本化)。
    ///
    /// [接続] 到着 → Occupy → 回復 → (満足で)Goal 解除 → Release → Brain 再評価、という流れ。
    ///
    /// [ライフサイクルの所有(将来の割込みに向けた整理)]
    ///   ・予約(Reserve)と解放(Release)の"プリミティブ"は TargetSelector が握る
    ///     (TargetSelector.ReleaseTarget が唯一の解放入口)。
    ///   ・占有(Occupy)と行動の実行・状態(AgentState)は ActionRunner が握る。
    ///   ・AbortCurrent() が「行動・移動・予約をまとめて畳む単一の中断経路」。
    ///     将来 Flee/危険回避はこれを呼ぶだけで解放漏れが起きない
    ///     (現状ではどこからも呼ばれず、動作は不変)。
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
        private CreaturePersonality personality;

        private bool acting = false;
        private Goal actingGoal = Goal.None;
        private float lastTime = -1f;
        private bool debugLog = true;

        // 明示的なエージェント状態(観測用。判断はしない)。
        private AgentState state = AgentState.Idle;

        public void Initialize(CreatureBrain b, CreatureTargetSelector s, MovementController m,
            NeedsController n, CreatureProfile p)
        {
            brain = b;
            targetSelector = s;
            movement = m;
            needsController = n;
            profile = p;
            personality = GetComponent<CreaturePersonality>(); // 活発さで回復速度が変わる
            CreatureCore core = GetComponent<CreatureCore>();
            if (core != null) debugLog = core.debugLog;
            lastTime = Time.time;
        }

        /// <summary>CreatureCore の Tick から毎回呼ばれる。</summary>
        public void Tick()
        {
            float now = Time.time;
            float dt = (lastTime < 0f) ? 0f : (now - lastTime);
            lastTime = now;

            if (brain == null || targetSelector == null || movement == null || needsController == null) return;

            Goal goal = brain.GetCurrentGoal();

            // 逃走中は地点行動をしない(移動は MovementController が担当)。状態は Moving 扱い。
            if (goal == Goal.Flee)
            {
                if (acting) EndAction();
                state = AgentState.Moving;
                return;
            }

            CreaturePoint tp = targetSelector.GetTargetPoint();
            bool canAct = (tp != null) && (goal != Goal.None) && movement.IsAtTarget();

            if (!canAct)
            {
                if (acting) EndAction();
                // 目的地へ移動中、または Idle 徘徊で歩いている間は Moving(歩行アニメ)。
                if ((tp != null && goal != Goal.None) || movement.IsWandering())
                    state = AgentState.Moving;
                else
                    state = AgentState.Idle;
                return;
            }

            // 途中で Goal が変わったら一旦終了して開始し直す(通常はヒステリシスで起きない)。
            if (acting && actingGoal != goal) EndAction();

            if (!acting)
            {
                acting = true;
                actingGoal = goal;
                tp.Occupy(); // Reserved → Occupied(デバッグ表示が赤になる)
                if (debugLog) Debug.Log("[Action] " + name + " started " + brain.GoalName(goal) + " at '" + tp.name + "'");
            }

            state = AgentState.Acting;

            // 対応する欲求を、その Need の decreaseRate で回復(活発さで速さが変わる)。
            NeedType nt = brain.NeedForGoal(goal);
            float rate = (profile != null) ? profile.GetDecreaseRate(nt) : fallbackRecoverRate;
            if (personality != null) rate *= personality.GetNeedsRateMult();
            if (dt > 0f) needsController.Satisfy(nt, rate * dt);
        }

        /// <summary>
        /// 現在の行動・移動・予約をまとめて中断する単一経路(仕様3.2 AbortCurrent 相当)。
        /// 行動停止 → 予約解放(TargetSelector) → 到着状態リセット(Movement) → Idle。
        /// 将来の割込み(Flee/危険回避)はこれ 1 つを呼べば解放漏れが起きない。
        /// 何度呼んでも安全(冪等)。※現状は未使用のため既存動作に影響しない。
        /// </summary>
        public void AbortCurrent()
        {
            if (acting) EndAction();
            if (targetSelector != null) targetSelector.ReleaseTarget();
            if (movement != null) movement.ResetArrival();
            state = AgentState.Idle;
        }

        private void EndAction()
        {
            if (debugLog) Debug.Log("[Action] " + name + " finished " + ((brain != null) ? brain.GoalName(actingGoal) : "?"));
            acting = false;
            actingGoal = Goal.None;
        }

        // ================= 参照(デバッグ表示用) =================

        public bool IsActing() { return acting; }
        public string GetActionStateName()
        {
            return acting ? ((brain != null) ? brain.GoalName(actingGoal) : "?") : "-";
        }

        /// <summary>今まさに行動中の対象 Need(空腹を食べている等)。NeedsController が増加を止めるのに使う。</summary>
        public NeedType GetActingNeed()
        {
            return (brain != null) ? brain.NeedForGoal(actingGoal) : NeedType.Hunger;
        }

        /// <summary>明示的なエージェント状態(Idle/Moving/Acting)。</summary>
        public AgentState GetState() { return state; }

        public string GetStateName()
        {
            switch (state)
            {
                case AgentState.Moving: return "Moving";
                case AgentState.Acting: return "Acting";
                default: return "Idle";
            }
        }

        // 対応表(GoalName / NeedForGoal)は CreatureBrain に一本化した(重複排除)。
    }
}
