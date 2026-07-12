using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// 移動の「障害物回避ステアリング」を集約する軽量コンポーネント(Phase 9)。
    ///
    /// [責務] 「本当は行きたい方向(desiredDir)」を受け取り、Raycast で前方の障害物を検知して、
    ///        塞がれていれば左右に振って通れる方向を選び、実際に進んでよい変位を返す。
    ///        それだけ。目的地の決定・徘徊・逃走の判断は持たない(それは MovementController)。
    ///        欲求・意思決定・危険検知にも一切触れない(Brain/Needs/Threat は無改造)。
    ///
    /// [両立の考え方] desiredDir(目的地/徘徊/逃走のいずれか)を「入力」、回避を「補正」とする。
    ///        正面が空いていれば desiredDir をそのまま返すので、障害物が無ければ従来どおり最短で進む。
    ///        塞がれている間だけ迂回方向へ寄せ、空けば再び desiredDir に戻る(＝目的地へ復帰)。
    ///
    /// [アルゴリズム] "ウィスカー(触角)ステアリング":
    ///        1. 正面へ1本 Raycast。当たらなければそのまま前進。
    ///        2. 当たれば、左右へ角度を段階的に広げながら Raycast し、最初に空いた側へ向く。
    ///        3. 一度避け始めた側を短時間優先して、ジグザグ振動を抑える(自然な壁沿い)。
    ///        4. 全方向が塞がれていれば変位ゼロ(＝その場で停止＝壁を貫通しない)。
    ///        1フレームの Raycast は「空いていれば1本、塞がれていても最大 1+2×probeSteps 本」で、
    ///        VRChat/UdonSharp でも十分軽量。
    ///
    /// [将来の発展] このクラスは「方向を入力→安全な方向を出力」する純ステアリング。
    ///        上位に Waypoint 追従や NavMesh 経路を足す場合は、経路上の次の点への方向を
    ///        desiredDir として渡すだけでよい(本クラスは無改造で局所回避を担い続ける)。
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

        [Tooltip("前方をどこまで先読みして障害物を探すか(m)。歩幅より十分長くする。")]
        [Min(0.1f)]
        public float probeDistance = 1.5f;

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
        /// 障害物が無ければ desiredDir*stepLen をそのまま返す。全方向塞がれていれば Vector3.zero。
        /// </summary>
        public Vector3 ComputeMove(Vector3 pos, Vector3 desiredDir, float stepLen)
        {
            lastDesiredDir = desiredDir;
            lastOrigin = pos + Vector3.up * rayHeight;

            if (!avoidObstacles || stepLen <= 0f || desiredDir.sqrMagnitude < 0.0001f)
            {
                lastChosenDir = desiredDir;
                lastBlocked = false;
                return desiredDir * stepLen;
            }

            Vector3 dir = ChooseDirection(lastOrigin, desiredDir);
            lastChosenDir = dir;

            if (dir.sqrMagnitude < 0.0001f)
            {
                lastBlocked = true;
                return Vector3.zero; // 塞がれている → 進まない(壁を貫通しない)
            }
            return dir * stepLen;
        }

        /// <summary>正面が空いていれば desiredDir。塞がれていれば左右に振って空いた方向を選ぶ。</summary>
        private Vector3 ChooseDirection(Vector3 origin, Vector3 desired)
        {
            // 1. 正面が空いていれば素直に前進(避けコミットを解除)。
            if (IsClear(origin, desired))
            {
                avoidSign = 0;
                lastBlocked = false;
                return desired;
            }
            lastBlocked = true;

            float now = Time.time;
            int preferred = (now < avoidCommitUntil) ? avoidSign : 0; // 直近で避けた側を優先

            // 2. 角度を段階的に広げ、左右で空いている方向を探す。
            float stepDeg = maxAvoidAngle / probeSteps;
            for (int i = 1; i <= probeSteps; i++)
            {
                float ang = stepDeg * i;
                Vector3 left = RotateY(desired, -ang);
                Vector3 right = RotateY(desired, ang);
                bool lc = IsClear(origin, left);
                bool rc = IsClear(origin, right);

                if (lc && rc)
                {
                    // 両方空き → 優先側を維持。未コミットなら個体差のためランダムに決める。
                    int sign = preferred;
                    if (sign == 0) sign = (Random.value < 0.5f) ? -1 : 1;
                    Commit(sign, now);
                    return (sign < 0) ? left : right;
                }
                if (lc) { Commit(-1, now); return left; }
                if (rc) { Commit(1, now); return right; }
            }

            // 3. 全方向塞がれている。
            return Vector3.zero;
        }

        private void Commit(int sign, float now)
        {
            avoidSign = sign;
            avoidCommitUntil = now + turnCommitTime;
        }

        /// <summary>origin から dir 方向 probeDistance 以内に障害物が無ければ true。</summary>
        private bool IsClear(Vector3 origin, Vector3 dir)
        {
            // マスクが空(0)のときは全レイヤーを障害物として扱う(初期状態でも回避が効くように)。
            int mask = obstacleMask.value;
            if (mask == 0) mask = ~0;

            // UdonSharp で確実に通る 5 引数の Raycast(QueryTriggerInteraction は使わない)。
            RaycastHit hit;
            return !Physics.Raycast(origin, dir, out hit, probeDistance, mask);
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
