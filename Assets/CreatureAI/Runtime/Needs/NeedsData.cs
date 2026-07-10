using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// 現在の欲求値をまとめて保持する純データ層。
    ///
    /// 空腹・眠気などを個別フィールドに散らさず、NeedType をインデックスとした
    /// 配列 1 本で一元管理する。これにより:
    ///   - NeedsController は「種類ごとに増やす」だけで済む
    ///   - Brain は「種類ごとにスコアを出して最大を選ぶ」形で回せる(Utility AI 化が容易)
    ///
    /// このクラスはロジックを持たない(増加・充足の判断は NeedsController、
    /// 優先度判断は Brain)。値は 0-100(100 = 限界)にクランプする。
    /// Cat のルート(NeedsController と同じ GameObject)に付ける。
    /// </summary>
    public class NeedsData : UdonSharpBehaviour
    {
        // NeedType の要素数と一致させる
        // (Hunger/Sleepiness/Thirst/Playfulness/Affection/Scratchiness)。
        // Need を1つ足すたびに、この定数も +1 する(Udon では enum 長を実行時取得できないため)。
        private const int NeedCount = 6;

        private float[] values = new float[NeedCount];

        public int GetNeedCount()
        {
            EnsureArray();
            return values.Length;
        }

        public float GetValue(NeedType type)
        {
            EnsureArray();
            return values[(int)type];
        }

        /// <summary>インデックス指定の取得(Brain がループで回すため)。</summary>
        public float GetValueByIndex(int index)
        {
            EnsureArray();
            if (index < 0 || index >= values.Length) return 0f;
            return values[index];
        }

        public void SetValue(NeedType type, float value)
        {
            EnsureArray();
            values[(int)type] = Mathf.Clamp(value, 0f, 100f);
        }

        /// <summary>差分を加算してクランプ(増加にも充足=負値にも使える)。</summary>
        public void AddClamped(NeedType type, float delta)
        {
            EnsureArray();
            int i = (int)type;
            values[i] = Mathf.Clamp(values[i] + delta, 0f, 100f);
        }

        private void EnsureArray()
        {
            if (values == null || values.Length < NeedCount) values = new float[NeedCount];
        }
    }
}
