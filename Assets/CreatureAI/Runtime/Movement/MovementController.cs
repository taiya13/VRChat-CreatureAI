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

        private CreatureTargetSelector targetSelector;
        private CreatureProfile profile;
        private CreatureBrain brain;
        private ThreatEvaluator threat;

        private CreaturePoint lastTarget = null;
        private bool arrived = false;

        void Start()
        {
            // 自己初期化(Update 駆動なので Core 注入に依存せず自前で参照を取る)。
            targetSelector = GetComponent<CreatureTargetSelector>();
            profile = GetComponentInChildren<CreatureProfile>();
            brain = GetComponent<CreatureBrain>();
            threat = GetComponent<ThreatEvaluator>();
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
                // ターゲット無し = 待機。到着状態はリセット。
                lastTarget = null;
                arrived = false;
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
                    Debug.Log("[Move] " + name + " arrived at '" + tp.name + "' (dist=" +
                        (Mathf.Round(dist * 100f) / 100f) + "m)");
                    // 到着イベント。今は通知のみ。ActionRunner はここ / IsAtTarget() を起点に繋ぐ。
                }
                return; // 停止
            }

            // --- 移動 ---
            float speed = (profile != null) ? profile.moveSpeed : fallbackSpeed;
            Vector3 dir = flat / dist; // 正規化した水平方向
            Vector3 step = dir * speed * Time.deltaTime;
            if (step.magnitude > dist) step = flat; // 行き過ぎ防止(スナップ)
            transform.position = pos + step;

            // --- 向き(進行方向へ回頭) ---
            Quaternion look = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
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

        /// <summary>脅威源から離れる方向へ、通常より速く走る(逃走)。</summary>
        private void FleeFrom(Vector3 threatPos)
        {
            arrived = false;
            lastTarget = null;

            Vector3 pos = transform.position;
            Vector3 away = new Vector3(pos.x - threatPos.x, 0f, pos.z - threatPos.z);
            float d = away.magnitude;

            Vector3 dir;
            if (d < 0.001f)
            {
                // 真上など縮退時は現在の前方へ(それも無ければ +Z)。
                dir = new Vector3(transform.forward.x, 0f, transform.forward.z);
                if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
                dir = dir.normalized;
            }
            else
            {
                dir = away / d;
            }

            float speed = ((profile != null) ? profile.moveSpeed : fallbackSpeed) * fleeSpeedMultiplier;
            transform.position = pos + dir * speed * Time.deltaTime;

            Quaternion look = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }
    }
}
