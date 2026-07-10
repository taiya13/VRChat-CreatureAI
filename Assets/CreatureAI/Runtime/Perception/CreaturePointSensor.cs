using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// Layer 1(知覚層): 猫の周囲にある候補地点をキャッシュする(仕様 3章)。
    ///
    /// 稼働中 Registry への参照は「遅延再取得」で持つ(仕様 6章):
    /// 参照が null か、対象 GameObject が非アクティブなときだけ GameObject.Find を
    /// 実行し直す。正常時はキャッシュを返し、毎フレームの Find を避ける。
    /// これにより、選出された猫がワールドギミックで非アクティブ化されても、
    /// 全猫が永久沈黙する事故を防ぐ。
    ///
    /// このコンポーネントは Cat のルート(CreatureCore と同じ GameObject)に置き、
    /// transform.position を猫の現在位置として扱う。
    /// </summary>
    public class CreaturePointSensor : UdonSharpBehaviour
    {
        // 稼働中レジストリのシーン上の名前。全クラスで一致させる。
        private const string RegistryObjectName = "__CatAI_Registry";

        [Tooltip("候補を再取得する最小間隔(秒)。CreatureCore の Tick から毎回呼ばれるが、" +
                 "この間隔未満の呼び出しはスキップする。")]
        public float refreshInterval = 0.5f;

        [Tooltip("同時にキャッシュできる近傍候補の最大数。")]
        public int maxCandidates = 16;

        private CreaturePointRegistry cachedRegistry = null;
        private CreaturePoint[] candidates;
        private int candidateCount = 0;
        private float lastRefreshTime = -999f;
        private int lastLoggedCount = -1;

        private bool debugLog = true;

        void Start()
        {
            if (maxCandidates < 1) maxCandidates = 1;
            candidates = new CreaturePoint[maxCandidates];
            CreatureCore core = GetComponent<CreatureCore>();
            if (core != null) debugLog = core.debugLog;
        }

        /// <summary>
        /// 間隔を空けて近傍候補キャッシュを更新する。CreatureCore.OnCoreTick から呼ぶ。
        /// </summary>
        public void RefreshIfNeeded()
        {
            if (Time.time - lastRefreshTime < refreshInterval) return;
            lastRefreshTime = Time.time;

            if (candidates == null) candidates = new CreaturePoint[maxCandidates < 1 ? 1 : maxCandidates];

            CreaturePointRegistry registry = GetRegistry();
            if (registry == null)
            {
                candidateCount = 0;
                return;
            }

            candidateCount = registry.CollectNearby(transform.position, candidates);

            // 候補数が変化したときだけログ(コンソールを埋めないため)。
            if (candidateCount != lastLoggedCount)
            {
                if (debugLog) Debug.Log("[CreaturePointSensor] 近傍候補: " + candidateCount + " 個 (" + name + ")");
                lastLoggedCount = candidateCount;
            }
        }

        /// <summary>
        /// 現在位置から見て、要求種別に合う最良候補を返す(Phase 2 の意思決定層が使う)。
        /// 実処理は稼働中 Registry へ委譲する。
        /// </summary>
        public CreaturePoint FindBest(PointType requiredTypes)
        {
            CreaturePointRegistry registry = GetRegistry();
            if (registry == null) return null;
            return registry.FindBestCandidate(transform.position, requiredTypes);
        }

        public int GetCandidateCount() { return candidateCount; }

        public CreaturePoint GetCandidate(int index)
        {
            if (candidates == null || index < 0 || index >= candidateCount) return null;
            return candidates[index];
        }

        /// <summary>
        /// 稼働中 Registry を返す。参照が死んでいる場合のみ Find で再取得する(仕様 6章)。
        /// </summary>
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
