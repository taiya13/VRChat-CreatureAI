using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace CreatureAI
{
    /// <summary>
    /// Layer X(反射・警戒層): 危険を検知する(仕様 3章 ThreatEvaluator)。
    ///
    /// この Phase の実装はシンプルに「ローカルプレイヤーが threatDistance 以内に近づいたら危険」
    /// とする。判断・中断の実行は行わない(検知だけ)。中断の指揮は CreatureCore が、
    /// 実際の中断は ActionRunner.AbortCurrent()、Goal 切替は Brain.ForceFlee() が担う。
    ///
    /// [将来の拡張土台]
    ///   ・PlayerRecognition: プレイヤーとの関係(警戒/信頼)で threatDistance を可変にする。
    ///   ・DangerSpot: シーンに置いた危険地点(掃除機・水場など)も脅威源として合成する。
    ///   ・複数プレイヤー: VRCPlayerApi.GetPlayers() で全員の最短距離を見る。
    ///   いずれも Check() の中で脅威源を合成し、IsThreatened()/GetThreatPosition() の
    ///   出力に落とすだけで、上位(Core/Brain/Action)は変更不要。
    ///
    /// Cat のルートに付ける(自分の transform.position を基準に距離を測る)。
    /// </summary>
    public class ThreatEvaluator : UdonSharpBehaviour
    {
        [Tooltip("プレイヤーがこの距離(m)以内に近づいたら危険とみなし Flee する。")]
        public float threatDistance = 3.0f;

        private bool threatened = false;
        private bool wasThreatened = false;
        private float lastDistance = 999f;
        private Vector3 threatPosition;

        private CreaturePersonality personality;

        void Start()
        {
            personality = GetComponent<CreaturePersonality>(); // 性格(臆病さ)で反応距離が変わる
        }

        /// <summary>CreatureCore の Tick(毎回)から呼ばれる。危険源を評価する。</summary>
        public void Check()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null)
            {
                // エディタ非再生時などプレイヤーがいない場合は安全扱い。
                threatened = false;
                return;
            }

            // 臆病な猫ほど遠くから逃げる(反応距離が伸びる)。
            float effectiveDistance = threatDistance;
            if (personality != null) effectiveDistance *= personality.GetThreatDistanceMult();

            threatPosition = lp.GetPosition();
            lastDistance = Vector3.Distance(transform.position, threatPosition);
            threatened = lastDistance < effectiveDistance;

            if (threatened != wasThreatened)
            {
                Debug.Log("[Threat] " + name + (threatened
                    ? (" 危険検知: プレイヤー接近 (d=" + Round1(lastDistance) + "m)")
                    : " 安全: プレイヤーが離れた"));
                wasThreatened = threatened;
            }
        }

        public bool IsThreatened() { return threatened; }
        public Vector3 GetThreatPosition() { return threatPosition; }
        public float GetThreatDistance() { return lastDistance; }

        private float Round1(float v) { return Mathf.Round(v * 10f) / 10f; }
    }
}
