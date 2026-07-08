namespace CreatureAI
{
    /// <summary>
    /// 欲求の種類。NeedsData 配列のインデックスと一致させるため、値は 0 から連番で固定する。
    ///
    /// この Phase で実際に増加するのは Hunger / Sleepiness の2つのみ。
    /// 残り(Thirst / Playfulness / Affection)は将来の拡張枠として先に定義しておき、
    /// 値は常に 0 のまま(＝Brain が選ばない)。増加処理を足せばそのまま有効化できる。
    ///
    /// Utility AI 化を見据え、Brain は「種類ごとにスコアを計算して最大を選ぶ」形で
    /// この列挙をインデックスとして回す。
    /// </summary>
    public enum NeedType
    {
        Hunger = 0,
        Sleepiness = 1,
        Thirst = 2,
        Playfulness = 3,
        Affection = 4,
    }
}
