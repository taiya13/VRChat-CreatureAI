using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// 種族パラメータの入れ物(仕様 3.6節)。
    ///
    /// UdonSharp は自作 ScriptableObject をランタイム参照できないため、
    /// ScriptableObject ではなく UdonSharpBehaviour として実装し、
    /// Cat Prefab の子オブジェクト "Profile" に AddComponent する。
    ///
    /// 犬・鹿など他種族へは、Cat Prefab の Prefab Variant を作り、この
    /// フィールド値を上書きすることで対応する(設計思想は SO と同等)。
    ///
    /// このクラス自体はロジックを持たない純粋なデータ保持体。
    /// Phase 1 で実際に参照されるのは空腹・眠気の増加速度のみだが、
    /// 仕様どおり全フィールドを最初から定義しておく(以降のフェーズで
    /// Profile を作り直さずに済むようにするため)。
    /// </summary>
    public class CreatureProfile : UdonSharpBehaviour
    {
        [Header("欲求増加速度 (1秒あたり / 0-100スケール)")]
        [Tooltip("空腹の増加速度。Phase 1 で使用。")]
        public float hungerGrowthRate = 1.0f;
        [Tooltip("渇きの増加速度。Phase 2 以降で使用。")]
        public float thirstGrowthRate = 1.5f;
        [Tooltip("眠気の増加速度。Phase 1 で使用。")]
        public float sleepinessGrowthRate = 0.8f;
        [Tooltip("遊びたさの増加速度。Phase 2 以降で使用。")]
        public float playfulnessGrowthRate = 0.6f;
        [Tooltip("甘えたさの増加速度。Phase 2 以降で使用。")]
        public float affectionGrowthRate = 0.4f;

        [Header("意思決定の重み (UtilityDecisionMaker が参照 / Phase 2 以降)")]
        public float hungerWeight = 1.0f;
        public float thirstWeight = 1.2f;
        public float sleepWeight = 0.9f;
        public float playWeight = 0.7f;
        public float affectionWeight = 0.5f;

        [Header("移動・行動パラメータ")]
        [Tooltip("簡易ステアリング移動の速度 (m/s)。")]
        public float moveSpeed = 1.5f;
        [Tooltip("占有タイムアウト秒数(仕様 3.5節)。Reserve から到達しないまま" +
                 "この秒数が過ぎると Registry の低頻度掃除で強制解放する。")]
        public float reserveTimeout = 10.0f;

        [Tooltip("行動しきい値: 対象の欲求がこの値(0-100)まで溜まったら行動を開始する。" +
                 "大きいほど『限界まで我慢してから動く』。メニューからも変更可。")]
        public float actionThreshold = 40f;
        [Tooltip("行動中に欲求が回復する速度(1秒あたり)。大きいほど食事/睡眠が早く終わる。")]
        public float actionRecoverRate = 20f;

        // [Animator について / 仕様 3.6 からの実装上の変更]
        // 仕様では RuntimeAnimatorController を Profile に持たせる設計だったが、
        // Udon は RuntimeAnimatorController を「変数の型」として許可しておらず、
        // U# コンパイルエラー(Udon does not support variables of type ... yet)で
        // 全スクリプトの Program Asset 生成が止まる。これは v3.1 が SO→UdonSharpBehaviour
        // で修正したのと同種の「Udon 実装不能」問題。
        // → AnimatorController は Cat の Animator コンポーネント側に直接設定する方式に変更。
        //   CreatureAnimator(後フェーズ)が Animator を参照して制御するため、この Profile に
        //   RuntimeAnimatorController フィールドを持つ必要はない。
    }
}
