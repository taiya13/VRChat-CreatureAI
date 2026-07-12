using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// 自由時間の行動(生活感)を司る層(Phase 10)。
    ///
    /// [責務] 空腹・眠気・緊急行動が無い「暇な時間(AgentState = Idle)」に、猫らしい所作
    ///        (毛づくろい・あくび・伸び・見回す・座る・遊ぶ)を選び、その MotionKind を
    ///        CreatureAnimator へ渡す。それだけ。移動・意思決定・危険検知には触れない
    ///        (Brain/ActionRunner/Movement は無改造)。「少し歩き回る」は徘徊(Wander)が担う。
    ///
    /// [選び方] 完全ランダムではなく、現在の Needs と性格から各行動に「重み」を付けて
    ///        重み付き抽選する(RollActivity)。例: のんびりな猫は座る/毛づくろいが多く、
    ///        好奇心が高い猫は見回しが多く、眠気が溜まり気味だとあくびが増える。
    ///        1つの行動を数秒間続けてから次を選ぶ(せわしなく切り替わらない)。
    ///
    /// [拡張] 新しい自由行動は IdleActivity に列挙を足し、MotionForActivity と Weight に
    ///        1行ずつ足すだけ(このクラスの他の部分は不変)。
    ///
    /// Cat のルート(CreatureCore と同じ GameObject)に付ける。
    /// </summary>
    public class CreatureIdleBehavior : UdonSharpBehaviour
    {
        [Tooltip("自由行動を有効にする。OFF なら暇なときは素の待機(Idle)のまま。")]
        public bool enableIdleActions = true;

        [Tooltip("1つの自由行動を続ける秒数(最小/最大)。性格ののんびりさで少し延びる。")]
        public float actionDurationMin = 2.0f;
        public float actionDurationMax = 5.0f;

        private NeedsData needsData;
        private ActionRunner actionRunner;
        private CreaturePersonality personality;
        private bool debugLog = true;

        private IdleActivity current = IdleActivity.Stand;
        private float nextRollAt = 0f;

        public void Initialize(NeedsData data, ActionRunner runner)
        {
            needsData = data;
            actionRunner = runner;
            personality = GetComponent<CreaturePersonality>();
            CreatureCore core = GetComponent<CreatureCore>();
            if (core != null) debugLog = core.debugLog;
        }

        /// <summary>CreatureCore の Tick から呼ばれる(CreatureAnimator の更新より前)。</summary>
        public void Tick()
        {
            // 暇なとき(Idle)以外は自由行動しない。移動中・行動中・逃走中は素通り。
            AgentState st = (actionRunner != null) ? actionRunner.GetState() : AgentState.Idle;
            if (!enableIdleActions || st != AgentState.Idle)
            {
                current = IdleActivity.Stand;
                nextRollAt = 0f; // 次に暇になったら即・選び直す
                return;
            }

            float now = Time.time;
            if (now >= nextRollAt)
            {
                IdleActivity picked = RollActivity();
                float relax = (personality != null) ? personality.relaxedness : 0.5f;
                float dur = Random.Range(actionDurationMin, actionDurationMax) * (0.8f + relax * 0.6f);
                nextRollAt = now + dur;

                if (picked != current)
                {
                    current = picked;
                    if (debugLog) Debug.Log("[Idle] " + name + " 自由行動: " + ActivityName(current));
                }
            }
        }

        /// <summary>CreatureAnimator が読む: 今の自由行動のモーション。</summary>
        public MotionKind GetIdleMotion() { return MotionForActivity(current); }

        public IdleActivity GetActivity() { return current; }
        public string GetActivityName() { return ActivityName(current); }

        // ================= 選択(Needs + 性格の重み付け抽選) =================

        /// <summary>各自由行動に重みを付け、その比率で1つ抽選する(ランダムだが傾向が出る)。</summary>
        private IdleActivity RollActivity()
        {
            // 列挙の総数(Stand..Play)。新しい行動を足したら末尾の +1 を合わせる。
            int count = (int)IdleActivity.Play + 1;

            float total = 0f;
            for (int i = 0; i < count; i++) total += Weight((IdleActivity)i);
            if (total <= 0f) return IdleActivity.Stand;

            float r = Random.value * total;
            for (int i = 0; i < count; i++)
            {
                r -= Weight((IdleActivity)i);
                if (r <= 0f) return (IdleActivity)i;
            }
            return IdleActivity.Stand;
        }

        /// <summary>
        /// 行動ごとの重み(＝選ばれやすさ)。Needs と性格から決める(拡張点)。
        /// 値が大きいほど選ばれやすい。0 にすればその行動は起きない。
        /// </summary>
        private float Weight(IdleActivity a)
        {
            // 性格(0〜1、既定 0.5)。
            float timid = 0.5f, curious = 0.5f, active = 0.5f, relax = 0.5f;
            if (personality != null)
            {
                timid = personality.timidity; curious = personality.curiosity;
                active = personality.activeness; relax = personality.relaxedness;
            }
            // まだ閾値未満の Needs も「気配」として少し効かせる(0〜1に正規化)。
            float sleepy = Need01(NeedType.Sleepiness);
            float playful = Need01(NeedType.Playfulness);
            float scratchy = Need01(NeedType.Scratchiness);

            switch (a)
            {
                case IdleActivity.Stand:      return 0.6f;                          // 基準(いつでも少し)
                case IdleActivity.Sit:        return 0.8f + relax * 1.2f;           // のんびりほど座る
                case IdleActivity.Groom:      return 0.8f + relax * 0.8f + scratchy * 0.6f; // 落ち着き/身繕い欲
                case IdleActivity.Yawn:       return 0.3f + sleepy * 1.2f;          // 眠気が溜まるほどあくび
                case IdleActivity.Stretch:    return 0.4f + (1f - active) * 0.6f;   // 動きが少ない猫ほど伸び
                case IdleActivity.LookAround: return 0.5f + curious * 1.3f + timid * 0.4f; // 好奇心/警戒で見回す
                case IdleActivity.Play:       return 0.2f + active * 0.8f + playful * 1.0f; // 活発/遊びたさ
                default:                      return 0.2f;
            }
        }

        private float Need01(NeedType t)
        {
            if (needsData == null) return 0f;
            return Mathf.Clamp01(needsData.GetValue(t) / 100f);
        }

        // ================= 行動 → モーション / 表示名 =================

        private MotionKind MotionForActivity(IdleActivity a)
        {
            switch (a)
            {
                case IdleActivity.Sit: return MotionKind.Sit;
                case IdleActivity.Groom: return MotionKind.Groom;
                case IdleActivity.Yawn: return MotionKind.Yawn;
                case IdleActivity.Stretch: return MotionKind.Stretch;
                case IdleActivity.LookAround: return MotionKind.LookAround;
                case IdleActivity.Play: return MotionKind.Play;
                default: return MotionKind.Idle; // Stand
            }
        }

        private string ActivityName(IdleActivity a)
        {
            switch (a)
            {
                case IdleActivity.Sit: return "Sit";
                case IdleActivity.Groom: return "Groom";
                case IdleActivity.Yawn: return "Yawn";
                case IdleActivity.Stretch: return "Stretch";
                case IdleActivity.LookAround: return "LookAround";
                case IdleActivity.Play: return "Play";
                default: return "Stand";
            }
        }
    }
}
