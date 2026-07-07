using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// 意思決定層(仕様 Layer 3)の土台。
    ///
    /// [責務] NeedsData を読み、今もっとも優先すべき Need を判断して CurrentGoal を保持する。
    ///        それだけ。点検索・Action 選択・移動・アニメは持たない(後フェーズ)。
    ///
    /// [優先度の決め方] 各 Need の「値 × 重み(Profile)」をスコアとし、最大のものを選ぶ。
    ///   例: Hunger=60(重み1.0)→60, Sleepiness=70(重み0.9)→63 ⇒ Sleep。
    ///   重み既定値では概ね「値が大きい Need」が勝つ。これは最小の Utility 計算であり、
    ///   後で「距離・時間帯・性格」などの項をスコアに足していけば、そのまま Utility AI に発展できる。
    ///   (スコア計算 ScoreOf() と Need→Goal 対応 GoalForNeed() が拡張ポイント)
    ///
    /// Cat のルート(NeedsController / NeedsData と同じ GameObject)に付ける。
    /// </summary>
    public class CreatureBrain : UdonSharpBehaviour
    {
        [Tooltip("最大スコアがこの値以下なら Goal を None(待機)にする。0 なら少しでも Need があれば Goal を持つ。")]
        public float activationThreshold = 0f;

        private NeedsData needsData;
        private CreatureProfile profile;

        private Goal currentGoal = Goal.None;

        // 直近の判断根拠(デバッグ表示が参照する)。
        private NeedType lastBestNeed = NeedType.Hunger;
        private float lastBestScore = 0f;

        /// <summary>CreatureCore.Start から依存を注入する。</summary>
        public void Initialize(NeedsData data, CreatureProfile creatureProfile)
        {
            needsData = data;
            profile = creatureProfile;
        }

        /// <summary>
        /// 現在の Need から CurrentGoal を再評価する(低頻度 Tick から呼ばれる)。
        /// 最優先 Need を選び、対応する Goal に更新。変化したときだけログを出す。
        /// </summary>
        public void Evaluate()
        {
            if (needsData == null) return;

            int count = needsData.GetNeedCount();
            NeedType best = NeedType.Hunger;
            float bestScore = -1f;

            for (int i = 0; i < count; i++)
            {
                NeedType nt = (NeedType)i;
                float score = ScoreOf(nt, needsData.GetValueByIndex(i));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = nt;
                }
            }

            lastBestNeed = best;
            lastBestScore = bestScore;

            Goal newGoal = (bestScore > activationThreshold) ? GoalForNeed(best) : Goal.None;

            if (newGoal != currentGoal)
            {
                currentGoal = newGoal;
                Debug.Log("[Brain] " + name + " current goal: " + GoalName(newGoal) +
                    "  (Hunger=" + Round1(needsData.GetValue(NeedType.Hunger)) +
                    ", Sleepiness=" + Round1(needsData.GetValue(NeedType.Sleepiness)) + ")");
            }
        }

        public Goal GetCurrentGoal() { return currentGoal; }

        /// <summary>現在の Goal を表示用の文字列で返す(状態表示 UI 等が使う)。</summary>
        public string GetCurrentGoalName() { return GoalName(currentGoal); }

        /// <summary>直近で最優先だった Need の名前(判断根拠の表示用)。</summary>
        public string GetTopNeedName() { return NeedName(lastBestNeed); }

        /// <summary>直近で最優先だった Need のスコア(値×重み)。</summary>
        public float GetTopScore() { return lastBestScore; }

        private string NeedName(NeedType t)
        {
            switch (t)
            {
                case NeedType.Hunger: return "Hunger";
                case NeedType.Sleepiness: return "Sleepiness";
                case NeedType.Thirst: return "Thirst";
                case NeedType.Playfulness: return "Playfulness";
                case NeedType.Affection: return "Affection";
                default: return "?";
            }
        }

        // ================= 拡張ポイント =================

        /// <summary>
        /// Need のスコア(＝優先度)。今は「値 × 重み」。
        /// Utility AI 化のときは、ここに距離・時間帯・性格などの係数を掛け足す。
        /// </summary>
        private float ScoreOf(NeedType type, float value)
        {
            return value * GetWeight(type);
        }

        private Goal GoalForNeed(NeedType type)
        {
            switch (type)
            {
                case NeedType.Hunger: return Goal.Eat;
                case NeedType.Sleepiness: return Goal.Sleep;
                case NeedType.Thirst: return Goal.Drink;
                case NeedType.Playfulness: return Goal.Play;
                case NeedType.Affection: return Goal.SeekAffection;
                default: return Goal.None;
            }
        }

        private float GetWeight(NeedType type)
        {
            if (profile == null) return 1f;
            switch (type)
            {
                case NeedType.Hunger: return profile.hungerWeight;
                case NeedType.Sleepiness: return profile.sleepWeight;
                case NeedType.Thirst: return profile.thirstWeight;
                case NeedType.Playfulness: return profile.playWeight;
                case NeedType.Affection: return profile.affectionWeight;
                default: return 1f;
            }
        }

        // ================= ログ用ヘルパー =================

        /// <summary>enum.ToString() は Udon で名前を返さないことがあるため自前で名前化する。</summary>
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

        private float Round1(float v) { return Mathf.Round(v * 10f) / 10f; }
    }
}
