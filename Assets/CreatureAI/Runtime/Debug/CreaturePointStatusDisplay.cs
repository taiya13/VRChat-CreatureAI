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

        private readonly Color freeColor = new Color(0.4f, 1f, 0.5f);
        private readonly Color reservedColor = new Color(1f, 0.85f, 0.25f);
        private readonly Color occupiedColor = new Color(1f, 0.4f, 0.4f);

        void Update()
        {
            if (targetText == null || point == null) return;

            PointOccupancy occ = point.GetOccupancy();

            string state;
            Color col;
            if (occ == PointOccupancy.Occupied) { state = "Occupied"; col = occupiedColor; }
            else if (occ == PointOccupancy.Reserved) { state = "Reserved"; col = reservedColor; }
            else { state = "Free"; col = freeColor; }

            string suffix = (occ != PointOccupancy.Free) ? ("\nby " + point.GetHolderName()) : "";
            targetText.text = point.name + "\n<b>" + state + "</b>" + suffix;
            targetText.color = col;
        }
    }
}
