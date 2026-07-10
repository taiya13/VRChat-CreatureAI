namespace CreatureAI
{
    /// <summary>
    /// Brain が決定する「今もっとも満たすべき目標」。
    ///
    /// None は「特に急ぐ Need が無い＝待機」を表す。実際に選ばれるかどうかは対応する
    /// Need の増加速度(CreatureProfile.increaseRate)次第で、増加が 0 の Need の Goal は
    /// 「配線済みだが休眠中(いつでも有効化できる)」状態になる。
    ///
    /// Goal は「何をしたいか」までで、「どこで・どう行動するか」は含まない。
    /// 点検索・Action 選択・移動は ActionRunner 等の責務。
    ///
    /// [新しい Action(Goal)の足し方]
    ///   1. ここに Goal を1つ足す(値は連番の末尾に。既存値は動かさない)。
    ///   2. NeedType に対応する欲求を足す(既存の欲求を使い回すなら不要)。
    ///   3. CreatureActionCatalog の対応表4つ(Need/PointType/Motion/Name)に1行ずつ足す。
    ///   これだけで Brain / TargetSelector / ActionRunner / Animator は無改造で対応する。
    /// </summary>
    public enum Goal
    {
        None = 0,
        Eat = 1,
        Drink = 2,
        Sleep = 3,
        Play = 4,
        SeekAffection = 5,
        // 割込み系(Need には対応しない特別な Goal)。危険回避で最優先になる。
        Flee = 6,
        // 追加の Action(対応表 CreatureActionCatalog で Need/Point/Motion に紐付く)。
        Scratch = 7,
    }
}
