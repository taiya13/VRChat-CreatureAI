namespace CreatureAI
{
    /// <summary>
    /// 見た目の動作種別(アニメーションの切り替え単位)。
    ///
    /// AgentState(Idle/Moving/Acting)と Goal(Eat/Sleep/Flee…)から
    /// CreatureAnimator が決定する「今どのモーションを再生すべきか」。
    ///
    /// 値は AnimatorController の整数パラメータ "MotionState" の値と一致させる
    /// (待機=0, 歩く=1, 食べる=2, 眠る=3, 逃げる=4, 飲む=5, 遊ぶ=6, 爪とぎ=7, 毛づくろい=8, 伸び=9)。
    ///
    /// [新しいモーションの足し方(コード1行 + 対応表)]
    ///   1. ここに種別を1つ足す(値は必ず連番の末尾に。既存値は動かさない)。
    ///   2. CreatureActionCatalog.MotionForGoal / MotionName にその種別を紐付ける。
    ///   3. AnimatorController の生成表(CreatureAISetup.MotionStates)に1行足す
    ///      (または既存 Controller の AnyState 遷移に MotionState==値 を1つ足す)。
    /// どのモーションを再生するかの判断は CreatureAnimator が一元管理し、モーションの
    /// クリップ差し替えは Animator 側(Inspector)で完結する。
    /// </summary>
    public enum MotionKind
    {
        Idle = 0,     // 待機
        Walk = 1,     // 歩く
        Eat = 2,      // 食べる
        Sleep = 3,    // 眠る
        Flee = 4,     // 逃げる
        Drink = 5,    // 飲む
        Play = 6,     // 遊ぶ
        Scratch = 7,  // 爪とぎ
        // --- 予約(状態は生成するが、まだ Goal に紐付いていない = 待機の演出候補) ---
        Groom = 8,    // 毛づくろい
        Stretch = 9,  // 伸び
    }
}
