namespace CreatureAI
{
    /// <summary>
    /// Brain が決定する「今もっとも満たすべき目標」。
    ///
    /// この Phase では Eat / Sleep のみが実際に選ばれる(対応する Need が増加するのが
    /// その2つだけのため)。None は「特に急ぐ Need が無い＝待機」を表す。
    /// 残り(Drink / Play / SeekAffection)は NeedType と対になる将来の拡張枠。
    ///
    /// Goal は「何をしたいか」までで、「どこで・どう行動するか」は含まない。
    /// 点検索・Action 選択・移動は後フェーズ(ActionRunner 等)の責務。
    /// </summary>
    public enum Goal
    {
        None = 0,
        Eat = 1,
        Drink = 2,
        Sleep = 3,
        Play = 4,
        SeekAffection = 5,
    }
}
