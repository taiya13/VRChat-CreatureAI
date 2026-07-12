using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// Layer 5(身体制御層): TargetPoint へ向かって簡易ステアリングで移動し、到着を判定する
    /// (仕様の CreatureLocomotion に相当)。移動の滑らかさが要るため Tick ではなく Update を使う。
    ///
    /// [責務] TargetSelector が保持する TargetPoint へ、moveSpeed で水平移動し、
    ///        進行方向へ向きを回頭。StopDistance 以内に入ったら停止し「到着」を1回だけ通知(ログ)。
    ///        食事・睡眠・アニメ・Action 実行は持たない(次フェーズ)。
    ///
    /// [接続設計] 到着状態は IsAtTarget() で公開する。後で ActionRunner が
    ///        「IsAtTarget() が true になったら Occupy → 行動 → Satisfy → Release」と繋げる。
    ///        MovementController 自身は占有状態を触らない(責務分離)。
    ///
    /// Cat のルート(TargetSelector と同じ GameObject)に付ける。root を動かすので
    /// 子(Body/HUD 等)も一緒に移動する。
    /// </summary>
    public class MovementController : UdonSharpBehaviour
    {
        [Tooltip("この距離以内に入ったら『到着』とみなして停止する(m)。")]
        public float stopDistance = 0.6f;

        [Tooltip("向きを変える速さ(度/秒)。")]
        public float turnSpeed = 360f;

        [Tooltip("moveSpeed の取得元が無い場合のフォールバック速度(m/s)。")]
        public float fallbackSpeed = 1.5f;

        [Tooltip("逃走時の速度倍率(通常移動速度に掛ける)。")]
        public float fleeSpeedMultiplier = 1.6f;

        [Header("徘徊(Idle 時のうろうろ)")]
        [Tooltip("徘徊する範囲の半径(m)。起点(spawn 位置)からこの範囲でランダムに歩き回る。")]
        public float wanderRadius = 4f;
        [Tooltip("徘徊時の速度倍率(通常よりゆっくり歩く)。")]
        public float wanderSpeedMultiplier = 0.5f;
        [Tooltip("徘徊の到着判定距離(m)。")]
        public float wanderStopDistance = 0.4f;
        [Tooltip("徘徊で立ち止まって休む秒数(最小/最大)。")]
        public float wanderPauseMin = 1.5f;
        public float wanderPauseMax = 5.0f;

        [Header("逃走の自然さ")]
        [Tooltip("逃走方向を『真逆』からどれだけランダムにずらすか(度)。")]
        public float fleeAngleMax = 70f;
        [Tooltip("逃走方向を切り替える間隔(秒・最小/最大)。ジグザグに逃げる。")]
        public float fleeRedirectMin = 0.4f;
        public float fleeRedirectMax = 1.2f;

        private CreatureTargetSelector targetSelector;
        private CreatureProfile profile;
        private CreatureBrain brain;
        private ThreatEvaluator threat;
        private CreaturePersonality personality;
        private CreatureLocomotion locomotion; // 障害物回避ステアリング(あれば移動方向を補正)
        private bool debugLog = true;

        private CreaturePoint lastTarget = null;
        private bool arrived = false;

        // 徘徊の状態。
        private Vector3 home;
        private Vector3 wanderTarget;
        private bool hasWanderTarget = false;
        private bool wanderActive = false;   // 徘徊で歩いている最中か(休憩中は false)
        private float wanderPauseUntil = 0f;

        // 逃走の状態。
        private Vector3 fleeDir;
        private float fleeRedirectAt = 0f;

        void Start()
        {
            // 自己初期化(Update 駆動なので Core 注入に依存せず自前で参照を取る)。
            targetSelector = GetComponent<CreatureTargetSelector>();
            profile = GetComponentInChildren<CreatureProfile>();
            brain = GetComponent<CreatureBrain>();
            threat = GetComponent<ThreatEvaluator>();
            personality = GetComponent<CreaturePersonality>(); // 活発さ/好奇心/臆病さ が移動に影響
            locomotion = GetComponent<CreatureLocomotion>();   // 無ければ従来どおり直進(null 安全)
            CreatureCore core = GetComponent<CreatureCore>();
            if (core != null) debugLog = core.debugLog;

            home = transform.position; // 徘徊の起点
        }

        void Update()
        {
            if (targetSelector == null) return;

            // 逃走中は TargetPoint を使わず、脅威源から離れる方向へ走る。
            if (brain != null && threat != null &&
                brain.GetCurrentGoal() == Goal.Flee && threat.IsThreatened())
            {
                FleeFrom(threat.GetThreatPosition());
                return;
            }

            CreaturePoint tp = targetSelector.GetTargetPoint();
            if (tp == null)
            {
                // ターゲット無し = Idle。猫らしく、起点の周りを不規則にうろうろする。
                lastTarget = null;
                arrived = false;
                WanderUpdate();
                return;
            }

            // ターゲットが変わったら到着判定をやり直す。
            if (tp != lastTarget)
            {
                lastTarget = tp;
                arrived = false;
            }

            Vector3 pos = transform.position;
            Vector3 target = tp.transform.position;

            // 水平面(XZ)だけで距離・方向を扱う(地面を歩く生き物のため上下は無視)。
            Vector3 flat = new Vector3(target.x - pos.x, 0f, target.z - pos.z);
            float dist = flat.magnitude;

            if (dist <= stopDistance)
            {
                if (!arrived)
                {
                    arrived = true;
                    if (debugLog) Debug.Log("[Move] " + name + " arrived at '" + tp.name + "' (dist=" +
                        (Mathf.Round(dist * 100f) / 100f) + "m)");
                    // 到着イベント。今は通知のみ。ActionRunner はここ / IsAtTarget() を起点に繋ぐ。
                }
                return; // 停止
            }

            // --- 移動(障害物回避込み) ---
            float speed = BaseSpeed();
            Vector3 dir = flat / dist; // 正規化した水平方向(目的地方向)
            Vector3 disp = StepWithAvoidance(pos, dir, speed, dist);
            transform.position = pos + disp;

            // --- 向き(実際に進んだ方向へ回頭。回避で曲がれば自然にそちらを向く) ---
            FaceMovement(disp, dir);
        }

        /// <summary>現在の TargetPoint に到着済みか(後フェーズの ActionRunner が参照)。</summary>
        public bool IsAtTarget() { return arrived; }

        /// <summary>
        /// 到着状態をリセットする(ActionRunner.AbortCurrent から呼ばれる)。
        /// 中断後に同じ地点を選び直しても、再度きちんと「移動→到着」を踏むようにする。
        /// </summary>
        public void ResetArrival()
        {
            arrived = false;
            lastTarget = null;
        }

        /// <summary>移動中(ターゲットあり・未到着)か。</summary>
        public bool IsMoving() { return targetSelector != null && targetSelector.HasTarget() && !arrived; }

        /// <summary>Idle 徘徊で歩いている最中か(ActionRunner が状態を Moving にするのに使う)。</summary>
        public bool IsWandering() { return wanderActive; }

        // ================= 徘徊(Idle Wander) =================

        /// <summary>
        /// 起点(home)の周りをランダムに歩き回る。到着したらしばらく休み、また別の地点へ。
        /// 一定範囲(wanderRadius)に収まるので遠くへ行き過ぎない。
        /// </summary>
        private void WanderUpdate()
        {
            float now = Time.time;

            // 目的地が無い = 休憩中 or 次の目的地を決める。
            if (!hasWanderTarget)
            {
                if (now < wanderPauseUntil) { wanderActive = false; return; } // 立ち止まって休む
                PickWanderTarget();
            }

            Vector3 pos = transform.position;
            Vector3 flat = new Vector3(wanderTarget.x - pos.x, 0f, wanderTarget.z - pos.z);
            float dist = flat.magnitude;

            if (dist <= wanderStopDistance)
            {
                // 到着 → しばらく休む(のんびり/好奇心で休憩時間が変わる)。
                hasWanderTarget = false;
                wanderActive = false;
                float pmult = (personality != null) ? personality.GetWanderPauseMult() : 1f;
                wanderPauseUntil = now + Random.Range(wanderPauseMin, wanderPauseMax) * pmult;
                return;
            }

            wanderActive = true;
            float speed = BaseSpeed() * wanderSpeedMultiplier;
            Vector3 dir = flat / dist;
            Vector3 disp = StepWithAvoidance(pos, dir, speed, dist);

            // 徘徊先が家具の中・壁の向こう等でほぼ進めない → その目的地は諦めて選び直す
            // (徘徊は「どこでもよい散歩」なので、無理に到達しようとして固まらせない)。
            float expected = speed * Time.deltaTime;
            if (disp.magnitude < expected * 0.25f)
            {
                hasWanderTarget = false;
                wanderActive = false;
                float pmult = (personality != null) ? personality.GetWanderPauseMult() : 1f;
                wanderPauseUntil = Time.time + Random.Range(wanderPauseMin, wanderPauseMax) * 0.5f * pmult;
                return;
            }

            transform.position = pos + disp;
            FaceMovement(disp, dir);
        }

        private void PickWanderTarget()
        {
            // 好奇心が高いほど広く探索する。
            float r = wanderRadius * ((personality != null) ? personality.GetWanderRadiusMult() : 1f);
            float rx = Random.Range(-r, r);
            float rz = Random.Range(-r, r);
            wanderTarget = new Vector3(home.x + rx, home.y, home.z + rz);
            hasWanderTarget = true;
        }

        // ================= 逃走(Flee) =================

        /// <summary>
        /// 脅威源から離れる方向へ、通常より速く走る。ただし『真逆一直線』ではなく、
        /// 一定間隔で逃走方向をランダムにずらしてジグザグに逃げる(生き物らしさ)。
        /// ずれは常に「離れる側 ±fleeAngleMax 度」に収めるので、相手に突っ込まない。
        /// </summary>
        private void FleeFrom(Vector3 threatPos)
        {
            arrived = false;
            lastTarget = null;

            Vector3 pos = transform.position;
            Vector3 away = new Vector3(pos.x - threatPos.x, 0f, pos.z - threatPos.z);
            float d = away.magnitude;

            Vector3 awayDir;
            if (d < 0.001f)
            {
                awayDir = new Vector3(transform.forward.x, 0f, transform.forward.z);
                if (awayDir.sqrMagnitude < 0.001f) awayDir = Vector3.forward;
                awayDir = awayDir.normalized;
            }
            else
            {
                awayDir = away / d;
            }

            // 一定間隔で逃走方向を、離れる方向から ±fleeAngleMax 度ずらして選び直す。
            float now = Time.time;
            if (now >= fleeRedirectAt || fleeDir.sqrMagnitude < 0.001f)
            {
                fleeRedirectAt = now + Random.Range(fleeRedirectMin, fleeRedirectMax);
                float angle = Random.Range(-fleeAngleMax, fleeAngleMax);
                fleeDir = RotateY(awayDir, angle);
            }
            else
            {
                // 相手が動いても突っ込まないよう、現在の逃走方向を少しずつ「離れる方向」へ寄せる。
                fleeDir = (fleeDir + awayDir * 0.15f).normalized;
            }

            // 臆病な猫ほど速く逃げる。
            float fleeMult = (personality != null) ? personality.GetFleeSpeedMult() : 1f;
            float speed = BaseSpeed() * fleeSpeedMultiplier * fleeMult;

            // 逃走中も壁は避ける(パニックで壁にめり込まない)。目的地距離の制限は無いので大きい値。
            Vector3 disp = StepWithAvoidance(pos, fleeDir, speed, Mathf.Infinity);
            transform.position = pos + disp;

            FaceMovement(disp, fleeDir);
        }

        // ================= 移動の共通処理(障害物回避 + 向き) =================

        /// <summary>
        /// desiredDir(正規化)へ speed で1フレーム進む変位を返す(障害物回避込み)。
        /// CreatureLocomotion があれば回避補正し、無ければ従来どおり直進する(null 安全)。
        /// maxDist は目的地までの距離(行き過ぎ防止 + それより先の障害物を無視する先読み上限)。
        /// 逃走など上限が無い場合は Infinity を渡す。
        /// </summary>
        private Vector3 StepWithAvoidance(Vector3 pos, Vector3 desiredDir, float speed, float maxDist)
        {
            float stepLen = speed * Time.deltaTime;
            if (stepLen > maxDist) stepLen = maxDist; // 行き過ぎ防止

            if (locomotion != null)
                return locomotion.ComputeMove(pos, desiredDir, stepLen, maxDist);
            return desiredDir * stepLen; // 従来動作(回避コンポーネント無し)
        }

        /// <summary>実際に進んだ方向(disp)へ回頭する。停止中(disp≈0)は desiredDir を向く。</summary>
        private void FaceMovement(Vector3 disp, Vector3 desiredDir)
        {
            Vector3 faceDir = (disp.sqrMagnitude > 0.0000001f) ? disp.normalized : desiredDir;
            if (faceDir.sqrMagnitude < 0.0000001f) return;
            Quaternion look = Quaternion.LookRotation(faceDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        /// <summary>基本移動速度(Profile の moveSpeed × 性格の活発さ倍率)。</summary>
        private float BaseSpeed()
        {
            float s = (profile != null) ? profile.moveSpeed : fallbackSpeed;
            if (personality != null) s *= personality.GetMoveSpeedMult();
            return s;
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
