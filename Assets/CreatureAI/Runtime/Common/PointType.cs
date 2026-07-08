using System;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// CreaturePoint の「種類」。複数種類を1つのポイントに持たせられるよう [Flags] とする。
    ///
    /// 表示名は ScriptableObject 等を介さず、enum 宣言に直接付与した [InspectorName]
    /// (Unity 標準属性)で管理する(仕様 5章)。
    ///
    /// 値(1 &lt;&lt; n)は将来にわたり固定する。並び順や表示名を変えても、
    /// 保存済みのマスク値が壊れないようにするため。
    /// </summary>
    [Flags]
    public enum PointType
    {
        None = 0,

        [InspectorName("餌")] Food = 1 << 0,
        [InspectorName("水")] Water = 1 << 1,
        [InspectorName("ベッド")] Bed = 1 << 2,
        [InspectorName("おもちゃ")] Toy = 1 << 3,
        [InspectorName("日向")] Sunny = 1 << 4,
        [InspectorName("高い場所")] HighPlace = 1 << 5,
        [InspectorName("隠れ場所")] Hideout = 1 << 6,

        // --- 将来拡張用の予約(値は変更しない) ---
        ScratchPost = 1 << 7,  // 爪とぎ
        Litterbox = 1 << 8,    // トイレ
        Window = 1 << 9,       // 窓
        Sofa = 1 << 10,        // ソファ
        Fireplace = 1 << 11,   // 暖炉
    }
}
