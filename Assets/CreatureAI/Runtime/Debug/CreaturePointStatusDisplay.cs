using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace CreatureAI
{
    /// <summary>
    /// CreaturePoint の占有状態を、そのオブジェクトの上にリアルタイム表示する
    /// (Scene ビューの Gizmo と違い、Play / ワールド内でも見える)。
    ///
    /// 参照した CreaturePoint の GetOccupancy() を毎フレーム読み、Free/Reserved/Occupied を
    /// 色つきで表示する。判断はしない(表示専用)。
    /// </summary>
    public class CreaturePointStatusDisplay : UdonSharpBehaviour
    {
        [Tooltip("表示先の UI Text(World Space Canvas 上)。")]
        public Text targetText;

        [Tooltip("状態を読み取る対象の CreaturePoint。")]
        public CreaturePoint point;

        void Update()
        {
            if (targetText == null || point == null) return;

            PointOccupancy occ = point.GetOccupancy();

            // 色は UI.Text.color の setter(Udon 非対応の可能性)を避け、リッチテキストで埋め込む。
            string state;
            string hex;
            if (occ == PointOccupancy.Occupied) { state = "Occupied"; hex = "FF6666"; }
            else if (occ == PointOccupancy.Reserved) { state = "Reserved"; hex = "FFD940"; }
            else { state = "Free"; hex = "7CFF7C"; }

            string suffix = (occ != PointOccupancy.Free) ? ("\nby " + point.GetHolderName()) : "";
            targetText.text = point.name + "\n<color=#" + hex + "><b>" + state + "</b></color>" + suffix;
        }
    }
}
