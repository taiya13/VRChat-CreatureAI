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
        // Profile が無い場合のフォールバック値。
        private const float FallbackThreshold = 40f;
        // 行動を「完了」とみなす下限。対象 Need がこの値以下まで回復したら Goal を手放す。
        private const float CompleteLevel = 5f;

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
        ///
        /// ヒステリシス:
        ///   ・待機中は、最優先 Need の値が「行動しきい値(Profile.threshold[Need])」以上に
        ///     溜まったら、対応する Goal を開始する。
        ///   ・行動中は、その Need が CompleteLevel 以下に回復するまで同じ Goal を維持する
        ///     (食べ始めたら満たされるまで食べ続ける)。回復しきったら Goal を手放して再評価。
        /// </summary>
        public void Evaluate()
        {
            if (needsData == null) return;

            // --- 行動中: 完了するまで現在の Goal を維持(ヒステリシス) ---
            if (currentGoal != Goal.None)
            {
                NeedType active = NeedForGoal(currentGoal);
                float activeValue = needsData.GetValue(active);
                lastBestNeed = active;
                lastBestScore = ScoreOf(active, activeValue);

                if (activeValue > CompleteLevel)
                {
                    return; // まだ満たされていない → 継続
                }

                // 満たされた → 完了。Goal を手放して下で再選択する。
                Goal finished = currentGoal;
                currentGoal = Goal.None;
                Debug.Log("[Brain] " + name + " goal complete: " + GoalName(finished) + " (satisfied)");
            }

            // --- 待機中: 最優先 Need を選び、しきい値を超えていれば開始 ---
            int count = needsData.GetNeedCount();
            NeedType best = NeedType.Hunger;
            float bestScore = -1f;
            for (int i = 0; i < count; i++)
            {
                NeedType nt = (NeedType)i;
                float score = ScoreOf(nt, needsData.GetValueByIndex(i));
                if (score > bestScore) { bestScore = score; best = nt; }
            }
            lastBestNeed = best;
            lastBestScore = bestScore;

            float bestValue = needsData.GetValue(best);
            float th = (profile != null) ? profile.GetThreshold(best) : FallbackThreshold;
            Goal newGoal = (bestValue >= th) ? GoalForNeed(best) : Goal.None;

            if (newGoal != currentGoal)
            {
                currentGoal = newGoal;
                Debug.Log("[Brain] " + name + " current goal: " + GoalName(newGoal) +
                    "  (top=" + NeedName(best) + " " + Round1(bestValue) +
                    ", th=" + Round1(th) + ")");
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

        /// <summary>Goal に対応する Need(GoalForNeed の逆)。ヒステリシス判定に使う。</summary>
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

        private float GetWeight(NeedType type)
        {
            if (profile == null) return 1f;
            return profile.GetWeight(type);
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
