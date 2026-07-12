using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace CreatureAI
{
    /// <summary>
    /// 視線システム(Phase 10)。近くの気になる対象へ「顔(頭)だけ」を向ける。
    ///
    /// [責務] 一定間隔で「最も気になる対象」を選び、毎フレーム headTransform を
    ///        そちらへ滑らかに向ける。体の向き・移動・行動には一切触れないので、
    ///        食事中でも歩行中でも "行動を中断せず" に視線だけが動く。
    ///        意思決定・移動・アニメ状態は他層の責務のまま(無改造)。
    ///
    /// [対象(拡張可能)] 既定で以下を候補にする。近さと「興味度(interest)」で最良を選ぶ。
    ///        ・プレイヤー(最も興味度が高い)
    ///        ・近くの CreaturePoint(Food/Water/Bed/… ; Sensor のキャッシュを利用)
    ///        ・近くの他の猫(Registry の猫インデックスを利用)
    ///        ・extraTargets(Inspector で任意の Transform を追加)
    ///        新しい対象カテゴリは EvaluateBest() に候補を1つ足すだけで増やせる。
    ///
    /// [向ける先] headTransform を割り当てると、そのボーン/オブジェクトだけが回る。
    ///        未割り当てなら視線対象の計算だけ行い、見た目は変えない(Gizmo で確認可)。
    ///        体の正面から maxYaw/maxPitch を超える対象は「見えない」ものとして無視する
    ///        (首があらぬ方向にねじれない)。
    ///
    /// Cat のルート(CreatureCore と同じ GameObject)に付ける。自己駆動(Update)。
    /// </summary>
    public class CreatureGaze : UdonSharpBehaviour
    {
        private const string RegistryObjectName = "__CatAI_Registry";

        [Tooltip("視線を向ける頭(ボーン/オブジェクト)。未割り当てなら計算のみで見た目は変えない。")]
        public Transform headTransform;

        [Tooltip("視線を有効にする。")]
        public bool enableGaze = true;

        [Tooltip("この距離(m)以内の対象だけを見る。")]
        public float gazeRange = 6.0f;

        [Tooltip("体の正面からの首振り上限(度)。左右。これを超える対象は見ない。")]
        [Range(10f, 170f)]
        public float maxYaw = 110f;

        [Tooltip("上下の首振り上限(度)。")]
        [Range(5f, 89f)]
        public float maxPitch = 45f;

        [Tooltip("頭が対象へ向く速さ(度/秒)。")]
        public float headTurnSpeed = 240f;

        [Tooltip("対象を選び直す間隔(秒)。頭の回転は毎フレーム滑らかに追従する。")]
        public float retargetInterval = 0.3f;

        [Tooltip("プレイヤーの足元からの目線の高さ(m)。頭を見上げる自然さ用。")]
        public float playerEyeHeight = 1.3f;

        [Tooltip("任意の追加注視対象(他の猫を手動指定したい場合や、置物・ギミック等)。")]
        public Transform[] extraTargets;

        // 興味度(大きいほど優先。距離とあわせてスコア化する)。
        [Header("興味度(大きいほど優先)")]
        public float interestPlayer = 3.0f;
        public float interestCat = 1.5f;
        public float interestPoint = 1.0f;
        public float interestExtra = 1.2f;

        private CreaturePointSensor sensor;
        private CreaturePointRegistry cachedRegistry;
        private Transform[] catBuffer = new Transform[8];

        private Quaternion neutralLocalRot;
        private bool hasNeutral = false;

        private float nextRetargetAt = 0f;
        private bool hasTarget = false;
        private Vector3 targetPos;

        // EvaluateBest の作業用(UdonSharp は user メソッドの out/ref を避けるためフィールドで持つ)。
        private Vector3 evalBestPos;
        private float evalBestScore;

        void Start()
        {
            sensor = GetComponent<CreaturePointSensor>();
            if (headTransform != null)
            {
                neutralLocalRot = headTransform.localRotation; // 正面(基準)姿勢を覚える
                hasNeutral = true;
            }
            // 自分を猫インデックスへ登録(他の猫の視線対象になれるように)。
            CreaturePointRegistry reg = GetRegistry();
            if (reg != null) reg.RegisterCreature(transform);
        }

        void Update()
        {
            if (!enableGaze || headTransform == null) { UpdateTargetOnly(); return; }

            float now = Time.time;
            if (now >= nextRetargetAt)
            {
                nextRetargetAt = now + retargetInterval;
                hasTarget = EvaluateBest();
            }

            if (hasTarget)
            {
                // 対象へ向く回転を、正面からの角度上限内にクランプして適用。
                Vector3 dir = targetPos - headTransform.position;
                Vector3 look = ClampToCone(dir);
                if (look.sqrMagnitude > 0.0001f)
                {
                    Quaternion want = Quaternion.LookRotation(look.normalized, Vector3.up);
                    headTransform.rotation = Quaternion.RotateTowards(
                        headTransform.rotation, want, headTurnSpeed * Time.deltaTime);
                    return;
                }
            }

            // 対象なし / 角度外 → 正面(基準姿勢)へ戻す。
            if (hasNeutral)
            {
                Quaternion wantLocal = neutralLocalRot;
                headTransform.localRotation = Quaternion.RotateTowards(
                    headTransform.localRotation, wantLocal, headTurnSpeed * Time.deltaTime);
            }
        }

        /// <summary>headTransform 未割り当てでも対象計算だけは進める(Gizmo/デバッグ用)。</summary>
        private void UpdateTargetOnly()
        {
            if (!enableGaze) { hasTarget = false; return; }
            float now = Time.time;
            if (now >= nextRetargetAt)
            {
                nextRetargetAt = now + retargetInterval;
                hasTarget = EvaluateBest();
            }
        }

        // ================= 対象選択(拡張点) =================

        /// <summary>
        /// 候補の中から「近くて興味度が高い」対象を1つ選ぶ。score = interest / (1 + 距離)。
        /// 結果は targetPos に入れ、見つかったら true。新しい対象カテゴリはここに Consider を足すだけ。
        /// </summary>
        private bool EvaluateBest()
        {
            evalBestPos = Vector3.zero;
            evalBestScore = 0f;
            Vector3 self = transform.position;

            // 1. プレイヤー(頭の高さを見る)。
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp != null)
                Consider(self, lp.GetPosition() + Vector3.up * playerEyeHeight, interestPlayer);

            // 2. 近くの CreaturePoint(Food/Water/Bed 等)。Sensor のキャッシュを使う。
            if (sensor != null)
            {
                int n = sensor.GetCandidateCount();
                for (int i = 0; i < n; i++)
                {
                    CreaturePoint cp = sensor.GetCandidate(i);
                    if (cp != null) Consider(self, cp.transform.position, interestPoint);
                }
            }

            // 3. 近くの他の猫。
            CreaturePointRegistry reg = GetRegistry();
            if (reg != null)
            {
                int cn = reg.CollectNearbyCreatures(self, transform, gazeRange, catBuffer);
                for (int i = 0; i < cn; i++)
                {
                    Transform t = catBuffer[i];
                    if (t != null) Consider(self, t.position + Vector3.up * 0.3f, interestCat);
                }
            }

            // 4. 追加対象(Inspector)。
            if (extraTargets != null)
            {
                for (int i = 0; i < extraTargets.Length; i++)
                {
                    Transform t = extraTargets[i];
                    if (t != null) Consider(self, t.position, interestExtra);
                }
            }

            targetPos = evalBestPos;
            return evalBestScore > 0f;
        }

        /// <summary>1候補をスコア評価して、より良ければ作業用フィールドを更新する。</summary>
        private void Consider(Vector3 self, Vector3 pos, float interest)
        {
            float d = Vector3.Distance(self, pos);
            if (d > gazeRange) return;
            float score = interest / (1f + d);
            if (score > evalBestScore) { evalBestScore = score; evalBestPos = pos; }
        }

        // ================= 角度クランプ =================

        /// <summary>
        /// dir(頭→対象)が体の正面から maxYaw/maxPitch 以内なら、その(必要なら上下を丸めた)
        /// 方向を返す。外なら Vector3.zero(=見ない)。首が不自然にねじれるのを防ぐ。
        /// </summary>
        private Vector3 ClampToCone(Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) return Vector3.zero;

            Vector3 fwd = transform.forward;
            Vector3 flatFwd = new Vector3(fwd.x, 0f, fwd.z);
            Vector3 flatDir = new Vector3(dir.x, 0f, dir.z);
            if (flatFwd.sqrMagnitude < 0.0001f || flatDir.sqrMagnitude < 0.0001f) return Vector3.zero;
            flatFwd = flatFwd.normalized; flatDir = flatDir.normalized;

            float yaw = Vector3.Angle(flatFwd, flatDir);
            if (yaw > maxYaw) return Vector3.zero;

            float pitch = Vector3.Angle(flatDir, dir.normalized);
            if (pitch > maxPitch)
            {
                // 上下だけ上限に丸める(左右は活かす)。
                float sign = (dir.y >= 0f) ? 1f : -1f;
                float py = Mathf.Tan(maxPitch * Mathf.Deg2Rad);
                return new Vector3(flatDir.x, sign * py, flatDir.z);
            }
            return dir;
        }

        // ================= 参照 / ヘルパー =================

        public bool HasGazeTarget() { return hasTarget; }
        public Vector3 GetGazeTarget() { return targetPos; }

        private CreaturePointRegistry GetRegistry()
        {
            if (cachedRegistry == null || !cachedRegistry.gameObject.activeInHierarchy)
            {
                GameObject go = GameObject.Find(RegistryObjectName);
                if (go != null) cachedRegistry = go.GetComponent<CreaturePointRegistry>();
                else cachedRegistry = null;
            }
            return cachedRegistry;
        }
    }
}
