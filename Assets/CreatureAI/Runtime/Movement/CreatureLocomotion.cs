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
    ///   ・lookAhead(目的地までの残り距離)より先の障害物は無視する(壁際の餌皿に到達できるように)。
    ///   ・トリガーコライダーと自分自身の体は障害物にしない。
    ///
    /// [壁ぎわで左右に揺れて突撃し続けない設計(v2)]
    ///   1. 進行方向のスムージング: 実際に進む向きは前フレームの向きから
    ///      steerTurnRate(度/秒)までしか変わらない。回避角度がフレームごとに
    ///      30°⇔60°と飛んでも、体の動きは滑らかな弧になる(=小刻みな揺れが消える)。
    ///   2. 回避サイドのロック: 一度「左に避ける」と決めたら、その側が実質的に
    ///      行き止まり(clearance近くまで塞がる)になるまで右へは切り替えない。
    ///      以前は反対側に空きがあると即切替していたため、角で左右に振動した。
    ///   3. スタック脱出: 進もうとしているのに一定時間ほとんど動けていない場合、
    ///      いったん目的地を無視して「最も開けた方向(後ろ寄りを優先)」へ
    ///      escapeDuration 秒だけ退避する。壁へ突撃し続ける状態を強制的に打ち切り、
    ///      「猫が諦めて別ルートを探す」ように見せる。
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

        [Tooltip("一度避け始めた側を維持し続ける秒数(壁の前で左右に迷うのを防ぐ)。" +
                 "大きいほど一方向に決めて壁沿いに回り込む。小さいと融通は利くが振動しやすい。")]
        [Min(0f)]
        public float turnCommitTime = 1.0f;

        [Header("揺れ・突撃の抑制 (v2)")]
        [Tooltip("実際の進行方向が1秒に変われる最大角度(度)。小さいほど滑らかな弧を描くが、" +
                 "曲がり始めが遅くなる。150〜300 が目安。")]
        [Min(30f)]
        public float steerTurnRate = 220f;

        [Tooltip("スタック判定の観察時間(秒)。この時間のあいだ…")]
        [Min(0.5f)]
        public float stuckWindow = 1.5f;

        [Tooltip("…移動距離がこの値(m)未満で、かつ大半が『塞がれ』判定なら、スタックとみなす。")]
        [Min(0.01f)]
        public float stuckMinMove = 0.12f;

        [Tooltip("スタック脱出で退避する時間(秒)。目的地を無視して開けた方向へ下がる。")]
        [Min(0.3f)]
        public float escapeDuration = 1.4f;

        // --- 診断用に最後の探査結果を保持(Editor Gizmo が読む / Inspector 非表示) ---
        [HideInInspector] public Vector3 lastOrigin;
        [HideInInspector] public Vector3 lastDesiredDir;
        [HideInInspector] public Vector3 lastChosenDir;
        [HideInInspector] public bool lastBlocked;

        // 避けている側(-1=左 / +1=右 / 0=未コミット)と、その保持期限。
        private int avoidSign = 0;
        private float avoidCommitUntil = 0f;

        // 実際に進んでいる向き(スムージングされた進行方向)。
        private Vector3 steerDir = Vector3.zero;
        private float lastComputeTime = -10f;

        // スタック検知(観察窓の起点と、その間に「塞がれていた」累計時間)。
        private Vector3 stuckAnchor = Vector3.zero;
        private float stuckAnchorTime = -10f;
        private float blockedAccum = 0f;

        // スタック脱出モード(期限と退避方向)。
        private float escapeUntil = -10f;
        private Vector3 escapeDir = Vector3.zero;

        // SteerNormal の「前方が塞がれていたか」の受け渡し(UdonSharp は out 引数非対応のため)。
        private bool steerBlocked = false;

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

            float now = Time.time;
            float dt = Time.deltaTime;

            // しばらく移動要求が無かった(立ち止まっていた)なら、内部状態を仕切り直す。
            if (now - lastComputeTime > 0.5f)
            {
                steerDir = desiredDir;
                stuckAnchor = pos;
                stuckAnchorTime = now;
                blockedAccum = 0f;
            }
            lastComputeTime = now;

            // 実効の先読み距離: 目的地より先の壁は気にしない(壁際の地点に到達できるように)。
            // ただし「今回の歩幅 + clearance」より短くはしない(最低限の安全確認)。
            float probe = probeDistance;
            if (lookAhead < probe) probe = lookAhead;
            float minProbe = clearance + stepLen;
            if (probe < minProbe) probe = minProbe;

            // ================= 進みたい方向(raw)を決める =================
            Vector3 raw;
            bool blocked;

            if (now < escapeUntil)
            {
                // --- スタック脱出中: 目的地は無視して開けた方向へ退避 ---
                blocked = true;
                float freeE = FreeDistance(lastOrigin, escapeDir, probeDistance);
                if (freeE < clearance + 0.05f)
                    escapeDir = WidestEscapeDir(lastOrigin, desiredDir); // 退避先も塞がれた → 選び直し
                raw = escapeDir;
            }
            else
            {
                raw = SteerNormal(desiredDir, probe, now, stepLen);
                blocked = steerBlocked;

                // --- スタック検知: 進みたいのにほぼ動けていない状態が続いたら脱出を始める ---
                if (blocked) blockedAccum += dt;
                if (now - stuckAnchorTime >= stuckWindow)
                {
                    float windowLen = now - stuckAnchorTime;
                    Vector3 movedVec = new Vector3(pos.x - stuckAnchor.x, 0f, pos.z - stuckAnchor.z);
                    bool noProgress = movedVec.magnitude < stuckMinMove;
                    bool mostlyBlocked = blockedAccum >= windowLen * 0.4f;
                    if (noProgress && mostlyBlocked)
                    {
                        escapeDir = WidestEscapeDir(lastOrigin, desiredDir);
                        escapeUntil = now + escapeDuration;
                        avoidSign = 0;               // 脱出後は避ける側を選び直す
                        avoidCommitUntil = now;
                        raw = escapeDir;
                    }
                    stuckAnchor = pos;
                    stuckAnchorTime = now;
                    blockedAccum = 0f;
                }
            }

            // ================= スムージング: 実際の進行方向は急に変えない =================
            steerDir = RotateTowardsY(steerDir, raw, steerTurnRate * dt);

            // 進行方向の安全確認: 曲がりかけの向きが壁に刺さるなら、その分だけ歩幅を詰める。
            float free = FreeDistance(lastOrigin, steerDir, probe);
            float allowed = free - clearance;
            if (allowed < 0.003f)
            {
                // 旋回途中の向きが完全に塞がっている → 素直に raw へ向き直して再確認。
                steerDir = raw;
                free = FreeDistance(lastOrigin, steerDir, probe);
                allowed = free - clearance;
            }

            lastBlocked = blocked;
            if (allowed <= 0f)
            {
                lastChosenDir = Vector3.zero;
                return Vector3.zero; // 本当に囲まれている → 停止(貫通しない)
            }
            if (allowed > stepLen) allowed = stepLen;
            lastChosenDir = steerDir;
            return steerDir * allowed;
        }

        /// <summary>
        /// 通常時のステアリング(ウィスカー探索 + サイドロック)。進みたい方向を返す。
        /// 「前方が塞がれていたか」は steerBlocked フィールドに残す。
        /// </summary>
        private Vector3 SteerNormal(Vector3 desiredDir, float probe, float now, float stepLen)
        {
            float stepDeg = maxAvoidAngle / probeSteps;

            // 1. 正面が十分空いている → 目的地へ直進。
            //    「避けている側」の記憶は、クリアが turnCommitTime 続いたときだけ解除する。
            float dFwd = FreeDistance(lastOrigin, desiredDir, probe);
            if (dFwd >= probe)
            {
                if (now >= avoidCommitUntil) avoidSign = 0;
                steerBlocked = false;
                return desiredDir;
            }

            steerBlocked = true;

            // 2. 避ける側を決める。一度決めたら、その側が実質行き止まりになるまで維持する。
            if (avoidSign == 0)
            {
                float dL0 = FreeDistance(lastOrigin, RotateY(desiredDir, -stepDeg), probe);
                float dR0 = FreeDistance(lastOrigin, RotateY(desiredDir, stepDeg), probe);
                avoidSign = (dL0 >= dR0) ? -1 : 1;
            }
            avoidCommitUntil = now + turnCommitTime;

            // 3. 決めた側で、目的地寄り(小さい角度)から順に、通れる方向を探す(壁沿いに滑らかに)。
            Vector3 bestDir = RotateY(desiredDir, stepDeg * avoidSign);
            float bestFree = dFwd;
            for (int i = 1; i <= probeSteps; i++)
            {
                Vector3 d = RotateY(desiredDir, stepDeg * i * avoidSign);
                float dd = FreeDistance(lastOrigin, d, probe);
                if (dd >= probe) return d;
                if (dd > bestFree) { bestFree = dd; bestDir = d; }
            }

            // 4. 決めた側がどの角度も「実質行き止まり」のときだけ、反対側を検討する。
            //    (以前は反対側に空きがあると即切替 → 角で左右に振動していた)
            float deadThreshold = clearance + Mathf.Max(0.06f, stepLen * 4f);
            if (bestFree < deadThreshold)
            {
                Vector3 oppBestDir = bestDir;
                float oppBestFree = bestFree;
                bool oppFullyFree = false;
                for (int i = 1; i <= probeSteps; i++)
                {
                    Vector3 d = RotateY(desiredDir, -stepDeg * i * avoidSign);
                    float dd = FreeDistance(lastOrigin, d, probe);
                    if (dd >= probe) { oppBestDir = d; oppFullyFree = true; break; }
                    if (dd > oppBestFree) { oppBestFree = dd; oppBestDir = d; }
                }
                if (oppFullyFree || oppBestFree > bestFree + 0.05f)
                {
                    avoidSign = -avoidSign; // 正式に切替(コミット済みなので次フレームで戻らない)
                    return oppBestDir;
                }
            }

            // 5. 最も空いた方向へ(歩幅は呼び出し元の clearance クランプで自然と詰まる)。
            return bestDir;
        }

        /// <summary>
        /// スタック脱出用: 全周をあらく見渡して「最も開けた方向」を返す。
        /// 同程度に開けているなら、いま行きたい方向(壁側)から離れる向きを優先する
        /// (=角から後ずさりして出る動きになる)。
        /// </summary>
        private Vector3 WidestEscapeDir(Vector3 origin, Vector3 desiredDir)
        {
            Vector3 best = -desiredDir;
            float bestScore = -1f;
            for (int i = 0; i < 12; i++)
            {
                Vector3 d = RotateY(desiredDir, 30f * i);
                float freeD = FreeDistance(origin, d, probeDistance);
                // 開けているほど高評価。行きたい方向(壁側)に近いほど少し減点。
                float score = freeD - 0.15f * probeDistance * Vector3.Dot(d, desiredDir);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = d;
                }
            }
            return best;
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

        /// <summary>水平単位ベクトル from を、to へ向けて最大 maxDeg 度だけ回した向きを返す。</summary>
        private Vector3 RotateTowardsY(Vector3 from, Vector3 to, float maxDeg)
        {
            if (from.sqrMagnitude < 0.0001f) return to;
            if (to.sqrMagnitude < 0.0001f) return from;
            float a0 = Mathf.Atan2(from.x, from.z) * Mathf.Rad2Deg;
            float a1 = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(a0, a1);
            if (delta > maxDeg) delta = maxDeg;
            else if (delta < -maxDeg) delta = -maxDeg;
            float a = (a0 + delta) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
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
