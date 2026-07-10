using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// 種族パラメータの入れ物(仕様 3.6節)。UdonSharpBehaviour として実装し、
    /// Cat の子 "Profile" に付ける。ロジックは持たない純データ。
    ///
    /// [SDK 設計] Need ごとのパラメータは、NeedType をインデックスとした配列で一元管理する。
    ///   ・increaseRate : 時間経過による増加速度(1秒あたり)。0 の Need は増えない。
    ///   ・decreaseRate : 行動中の回復(減少)速度(1秒あたり)。
    ///   ・threshold    : 行動開始のしきい値(この値まで溜まったら行動)。
    ///   ・weight       : Brain の優先度計算に使う重み。
    ///   配列の並びは NeedType の順(Hunger, Sleepiness, Thirst, Playfulness, Affection, Scratchiness)。
    ///
    /// [Need の追加(Water/Fun/Social 等)] NeedType に列挙を足し、各配列を同じ長さに
    ///   拡張し、NeedsData の NeedCount を合わせるだけ。未設定分はアクセサが既定値を返すので
    ///   壊れない。値の編集は「CreatureAI > ステータス設定」ウィンドウが便利(配列を表で編集)。
    /// </summary>
    public class CreatureProfile : UdonSharpBehaviour
    {
        [Tooltip("時間経過による増加速度(1秒あたり)。Hunger,Sleepiness,Thirst,Playfulness,Affection,Scratchiness の順。" +
                 "0 の Need は増えない(＝配線済みだが休眠中)。")]
        public float[] increaseRate = new float[] { 1.0f, 0.8f, 0.5f, 0.0f, 0.0f, 0.3f };

        [Tooltip("行動中の回復(減少)速度(1秒あたり)。")]
        public float[] decreaseRate = new float[] { 20f, 20f, 20f, 20f, 20f, 20f };

        [Tooltip("行動開始のしきい値(この値まで溜まったら行動を開始)。")]
        public float[] threshold = new float[] { 40f, 40f, 40f, 40f, 40f, 40f };

        [Tooltip("Brain の優先度計算に使う重み(値×重み で最優先 Need を決める)。")]
        public float[] weight = new float[] { 1.0f, 0.9f, 1.2f, 0.7f, 0.5f, 0.8f };

        [Header("移動・占有")]
        [Tooltip("簡易ステアリング移動の速度 (m/s)。")]
        public float moveSpeed = 1.5f;
        [Tooltip("占有タイムアウト秒数(仕様 3.5節)。")]
        public float reserveTimeout = 10.0f;

        // ================= アクセサ(範囲外は既定値を返す = Need 追加に強い) =================

        public float GetIncreaseRate(NeedType t) { return GetAt(increaseRate, (int)t, 0f); }
        public float GetDecreaseRate(NeedType t) { return GetAt(decreaseRate, (int)t, 20f); }
        public float GetThreshold(NeedType t) { return GetAt(threshold, (int)t, 40f); }
        public float GetWeight(NeedType t) { return GetAt(weight, (int)t, 1f); }

        private float GetAt(float[] arr, int i, float fallback)
        {
            if (arr == null || i < 0 || i >= arr.Length) return fallback;
            return arr[i];
        }
    }
}
