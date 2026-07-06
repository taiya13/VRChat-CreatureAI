using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// Layer 1(知覚層): 全 CreaturePoint の登録先(仕様 3.3節 / 6章)。
    ///
    /// Cat Prefab に子オブジェクト "__CatAI_Registry" として最初から同梱される。
    /// 複数の猫を置くと Registry も複数存在するが、起動時の「自己選出」により
    /// 実際に稼働するのは 1 インスタンスだけになる(ランタイム生成は行わない)。
    ///
    /// 自己選出ロジック:
    ///   GameObject.Find("__CatAI_Registry") が返すのは常にヒエラルキー上で最初の
    ///   1 個。各 Registry が Find し、「返ってきたのが自分自身」だった 1 個だけが
    ///   singleton になる。CreaturePoint / CreaturePointSensor も同じ Find で
    ///   同一インスタンスに到達するため、参照が食い違わない。
    /// </summary>
    public class CreaturePointRegistry : UdonSharpBehaviour
    {
        // 稼働中レジストリのシーン上の名前。全クラスで一致させる。
        private const string RegistryObjectName = "__CatAI_Registry";

        // FindBestCandidate の距離ペナルティ係数(評価値からの減算)。
        private const float DistancePenalty = 0.1f;

        private CreaturePoint[] points = new CreaturePoint[16];
        private int pointCount = 0;
        private bool isActiveSingleton = false;

        void Start()
        {
            // --- 自己選出(仕様 6章) ---
            GameObject found = GameObject.Find(RegistryObjectName);
            if (found == this.gameObject)
            {
                isActiveSingleton = true;
                Debug.Log("[CreaturePointRegistry] Singleton に選出されました: " + GetPath());
            }
            else
            {
                isActiveSingleton = false;
                // 以後は完全に待機。登録も検索も稼働 singleton 側に集約されるため何もしない。
                Debug.Log("[CreaturePointRegistry] 待機(別インスタンスが稼働中): " + GetPath());
            }
        }

        // ================= 登録 API =================

        public void Register(CreaturePoint point)
        {
            if (point == null) return;
            EnsureArray();

            // 重複登録の防止。
            for (int i = 0; i < pointCount; i++)
            {
                if (points[i] == point) return;
            }

            if (pointCount >= points.Length) GrowArray();
            points[pointCount] = point;
            pointCount++;

            Debug.Log("[CreaturePointRegistry] 登録: " + point.name + " (合計 " + pointCount + " 個)");
        }

        public void Unregister(CreaturePoint point)
        {
            for (int i = 0; i < pointCount; i++)
            {
                if (points[i] == point)
                {
                    // 末尾要素で穴を埋めるスワップ削除(順序は保持しない)。
                    points[i] = points[pointCount - 1];
                    points[pointCount - 1] = null;
                    pointCount--;
                    return;
                }
            }
        }

        // ================= 検索 API =================

        /// <summary>
        /// 指定位置から見て最良の候補地点を返す(意思決定層が使う / Phase 2)。
        /// 使用不可・占有済み・種別不一致・検索半径外は除外し、
        /// (評価値 − 距離ペナルティ)が最大の地点を選ぶ。該当なしは null。
        /// </summary>
        public CreaturePoint FindBestCandidate(Vector3 fromPosition, PointType requiredTypes)
        {
            CreaturePoint best = null;
            float bestScore = -1000000f;

            for (int i = 0; i < pointCount; i++)
            {
                CreaturePoint p = points[i];
                if (p == null) continue;
                if (!p.isUsable) continue;
                if (p.GetOccupancy() != PointOccupancy.Free) continue;   // 占有中は除外(仕様 3.5)
                if (!p.MatchesType(requiredTypes)) continue;

                float dist = Vector3.Distance(fromPosition, p.transform.position);
                if (dist > p.searchRadius) continue;

                float score = p.evaluationValue - dist * DistancePenalty;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>
        /// 指定位置の周囲にある(検索半径内・使用可能な)候補地点を buffer に詰め、
        /// その個数を返す。CreaturePointSensor が近傍候補をキャッシュするのに使う。
        /// 種別による絞り込みは行わない(それは意思決定層の役割 / Phase 2)。
        /// </summary>
        public int CollectNearby(Vector3 fromPosition, CreaturePoint[] buffer)
        {
            if (buffer == null) return 0;
            int n = 0;
            for (int i = 0; i < pointCount && n < buffer.Length; i++)
            {
                CreaturePoint p = points[i];
                if (p == null) continue;
                if (!p.isUsable) continue;

                float dist = Vector3.Distance(fromPosition, p.transform.position);
                if (dist > p.searchRadius) continue;

                buffer[n] = p;
                n++;
            }
            return n;
        }

        /// <summary>
        /// タイムアウトした予約を一括解放する安全網(仕様 3.5節)。
        /// CreatureCore の低頻度 Tick(tickCounter % 10)から呼ばれる。
        /// </summary>
        public void CleanupExpiredReservations(float timeout)
        {
            for (int i = 0; i < pointCount; i++)
            {
                CreaturePoint p = points[i];
                if (p == null) continue;
                if (p.IsReservationExpired(timeout))
                {
                    p.Release();
                    Debug.Log("[CreaturePointRegistry] 予約タイムアウトのため解放: " + p.name);
                }
            }
        }

        // ================= 状態参照 =================

        public bool IsActiveSingleton() { return isActiveSingleton; }
        public int GetPointCount() { return pointCount; }

        // ================= 内部ヘルパー =================

        private void EnsureArray()
        {
            if (points == null) points = new CreaturePoint[16];
        }

        private void GrowArray()
        {
            CreaturePoint[] bigger = new CreaturePoint[points.Length * 2];
            for (int i = 0; i < pointCount; i++) bigger[i] = points[i];
            points = bigger;
        }

        private string GetPath()
        {
            // 親をたどって "Cat/__CatAI_Registry" 形式のパスを作る(ログ判別用)。
            Transform parent = transform.parent;
            if (parent != null) return parent.name + "/" + name;
            return name;
        }
    }
}
