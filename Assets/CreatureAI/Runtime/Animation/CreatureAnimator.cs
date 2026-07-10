using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// 身体制御層: AI の状態を「見た目(アニメーション)」へ反映する専用コンポーネント。
    ///
    /// [責務] AgentState(ActionRunner)と Goal(Brain)を読み取り、動作種別(MotionKind)を
    ///        決めて、Animator の整数パラメータ "MotionState" に反映する。それだけ。
    ///        アニメーションのクリップや遷移の中身は作らない(Animator 側の役割)。
    ///        Brain・Movement には一切アニメ処理を書かない(責務分離)。
    ///
    /// [決定の一元化] DecideMotion() が「状態→モーション」を1箇所で決める:
    ///        Flee → 逃げる / Moving → 歩く / Acting → その Goal 用モーション / それ以外 → 待機。
    ///        「どの Goal でどのモーションを出すか」の対応は CreatureActionCatalog.MotionForGoal
    ///        に集約されており、新しい Action の専用モーションは対応表に1行足すだけで反映される
    ///        (このクラスは無改造)。クリップ差し替えは Animator(Inspector)側で完結する。
    ///
    /// [他の動物への拡張] このクラスは種族非依存。犬・鹿は AnimatorController(animator)を
    ///        差し替えるだけで、同じ仕組みで別のモーションに切り替わる。
    ///
    /// [しっぽ・耳・表情など] それらは別パラメータ/別レイヤーとして Animator 側に足し、
    ///        必要なら本クラスに「感情」等の追加パラメータ反映を足せばよい(構造は不変)。
    /// </summary>
    public class CreatureAnimator : UdonSharpBehaviour
    {
        [Tooltip("反映先の Animator。未設定なら子から自動取得する。")]
        public Animator animator;

        [Tooltip("Animator 側の整数パラメータ名(動作状態を渡す)。")]
        public string parameterName = "MotionState";

        private CreatureBrain brain;
        private ActionRunner actionRunner;
        private CreatureActionCatalog catalog; // Goal→Motion / 表示名 の唯一の対応表
        private bool debugLog = true;

        private MotionKind currentKind = MotionKind.Idle;
        private int lastSent = -999;

        public void Initialize(CreatureBrain creatureBrain, ActionRunner runner, CreatureActionCatalog actionCatalog)
        {
            brain = creatureBrain;
            actionRunner = runner;
            catalog = actionCatalog;
            CreatureCore core = GetComponent<CreatureCore>();
            if (core != null) debugLog = core.debugLog;

            // Animator の解決は「本体(コントローラーが載る GameObject)」を最優先にする。
            // 子から取ると、差し替えたモデルが持つ別 Animator を掴んでしまい、
            // MotionState が本体のコントローラーへ届かなくなる(= Idle から遷移しない)。
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            if (animator == null)
                Debug.LogWarning("[Animator] " + name + " に Animator が見つかりません。Cat 本体に " +
                    "Animator を付け、CreatureAnimator.controller を割り当ててください。");
            else if (debugLog)
                Debug.Log("[Animator] " + name + " 接続 OK。param='" + parameterName +
                    "' animator='" + animator.name + "'");
        }

        void Start()
        {
            // Initialize(Core 経由)が呼ばれない構成でも、最低限 Animator を解決しておく。
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        /// <summary>CreatureCore の Tick から毎回呼ばれる。状態が変わった時だけ Animator に反映。</summary>
        public void UpdateAnimation()
        {
            currentKind = DecideMotion();
            int v = (int)currentKind;

            if (v != lastSent)
            {
                lastSent = v;
                if (animator != null)
                {
                    animator.SetInteger(parameterName, v);
                    if (debugLog) Debug.Log("[Animator] " + name + " MotionState=" + v +
                        " (" + GetMotionName() + ")");
                }
                else if (debugLog)
                {
                    Debug.LogWarning("[Animator] " + name + " Animator 未接続のため MotionState=" +
                        v + " を反映できません。");
                }
            }
        }

        /// <summary>状態 → 動作種別の決定(モーション決定の一元化箇所)。</summary>
        private MotionKind DecideMotion()
        {
            Goal goal = (brain != null) ? brain.GetCurrentGoal() : Goal.None;

            // 逃走は最優先。
            if (goal == Goal.Flee) return MotionKind.Flee;

            AgentState st = (actionRunner != null) ? actionRunner.GetState() : AgentState.Idle;

            // 地点で行動中なら、その Goal 用のモーション(対応表 = CreatureActionCatalog)。
            // 専用モーションが無い Goal は対応表が Idle を返すので、ここに条件を足す必要はない。
            if (st == AgentState.Acting)
                return (catalog != null) ? catalog.MotionForGoal(goal) : MotionKind.Idle;

            if (st == AgentState.Moving) return MotionKind.Walk;

            return MotionKind.Idle;
        }

        // ================= 参照(デバッグ表示用) =================

        public MotionKind GetMotionKind() { return currentKind; }
        public string GetMotionName() { return (catalog != null) ? catalog.MotionName(currentKind) : "Idle"; }
    }
}
