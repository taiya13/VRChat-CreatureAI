namespace CreatureAI
{
    /// <summary>
    /// CreaturePoint の占有状態(仕様 3.5節)。
    ///
    ///   Free     … 未使用。候補になり得る。
    ///   Reserved … ある猫が Goal 決定と同時に予約した。到達前でも他の猫は選べない。
    ///   Occupied … 予約した猫が到達し、実際に使用中。
    ///
    /// 状態は同期しない(仕様 8章)。全猫の Udon Owner を同一クライアントに
    /// 揃えることで、占有判断は 1 クライアント内のローカル状態として完結させる。
    /// </summary>
    public enum PointOccupancy
    {
        Free = 0,
        Reserved = 1,
        Occupied = 2,
    }
}
