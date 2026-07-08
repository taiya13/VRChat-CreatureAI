using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// Layer 2(欲求層): 欲求値を「時間で増やす／充足で減らす」制御(仕様 3.4節)。
    ///
    /// 値そのものは NeedsData(配列)に一元保持し、このクラスはその中身を
    /// 増減させる役割に専念する。優先度の判断は持たない(それは CreatureBrain)。
    ///
    /// この Phase で増加するのは Hunger / Sleepiness の2つのみ。増加速度は
    /// CreatureProfile 由来。GrowNeeds() は実時間差分で増やすため、Tick 間隔を
    /// 変えても増加ペースは変わらない。
    /// </summary>
    public class NeedsController : UdonSharpBehaviour
    {
        private CreatureProfile profile;
        private NeedsData needsData;

        private float lastGrowTime = -1f;

        /// <summary>CreatureCore.Start から Profile と NeedsData を注入する。</summary>
        public void Initialize(CreatureProfile creatureProfile, NeedsData data)
        {
            profile = creatureProfile;
            needsData = data;
            lastGrowTime = Time.time;
        }

        /// <summary>欲求を実時間ぶん増加させる(低頻度 Tick から呼ばれる)。</summary>
        public void GrowNeeds()
        {
            if (profile == null || needsData == null) return;

            float now = Time.time;
            if (lastGrowTime < 0f) { lastGrowTime = now; return; }

            float dt = now - lastGrowTime;
            lastGrowTime = now;
            if (dt <= 0f) return;

            // 全 Need を、それぞれの increaseRate で増やす(0 の Need は増えない)。
            // Water/Fun/Social を足しても、増加速度を設定するだけで自動的に働く。
            int count = needsData.GetNeedCount();
            for (int i = 0; i < count; i++)
            {
                NeedType nt = (NeedType)i;
                float rate = profile.GetIncreaseRate(nt);
                if (rate != 0f) needsData.AddClamped(nt, rate * dt);
            }
        }

        // ================= 充足 API(後フェーズの ActionRunner が使う) =================

        /// <summary>指定 Need を amount だけ充足(減少)させる。</summary>
        public void Satisfy(NeedType type, float amount)
        {
            if (needsData == null) return;
            needsData.AddClamped(type, -amount);
        }

        public void SatisfyHunger(float amount) { Satisfy(NeedType.Hunger, amount); }
        public void SatisfySleepiness(float amount) { Satisfy(NeedType.Sleepiness, amount); }

        // ================= 参照(便宜的なショートカット) =================

        public float GetHunger() { return needsData != null ? needsData.GetValue(NeedType.Hunger) : 0f; }
        public float GetSleepiness() { return needsData != null ? needsData.GetValue(NeedType.Sleepiness) : 0f; }
    }
}
