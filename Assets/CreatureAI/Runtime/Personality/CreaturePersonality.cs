using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// 性格システム(Phase 8)。同じ AI でも、猫ごとの「行動の傾向」を表現する。
    ///
    /// [設計] 学習や記憶は持たない。4つの性格値(0〜1、0.5 が平均)を保持し、
    ///        各層(Threat/Needs/Movement/Brain)が「必要な倍率」を問い合わせる純データ層。
    ///        判断そのものはしない(責務分離を維持)。
    ///
    /// [4つの性格]
    ///   臆病さ(timidity)   : 危険への反応距離・逃走の速さ。高いほど遠くから素早く逃げる。
    ///   好奇心(curiosity)  : 徘徊(探索)の広さと積極性。高いほど広く歩き回り、休みが短い。
    ///   活発さ(activeness) : 移動速度・欲求の増減。高いほど速く動き、早く空腹になり早く回復。
    ///   のんびりさ(relaxedness): 待機/休憩の長さ・行動を始める溜まり具合。高いほどゆったり。
    ///
    /// [将来の発展] Memory/Utility AI へは、この倍率群に記憶や学習の係数を掛け足すだけで
    ///        発展できる(消費側は変更不要)。
    /// </summary>
    public class CreaturePersonality : UdonSharpBehaviour
    {
        [Range(0f, 1f)] [Tooltip("臆病さ。高いほど遠くから素早く逃げる。")]
        public float timidity = 0.5f;
        [Range(0f, 1f)] [Tooltip("好奇心。高いほど広く歩き回り、探索的。")]
        public float curiosity = 0.5f;
        [Range(0f, 1f)] [Tooltip("活発さ。高いほど速く動き、早く空腹/早く回復。")]
        public float activeness = 0.5f;
        [Range(0f, 1f)] [Tooltip("のんびりさ。高いほど休憩が長く、溜まるまで行動しない。")]
        public float relaxedness = 0.5f;

        // ================= 各層が問い合わせる倍率(1.0 が基準) =================

        /// <summary>危険検知距離の倍率(臆病ほど遠くから逃げる)。ThreatEvaluator が使用。</summary>
        public float GetThreatDistanceMult() { return Lerp(0.5f, 1.5f, timidity); }

        /// <summary>逃走速度の倍率(臆病ほど速く逃げる)。MovementController が使用。</summary>
        public float GetFleeSpeedMult() { return Lerp(0.85f, 1.3f, timidity); }

        /// <summary>移動速度の倍率(活発ほど速い)。MovementController が使用。</summary>
        public float GetMoveSpeedMult() { return Lerp(0.7f, 1.3f, activeness); }

        /// <summary>欲求の増減倍率(活発ほど早く空腹/早く回復)。NeedsController・ActionRunner が使用。</summary>
        public float GetNeedsRateMult() { return Lerp(0.7f, 1.3f, activeness); }

        /// <summary>徘徊範囲の倍率(好奇心ほど広く探索)。MovementController が使用。</summary>
        public float GetWanderRadiusMult() { return Lerp(0.6f, 1.6f, curiosity); }

        /// <summary>徘徊/休憩時間の倍率(のんびりほど長い、好奇心ほど短い)。MovementController が使用。</summary>
        public float GetWanderPauseMult() { return Lerp(0.7f, 1.6f, relaxedness) * Lerp(1.2f, 0.6f, curiosity); }

        /// <summary>行動開始しきい値の倍率(のんびりほど溜まるまで待つ)。Brain が使用。</summary>
        public float GetThresholdMult() { return Lerp(0.85f, 1.2f, relaxedness); }

        private float Lerp(float a, float b, float t)
        {
            return a + (b - a) * Mathf.Clamp01(t);
        }
    }
}
