namespace CreatureAI
{
    /// <summary>
    /// 猫(エージェント)の行動ライフサイクルの状態。
    ///
    ///   Idle   … 目的地なし・待機中
    ///   Moving … TargetPoint へ移動中(まだ到着していない)
    ///   Acting … TargetPoint に到着して行動中(食事/睡眠など)
    ///
    /// これは「今どのフェーズか」を明示するための観測用の状態であり、判断はしない
    /// (判断は Brain、実行は ActionRunner)。将来の割込み(Flee/危険回避)やアニメ接続が
    /// この状態を参照して分岐しやすくするための土台。
    /// </summary>
    public enum AgentState
    {
        Idle = 0,
        Moving = 1,
        Acting = 2,
    }
}
