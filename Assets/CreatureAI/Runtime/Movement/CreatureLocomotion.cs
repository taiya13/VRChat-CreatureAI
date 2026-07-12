using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// 移動の「障害物回避ステアリング」を集約する軽量コンポーネント(Phase 9)。
    ///
    /// [責務] 「本当は行きたい方向(desiredDir)」を受け取り、Raycast で前方の障害物までの
    ///        距離を測って、実際に進んでよい変位を返す。それだけ。
    ///        目的地の決定・徘徊・逃走の判断は持たない(それは MovementController)。
    ///        欲求・意思決定・危険検知にも一切触れない(Brain/Needs/Threat は無改造)。
    ///
    /// [家具のある室内でも固まらない設計]
    ///   ・「障害物までの距離」を測り、その手前 clearance までは進める分だけ進む(にじり寄り)。
    ///     「probeDistance 先まで空いていないと進まない」方式だと、家具に囲まれた室内では
    ///     全方向ふさがり扱いになり完全停止するため。
    ///   ・lookAhead(目的地までの残り距離)より先の障害物は無視する。これが無いと
    ///     「壁際に置かれた餌皿」に永遠に到達できない。
    ///   ・トリガーコライダー(ドアやギミックの反応領域)と自分自身の体は障害物にしない。
    ///
    /// [アルゴリズム] "ウィスカー(触角)ステアリング + にじり寄り":
    ///   1. 正面へ1本 Raycast。十分空いていれば desiredDir へそのまま前進。
    ///   2. ふさがれていれば、左右へ角度を段階的に広げながら Raycast し、
    ///      最初に「十分空いた」側へ向く(壁沿いに回り込む)。
    ///      一度避け始めた側を短時間優先して、ジグザグ振動を抑える。
    ///   3. どの方向も十分には空いていなければ、最も遠くまで空いている方向へ
    ///      「障害物の clearance 手前まで」進む(=詰まらず、貫通もしない)。
    ///   4. clearance 以内まで全方向ふさがれている時だけ停止する。
    ///   1フレームの Raycast は最大 1+2×probeSteps 本で UdonSharp でも軽量。
    ///
    /// [将来の発展] 「方向を入力→安全な変位を出力」の純ステアリングなので、上位に
    ///        Waypoint/NavMesh 経路を足す場合は、経路上の次の点への方向を desiredDir として
    ///        渡すだけでよい(本クラスは無改造で局所回避を担い続ける)。
    ///
    /// Cat のルート(MovementController と同じ GameObject)に付ける。
    /// </summary>
    public class CreatureLocomotion : UdonSharpBehaviour
    {
        [Header("障害物回避")]
        [Tooltip("障害物回避を有効にする。OFF なら従来どおり最短方向へ直進する。")]
        public bool avoidObstacles = true;

        [Tooltip("障害物とみなすレイヤー。空(Nothing)のときは全レイヤーを障害物として扱う。" +
                 "実ワールドでは壁・家具のレイヤーに限定するのを推奨" +
                 "(プレイヤーや Pickup を含めると誤って避けるため)。")]
        public LayerMask obstacleMask;

        [Tooltip("前方をどこまで先読みして障害物を探すか(m)。長いほど早めに曲がる。")]
        [Min(0.1f)]
        public float probeDistance = 1.5f;

        [Tooltip("障害物へどこまで近づいてよいか(m)。体の半径より少し大きく。")]
        [Min(0.05f)]
        public float clearance = 0.25f;

        [Tooltip("Raycast を出す高さ(足元からの m)。体の中心あたりが目安。")]
        public float rayHeight = 0.3f;

        [Tooltip("左右に振って探す最大角度(度)。90 なら真横まで探す。")]
        [Range(10f, 170f)]
        public float maxAvoidAngle = 90f;

        [Tooltip("左右それぞれ何段階の角度で探すか。多いほど滑らかだが Raycast が増える。")]
        [Range(1, 6)]
        public int probeSteps = 3;

        [Tooltip("一度避け始めた側を優先し続ける秒数(ジグザグ振動を抑える)。")]
        [Min(0f)]
        public float turnCommitTime = 0.6f;

        // --- 診断用に最後の探査結果を保持(Editor Gizmo が読む / Inspector 非表示) ---
        [HideInInspector] public Vector3 lastOrigin;
        [HideInInspector] public Vector3 lastDesiredDir;
        [HideInInspector] public Vector3 lastChosenDir;
        [HideInInspector] public bool lastBlocked;

        // 避けている側(-1=左 / +1=右 / 0=未コミット)と、その保持期限。
        private int avoidSign = 0;
        private float avoidCommitUntil = 0f;

        /// <summary>
        /// desiredDir(正規化した水平方向)へ stepLen だけ進みたい。障害物を避けた実際の変位を返す。
        /// lookAhead は目的地までの残り距離(それより先の障害物は無視する)。上限が無ければ
        /// Infinity を渡す。障害物が無ければ desiredDir*stepLen をそのまま返し、clearance 以内まで
        /// 全方向ふさがれている時だけ Vector3.zero を返す(=貫通しない)。
        /// </summary>
        public Vector3 ComputeMove(Vector3 pos, Vector3 desiredDir, float stepLen, float lookAhead)
        {
            lastDesiredDir = desiredDir;
            lastOrigin = pos + Vector3.up * rayHeight;

            if (!avoidObstacles || stepLen <= 0f || desiredDir.sqrMagnitude < 0.0001f)
            {
                lastChosenDir = desiredDir;
                lastBlocked = false;
                return desiredDir * stepLen;
            }

            // 実効の先読み距離: 目的地より先の壁は気にしない(壁際の地点に到達できるように)。
            // ただし「今回の歩幅 + clearance」より短くはしない(最低限の安全確認)。
            float probe = probeDistance;
            if (lookAhead < probe) probe = lookAhead;
            float minProbe = clearance + stepLen;
            if (probe < minProbe) probe = minProbe;

            // 1. 正面が十分空いていれば素直に前進(避けコミット解除)。
            float dFwd = FreeDistance(lastOrigin, desiredDir, probe);
            if (dFwd >= probe)
            {
                avoidSign = 0;
                lastBlocked = false;
                lastChosenDir = desiredDir;
                return desiredDir * stepLen;
            }

            lastBlocked = true;
            float now = Time.time;
            int preferred = (now < avoidCommitUntil) ? avoidSign : 0; // 直近で避けた側を優先

            // にじり寄り用: これまでで最も遠くまで空いている方向を記録しておく。
            Vector3 bestDir = desiredDir;
            float bestFree = dFwd;

            // 2. 角度を段階的に広げ、左右で「十分空いた」方向を探す。
            float stepDeg = maxAvoidAngle / probeSteps;
            for (int i = 1; i <= probeSteps; i++)
            {
                float ang = stepDeg * i;
                Vector3 left = RotateY(desiredDir, -ang);
                Vector3 right = RotateY(desiredDir, ang);
                float dL = FreeDistance(lastOrigin, left, probe);
                float dR = FreeDistance(lastOrigin, right, probe);

                if (dL >= probe && dR >= probe)
                {
                    // 両方空き → 優先側を維持。未コミットなら個体差のためランダムに決める。
                    int sign = preferred;
                    if (sign == 0) sign = (Random.value < 0.5f) ? -1 : 1;
                    Commit(sign, now);
                    lastChosenDir = (sign < 0) ? left : right;
                    return lastChosenDir * stepLen;
                }
                if (dL >= probe) { Commit(-1, now); lastChosenDir = left; return left * stepLen; }
                if (dR >= probe) { Commit(1, now); lastChosenDir = right; return right * stepLen; }

                if (dL > bestFree) { bestFree = dL; bestDir = left; }
                if (dR > bestFree) { bestFree = dR; bestDir = right; }
            }

            // 3. どの方向も十分には空いていない(家具の多い室内など)。
            //    最も空いている方向へ「障害物の clearance 手前まで」進む(にじり寄り)。
            float creep = bestFree - clearance;
            if (creep <= 0.005f)
            {
                lastChosenDir = Vector3.zero;
                return Vector3.zero; // 本当に囲まれている → 停止(貫通しない)
            }
            if (creep > stepLen) creep = stepLen;
            lastChosenDir = bestDir;
            return bestDir * creep;
        }

        private void Commit(int sign, float now)
        {
            avoidSign = sign;
            avoidCommitUntil = now + turnCommitTime;
        }

        /// <summary>
        /// origin から dir 方向に、どこまで空いているか(m)を返す(最大 maxDist)。
        /// トリガーコライダーと自分自身(Cat 配下)のコライダーは障害物にしない。
        /// </summary>
        private float FreeDistance(Vector3 origin, Vector3 dir, float maxDist)
        {
            int mask = obstacleMask.value;
            if (mask == 0) mask = ~0; // 未設定なら全レイヤー

            RaycastHit hit;
            if (!Physics.Raycast(origin, dir, out hit, maxDist, mask)) return maxDist;

            Collider col = hit.collider;
            if (col != null)
            {
                if (col.isTrigger) return maxDist;          // 反応領域は通り抜けてよい
                if (IsOwnChild(hit.transform)) return maxDist; // 自分の体(モデルのコライダー)は無視
            }
            return hit.distance;
        }

        /// <summary>t が自分(Cat ルート)配下の Transform なら true。</summary>
        private bool IsOwnChild(Transform t)
        {
            int guard = 0;
            while (t != null && guard < 16)
            {
                if (t == transform) return true;
                t = t.parent;
                guard++;
            }
            return false;
        }

        /// <summary>水平ベクトルを Y 軸まわりに angle 度回す(Quaternion を使わない軽量版)。</summary>
        private Vector3 RotateY(Vector3 v, float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return new Vector3(v.x * cos - v.z * sin, 0f, v.x * sin + v.z * cos);
        }
    }
}
