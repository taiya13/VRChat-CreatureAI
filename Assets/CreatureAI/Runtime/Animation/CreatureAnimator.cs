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
    /// [決定表(唯一の対応箇所)] DecideMotion():
    ///        Flee → 逃げる / Acting+Eat → 食べる / Acting+Sleep → 眠る /
    ///        Moving → 歩く / それ以外 → 待機。
    ///        毛づくろい・あくび・伸び等を足すときは、ここに条件を1つ足し、
    ///        MotionKind と AnimatorController に状態を足すだけでよい。
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

        private MotionKind currentKind = MotionKind.Idle;
        private int lastSent = -999;

        public void Initialize(CreatureBrain creatureBrain, ActionRunner runner)
        {
            brain = creatureBrain;
            actionRunner = runner;
        }

        void Start()
        {
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
                if (animator != null) animator.SetInteger(parameterName, v);
                // 動作種別は HUD / Scene ラベル(Motion)で確認できるためログは出さない。
            }
        }

        /// <summary>状態 → 動作種別の対応(唯一の決定箇所 / 拡張点)。</summary>
        private MotionKind DecideMotion()
        {
            Goal goal = (brain != null) ? brain.GetCurrentGoal() : Goal.None;

            // 逃走は最優先。
            if (goal == Goal.Flee) return MotionKind.Flee;

            AgentState st = (actionRunner != null) ? actionRunner.GetState() : AgentState.Idle;

            if (st == AgentState.Acting)
            {
                if (goal == Goal.Eat) return MotionKind.Eat;
                if (goal == Goal.Sleep) return MotionKind.Sleep;
                // Drink / Play など専用モーションが無いものは今は待機扱い(将来ここに追加)。
                return MotionKind.Idle;
            }

            if (st == AgentState.Moving) return MotionKind.Walk;

            return MotionKind.Idle;
        }

        // ================= 参照(デバッグ表示用) =================

        public MotionKind GetMotionKind() { return currentKind; }
        public string GetMotionName() { return MotionName(currentKind); }

        private string MotionName(MotionKind k)
        {
            switch (k)
            {
                case MotionKind.Walk: return "Walk";
                case MotionKind.Eat: return "Eat";
                case MotionKind.Sleep: return "Sleep";
                case MotionKind.Flee: return "Flee";
                default: return "Idle";
            }
        }
    }
}
