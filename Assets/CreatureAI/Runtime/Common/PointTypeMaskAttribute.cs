using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// PointType(Flags)フィールドを、[InspectorName] の日本語ラベル付きの
    /// チェックボックス(マスク)として描画させるためのマーカー属性。
    ///
    /// 実際の描画は Editor 側の PointTypeMaskDrawer が行う。ランタイムには
    /// 影響しない(UdonSharp のコンパイル対象外の純メタデータ)。
    /// PropertyAttribute は UnityEngine 側の型なので Runtime に置く。
    /// </summary>
    public class PointTypeMaskAttribute : PropertyAttribute
    {
    }
}
