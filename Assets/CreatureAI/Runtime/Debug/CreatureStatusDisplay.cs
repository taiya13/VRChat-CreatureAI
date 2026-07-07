using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace CreatureAI
{
    /// <summary>
    /// ワールド内に置ける、猫のリアルタイム状態表示(仕様 0章で延期されていた
    /// 頭上デバッグ HUD の簡易版)。
    ///
    /// NeedsData / Brain / TargetSelector を読み、World Space Canvas 上の
    /// UnityEngine.UI.Text に「現在の Goal・Target・各 Need のバー」を表示する。
    /// 判断はしない(表示専用)。
    ///
    /// UI.Text 参照(targetText)はセットアップツールが自動で組み立てて割り当てる。
    /// 手動で使う場合は World Space Canvas 上の Text を割り当てればよい。
    /// </summary>
    public class CreatureStatusDisplay : UdonSharpBehaviour
    {
        [Tooltip("表示先の UI Text(World Space Canvas 上)。")]
        public Text targetText;

        [Tooltip("表示に使う猫の名前。")]
        public string displayName = "Cat";

        private NeedsData needsData;
        private CreatureBrain brain;
        private CreatureTargetSelector targetSelector;

        public void Initialize(NeedsData data, CreatureBrain creatureBrain, CreatureTargetSelector selector)
        {
            needsData = data;
            brain = creatureBrain;
            targetSelector = selector;
        }

        /// <summary>CreatureCore の Tick から毎回呼ばれ、Text を最新状態に更新する。</summary>
        public void UpdateDisplay()
        {
            if (targetText == null) return;

            string goal = (brain != null) ? brain.GetCurrentGoalName() : "-";
            string target = (targetSelector != null) ? targetSelector.GetTargetPointName() : "-";

            float hunger = (needsData != null) ? needsData.GetValue(NeedType.Hunger) : 0f;
            float sleep = (needsData != null) ? needsData.GetValue(NeedType.Sleepiness) : 0f;

            string s =
                "<b>" + displayName + "</b>\n" +
                "Goal   : " + goal + "\n" +
                "Target : " + target + "\n" +
                "\n" +
                "Hunger     " + Bar(hunger) + " " + Pct(hunger) + "\n" +
                "Sleepiness " + Bar(sleep) + " " + Pct(sleep);

            targetText.text = s;
        }

        // ================= 表示ヘルパー =================

        /// <summary>0-100 を 10 段のバーにする(■=満杯側 / □=空き)。</summary>
        private string Bar(float value)
        {
            int filled = (int)(Mathf.Clamp(value, 0f, 100f) / 10f + 0.5f);
            if (filled > 10) filled = 10;
            string bar = "[";
            for (int i = 0; i < 10; i++) bar += (i < filled) ? "■" : "□";
            bar += "]";
            return bar;
        }

        private string Pct(float value)
        {
            int v = (int)(Mathf.Clamp(value, 0f, 100f) + 0.5f);
            return v + "%";
        }
    }
}
