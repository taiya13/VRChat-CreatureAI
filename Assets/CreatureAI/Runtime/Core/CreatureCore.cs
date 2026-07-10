using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// 1 匹の猫の「まとめ役」(仕様 3.1節 / 4章)。
    ///
    /// [役割]
    ///   ① 兄弟コンポーネントの参照を起動時に 1 回だけ GetComponent で収集する。
    ///   ② 稼働 Registry の選出状況を確認する(選出そのものは Registry.Start が行う)。
    ///   ③ 単一のタイマーループ(OnCoreTick)で各層を定期実行する。
    ///
    /// 判断・行動ロジックは一切持たない。「誰がいつ動くか」だけを 1 本の
    /// SendCustomEventDelayedSeconds ループに集約することで、猫が増えても
    /// 「1 匹につきタイマー 1 本」で済む(大量のタイマーが並走しない)。
    ///
    /// Phase 1 で存在するのは needsController / pointSensor / profile / registry のみ。
    /// 他層(playerSensor・decisionMaker・threatEvaluator・actionRunner・locomotion・
    /// animator・relationshipManager)は各フェーズで追加する。
    /// </summary>
    public class CreatureCore : UdonSharpBehaviour
    {
        [Header("Tick 設定")]
        [Tooltip("メインループの間隔(秒)。")]
        public float tickInterval = 0.2f;

        [Tooltip("起動時にこの秒数までのランダム遅延を入れ、多数の猫の Tick 位相をばらす。")]
        public float startupJitter = 0.2f;

        [Header("デバッグ")]
        [Tooltip("各コンポーネントの動作ログ([Brain]/[Target]/[Move] 等)を出すか。" +
                 "公開ワールドでは OFF 推奨。各コンポーネントは起動時にこの値を読む。")]
        public bool debugLog = true;

        // --- 兄弟コンポーネント参照(仕様 3.1「公開参照」) ---
        [HideInInspector] public NeedsController needsController;
        [HideInInspector] public NeedsData needsData;
        [HideInInspector] public CreatureBrain brain;
        [HideInInspector] public CreatureTargetSelector targetSelector;
        [HideInInspector] public MovementController movementController;
        [HideInInspector] public ActionRunner actionRunner;
        [HideInInspector] public ThreatEvaluator threatEvaluator;
        [HideInInspector] public CreatureAnimator creatureAnimator;
        [HideInInspector] public CreaturePointSensor pointSensor;
        [HideInInspector] public CreatureStatusDisplay statusDisplay;
        [HideInInspector] public CreatureProfile profile;

        // このネコが同梱する Registry(選出結果の確認・タイムアウト掃除に使う)。
        private CreaturePointRegistry localRegistry;

        private int tickCounter = 0;

        void Start()
        {
            // ① 参照キャッシュ(GetComponent は起動時の 1 回だけ)。
            needsController = GetComponent<NeedsController>();
            needsData = GetComponent<NeedsData>();
            brain = GetComponent<CreatureBrain>();
            targetSelector = GetComponent<CreatureTargetSelector>();
            movementController = GetComponent<MovementController>();
            actionRunner = GetComponent<ActionRunner>();
            threatEvaluator = GetComponent<ThreatEvaluator>();
            creatureAnimator = GetComponent<CreatureAnimator>();
            pointSensor = GetComponent<CreaturePointSensor>();
            statusDisplay = GetComponentInChildren<CreatureStatusDisplay>();
            profile = GetComponentInChildren<CreatureProfile>();
            localRegistry = GetComponentInChildren<CreaturePointRegistry>();

            // ② 依存注入 & 選出状況の確認ログ。
            if (needsController != null) needsController.Initialize(profile, needsData, actionRunner);
            if (brain != null) brain.Initialize(needsData, profile);
            if (targetSelector != null) targetSelector.Initialize(brain, pointSensor);
            if (actionRunner != null) actionRunner.Initialize(brain, targetSelector, movementController, needsController, profile);
            if (creatureAnimator != null) creatureAnimator.Initialize(brain, actionRunner);
            if (statusDisplay != null) statusDisplay.Initialize(needsData, brain, targetSelector, pointSensor, actionRunner);

            if (profile == null)
                Debug.LogWarning("[CreatureCore] Profile が見つかりません。子オブジェクト 'Profile' に " +
                    "CreatureProfile を付けてください: " + name);

            if (debugLog && localRegistry != null)
                Debug.Log("[CreatureCore] " + name + " 起動。この個体の Registry singleton = " +
                    localRegistry.IsActiveSingleton());

            // ③ タイマーループ開始(起動時ジッタを入れて位相をばらす)。
            SendCustomEventDelayedSeconds(nameof(OnCoreTick), Random.Range(0f, startupJitter));
        }

        /// <summary>
        /// 単一タイマーループ本体(仕様 4章)。各層を頻度分けで呼び、末尾で自分を再予約する。
        /// public でなければ SendCustomEventDelayedSeconds から呼べない点に注意。
        /// </summary>
        public void OnCoreTick()
        {
            tickCounter++;

            // 最優先・毎 Tick: 危険検知と割込み(Threat → AbortCurrent → Goal切替)。
            //   反応速度が要るので低頻度ブロックには入れず毎 Tick 評価する。
            if (threatEvaluator != null)
            {
                threatEvaluator.Check();
                if (brain != null)
                {
                    bool threatened = threatEvaluator.IsThreatened();
                    Goal g = brain.GetCurrentGoal();
                    if (threatened && g != Goal.Flee)
                    {
                        if (actionRunner != null) actionRunner.AbortCurrent(); // 現在の行動・予約を安全に畳む
                        brain.ForceFlee();                                     // Goal を Flee へ
                    }
                    else if (!threatened && g == Goal.Flee)
                    {
                        brain.EndFlee(); // 危険が去ったら通常評価へ戻す
                    }
                }
            }

            // 中頻度: 近傍候補の更新(Sensor 側でも間隔スロットリングされる)。
            if (pointSensor != null) pointSensor.RefreshIfNeeded();

            // 低頻度: 欲求増加 → 意思決定 → ターゲット選択(この順序)。
            if (tickCounter % 5 == 0)
            {
                if (needsController != null) needsController.GrowNeeds();
                if (brain != null) brain.Evaluate();
                if (targetSelector != null) targetSelector.SelectTarget();
            }

            // 行動実行は毎 Tick(到着後の回復を進める)。
            if (actionRunner != null) actionRunner.Tick();

            // 見た目への反映(状態→アニメーション)は毎 Tick。
            if (creatureAnimator != null) creatureAnimator.UpdateAnimation();

            // 表示更新は毎 Tick(滑らかに見せるため)。
            if (statusDisplay != null) statusDisplay.UpdateDisplay();

            // より低頻度: 占有タイムアウトの掃除(安全網 / 仕様 3.5節)。
            if (tickCounter % 10 == 0 && localRegistry != null && profile != null)
                localRegistry.CleanupExpiredReservations(profile.reserveTimeout);
            // Phase 3+: if (tickCounter % 10 == 0) relationshipManager.Update();

            // 次の Tick を予約(1 匹 = タイマー 1 本)。
            SendCustomEventDelayedSeconds(nameof(OnCoreTick), tickInterval);
        }
    }
}
