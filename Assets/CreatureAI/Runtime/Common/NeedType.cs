namespace CreatureAI
{
    /// <summary>
    /// 欲求の種類。NeedsData 配列のインデックスと一致させるため、値は 0 から連番で固定する。
    ///
    /// 実際に増加するかどうかは CreatureProfile.increaseRate で決まる。増加速度 0 の欲求は
    /// 「配線済みだが休眠中(いつでも有効化できる)」状態になる。
    ///
    /// Utility AI 化を見据え、Brain は「種類ごとにスコアを計算して最大を選ぶ」形で
    /// この列挙をインデックスとして回す。
    ///
    /// [新しい欲求の足し方]
    ///   1. ここに欲求を1つ足す(値は連番の末尾に固定。配列インデックスと一致させるため)。
    ///   2. NeedsData.NeedCount を +1 する(Udon では enum 長を実行時取得できないための割り切り)。
    ///   3. CreatureProfile の各配列を1要素伸ばす(未設定でもアクセサが既定値を返すので壊れない)。
    ///   4. CreatureActionCatalog.GoalForNeed / NeedForGoal / NeedName に紐付ける。
    /// 値の編集は「CreatureAI > ステータス設定」ウィンドウが便利(配列を表で編集)。
    /// </summary>
    public enum NeedType
    {
        Hunger = 0,
        Sleepiness = 1,
        Thirst = 2,
        Playfulness = 3,
        Affection = 4,
        Scratchiness = 5,
    }
}
