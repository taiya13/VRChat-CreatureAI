namespace CreatureAI
{
    /// <summary>
    /// 見た目の動作種別(アニメーションの切り替え単位)。
    ///
    /// AgentState(Idle/Moving/Acting)と Goal(Eat/Sleep/Flee…)から
    /// CreatureAnimator が決定する「今どのモーションを再生すべきか」。
    ///
    /// 値は AnimatorController の整数パラメータ "MotionState" の値と一致させる
    /// (待機=0, 歩く=1, 食べる=2, 眠る=3, 逃げる=4)。
    ///
    /// 将来の演出(毛づくろい・あくび・伸び 等)は、ここに種別を足し、
    /// CreatureAnimator の決定表と AnimatorController に状態を足すだけで拡張できる。
    /// </summary>
    public enum MotionKind
    {
        Idle = 0,   // 待機
        Walk = 1,   // 歩く
        Eat = 2,    // 食べる
        Sleep = 3,  // 眠る
        Flee = 4,   // 逃げる
    }
}
