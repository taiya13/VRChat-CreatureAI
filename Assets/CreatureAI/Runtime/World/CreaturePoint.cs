using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// Layer 0(ワールドコンテンツ層): ワールド制作者がシーン上のオブジェクトに付与する
    /// 「猫が利用できる地点」。餌皿・水飲み場・ベッド等がこれにあたる。
    ///
    /// [Inspector で設定する静的属性]  種類 / 評価値 / 使用可否 / 検索半径
    /// [ランタイムの占有状態(仕様 3.5節)]  occupancy / holder / reservedAt
    ///
    /// 起動時に自分を稼働中の CreaturePointRegistry へ自己登録する。
    /// 占有は Reserve → Occupy → Release の順に遷移し、全ての中断は Release を通す。
    /// </summary>
    public class CreaturePoint : UdonSharpBehaviour
    {
        // 稼働中レジストリのシーン上の名前(仕様 6章)。全クラスで一致させる。
        private const string RegistryObjectName = "__CatAI_Registry";

        [Header("種類 / 属性")]
        [PointTypeMask]
        [Tooltip("この地点の種類。複数選択可。少なくとも1つ選ぶこと。")]
        public PointType pointType = PointType.None;

        [Header("評価")]
        [Tooltip("この地点の基礎的な魅力度。高いほど選ばれやすい(意思決定は Phase 2)。")]
        public float evaluationValue = 1.0f;

        [Tooltip("false の間は候補から除外される(工事中・破損などの表現)。")]
        public bool isUsable = true;

        [Header("検索")]
        [Min(0.5f)]
        [Tooltip("猫がこの地点を『目的地の候補』として選べる最大距離(m)。" +
                 "これより遠いと候補から外れる。既定は広め(50m)。狭くすると近くの地点しか選ばない。")]
        public float searchRadius = 50.0f;

        [Header("移行管理")]
        [Tooltip("将来タグ体系を変更する際の移行用。通常は変更しない(仕様 5章)。")]
        public int configVersion = 1;

        // --- ランタイム占有状態(Inspector 非公開 / 仕様 3.5節) ---
        private PointOccupancy occupancy = PointOccupancy.Free;
        private UdonSharpBehaviour holder = null;   // 予約/占有している ActionRunner(Phase 2)
        private float reservedAt = 0f;              // Reserve した時刻(Time.time)

        void Start()
        {
            RegisterToRegistry();
        }

        /// <summary>稼働中レジストリを探して自己登録する(仕様 1章 / 6章)。</summary>
        private void RegisterToRegistry()
        {
            GameObject go = GameObject.Find(RegistryObjectName);
            if (go == null)
            {
                Debug.LogWarning("[CreaturePoint] Registry '" + RegistryObjectName +
                    "' が見つかりません。猫 Prefab がシーンに存在するか確認してください: " + name);
                return;
            }
            CreaturePointRegistry registry = go.GetComponent<CreaturePointRegistry>();
            if (registry != null)
            {
                registry.Register(this);
            }
        }

        // ================= 占有 API(仕様 3.5節) =================

        /// <summary>
        /// 予約する。Goal 決定と同時に呼ぶ。Free のときのみ成功し true を返す。
        /// 到達時初占有ではなく「予約先取り」にすることで、2匹が同一地点へ
        /// 同時に動き出す挙動を防ぐ(仕様 3.5「設計上の原則」)。
        /// </summary>
        public bool Reserve(UdonSharpBehaviour newHolder)
        {
            if (occupancy != PointOccupancy.Free) return false;
            occupancy = PointOccupancy.Reserved;
            holder = newHolder;
            reservedAt = Time.time;
            return true;
        }

        /// <summary>到達判定が出たときに呼ぶ。Reserved → Occupied。</summary>
        public void Occupy()
        {
            occupancy = PointOccupancy.Occupied;
        }

        /// <summary>
        /// 解放する。行動完了・AbortCurrent・タイムアウトの全経路がここを通る。
        /// これにより「解放漏れ」の経路が構造的に生じないようにする。
        /// </summary>
        public void Release()
        {
            occupancy = PointOccupancy.Free;
            holder = null;
        }

        // ================= 参照用ヘルパー =================

        public PointOccupancy GetOccupancy() { return occupancy; }
        public UdonSharpBehaviour GetHolder() { return holder; }
        public float GetReservedAt() { return reservedAt; }
        public bool IsHeldBy(UdonSharpBehaviour who) { return holder == who; }

        /// <summary>予約者の名前(表示用)。未予約なら "-"。</summary>
        public string GetHolderName() { return holder != null ? holder.name : "-"; }

        /// <summary>要求種別のいずれかを、この地点が備えているか。</summary>
        public bool MatchesType(PointType required)
        {
            return ((int)pointType & (int)required) != 0;
        }

        /// <summary>
        /// 予約が指定秒数を超過しているか(Registry の低頻度掃除が使う安全網)。
        /// </summary>
        public bool IsReservationExpired(float timeout)
        {
            return occupancy == PointOccupancy.Reserved && (Time.time - reservedAt) >= timeout;
        }
    }
}
