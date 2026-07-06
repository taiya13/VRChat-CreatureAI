using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// Layer 2(欲求層): 欲求値を管理する(仕様 3.4節)。
    ///
    /// Phase 1 では「空腹(hunger)」「眠気(sleepiness)」の 2 つのみを扱う。
    /// 各値は 0-100 スケール(100 = 限界)。増加速度は CreatureProfile が持つ。
    ///
    /// GrowNeeds() は CreatureCore の低頻度 Tick(tickCounter % 5)から呼ばれるが、
    /// 呼び出し間隔に依存しないよう、実時間ベース(Time.time の差分)で増加させる。
    /// これにより Tick 間隔を後で変えても欲求の増加ペースが変わらない。
    /// </summary>
    public class NeedsController : UdonSharpBehaviour
    {
        [Tooltip("この値を超えると『空腹/眠い』とみなす閾値(状態変化ログのトリガ)。")]
        public float needThreshold = 60f;

        private CreatureProfile profile;

        // 欲求値(0-100)。
        private float hunger = 0f;
        private float sleepiness = 0f;

        private float lastGrowTime = -1f;

        // 状態変化ログ用の直前フラグ。
        private bool wasHungry = false;
        private bool wasSleepy = false;

        /// <summary>CreatureCore.Start から Profile を注入する。</summary>
        public void Initialize(CreatureProfile creatureProfile)
        {
            profile = creatureProfile;
            lastGrowTime = Time.time;
        }

        /// <summary>欲求を実時間ぶん増加させる(低頻度 Tick から呼ばれる)。</summary>
        public void GrowNeeds()
        {
            if (profile == null) return;

            float now = Time.time;
            if (lastGrowTime < 0f) { lastGrowTime = now; return; }

            float dt = now - lastGrowTime;
            lastGrowTime = now;
            if (dt <= 0f) return;

            hunger = Mathf.Clamp(hunger + profile.hungerGrowthRate * dt, 0f, 100f);
            sleepiness = Mathf.Clamp(sleepiness + profile.sleepinessGrowthRate * dt, 0f, 100f);

            LogThresholdCrossings();
        }

        // ================= 充足 API(Phase 2 の ActionRunner が使う) =================

        public void SatisfyHunger(float amount)
        {
            hunger = Mathf.Clamp(hunger - amount, 0f, 100f);
            LogThresholdCrossings();
        }

        public void SatisfySleepiness(float amount)
        {
            sleepiness = Mathf.Clamp(sleepiness - amount, 0f, 100f);
            LogThresholdCrossings();
        }

        // ================= 参照 =================

        public float GetHunger() { return hunger; }
        public float GetSleepiness() { return sleepiness; }
        public bool IsHungry() { return hunger >= needThreshold; }
        public bool IsSleepy() { return sleepiness >= needThreshold; }

        // ================= 内部 =================

        /// <summary>閾値をまたいだ瞬間だけコンソールに出力する(Phase 1 の動作確認用)。</summary>
        private void LogThresholdCrossings()
        {
            bool nowHungry = hunger >= needThreshold;
            if (nowHungry != wasHungry)
            {
                // 書式付き ToString("F1") は Udon で非対応のため、四捨五入して既定 ToString を使う。
                Debug.Log("[NeedsController] " + name + " 空腹=" +
                    (nowHungry ? "限界に接近" : "解消") +
                    " (hunger=" + (Mathf.Round(hunger * 10f) / 10f) + ")");
                wasHungry = nowHungry;
            }

            bool nowSleepy = sleepiness >= needThreshold;
            if (nowSleepy != wasSleepy)
            {
                Debug.Log("[NeedsController] " + name + " 眠気=" +
                    (nowSleepy ? "限界に接近" : "解消") +
                    " (sleepiness=" + (Mathf.Round(sleepiness * 10f) / 10f) + ")");
                wasSleepy = nowSleepy;
            }
        }
    }
}
