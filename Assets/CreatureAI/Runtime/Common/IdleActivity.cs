namespace CreatureAI
{
    /// <summary>
    /// 自由時間(暇なとき)の猫らしい行動の種類。CreatureIdleBehavior が Needs と性格から選ぶ。
    ///
    /// これは「地点を使う Goal(Eat/Sleep 等)」とは別軸で、空腹・眠気・緊急が無いときの
    /// その場の所作(毛づくろい・あくび 等)を表す。「少し歩き回る」は徘徊(Wander)が担うので
    /// ここには含めない(移動は MovementController の責務のまま)。
    ///
    /// [新しい自由行動の足し方]
    ///   1. ここに列挙を1つ足す(末尾に)。
    ///   2. MotionKind に対応するモーションを足し、CreatureIdleBehavior の MotionForActivity と
    ///      Weight(重み付け)に1行ずつ足す。
    ///   3. AnimatorController に状態を足す(CreatureAISetup のモーション対応表に1行)。
    /// </summary>
    public enum IdleActivity
    {
        Stand = 0,      // ただ立って待つ(基準)
        Sit = 1,        // 座る
        Groom = 2,      // 毛づくろい
        Yawn = 3,       // あくび
        Stretch = 4,    // 伸び
        LookAround = 5, // 見回す
        Play = 6,       // その場で遊ぶ
    }
}
