using UdonSharp;
using UnityEngine;

namespace CreatureAI
{
    /// <summary>
    /// 身体制御層: AI の状態を「見た目(アニメーション)」へ反映する専用コンポーネント。
    ///
    /// [責務] AgentState(ActionRunner)と Goal(Brain)を読み取り、動作種別(MotionKind)を
    ///        決めて、Animator の整数パラメータ "MotionState" に反映する。それだけ。
    ///        アニメーションのクリップや遷移の中身は作らない(Animator 側の役割)。
    ///        Brain・Movement には一切アニメ処理を書かない(責務分離)。
    ///
    /// [反映先の自動検出] 差し替えモデル(FBX)は自前の Animator を持って入ってくることが
    ///        多く、「ルートの Animator にだけ書き込む」とモデル側の Animator に値が届かず
    ///        Idle から遷移しない事故が起きる。そこで起動時に Cat 配下の全 Animator を探査し、
    ///        パラメータ(MotionState)を持つものすべてを反映先にする。
    ///        Inspector の animator を明示指定した場合は、それだけに反映する。
    ///
    /// [決定の一元化] DecideMotion() が「状態→モーション」を1箇所で決める:
    ///        Flee → 逃げる / Moving → 歩く / Acting → その Goal 用モーション / それ以外 → 待機。
    ///        「どの Goal でどのモーションを出すか」の対応は CreatureActionCatalog.MotionForGoal
    ///        に集約されており、新しい Action の専用モーションは対応表に1行足すだけで反映される
    ///        (このクラスは無改造)。クリップ差し替えは Animator(Inspector)側で完結する。
    ///
    /// [他の動物への拡張] このクラスは種族非依存。犬・鹿はモデルと AnimatorController を
    ///        差し替えるだけで、同じ仕組みで別のモーションに切り替わる。
    /// </summary>
    public class CreatureAnimator : UdonSharpBehaviour
    {
        [Tooltip("反映先の Animator。指定するとこれ1つだけに反映する。" +
                 "未設定なら Cat 配下の全 Animator から MotionState を持つものを自動検出(推奨)。")]
        public Animator animator;

        [Tooltip("Animator 側の整数パラメータ名(動作状態を渡す)。")]
        public string parameterName = "MotionState";

        private CreatureBrain brain;
        private ActionRunner actionRunner;
        private CreatureActionCatalog catalog; // Goal→Motion / 表示名 の唯一の対応表
        private bool debugLog = true;

        private MotionKind currentKind = MotionKind.Idle;
        private int lastSent = -999;

        // パラメータ(MotionState)を持つ反映先 Animator 群(起動時に探査)。
        private Animator[] targets = new Animator[0];
        private bool resolved = false;

        // 探査用の値。どの遷移条件にも一致しない値を書いて読み戻し、
        // パラメータの有無を判定する(無い Animator への書き込みは無視されて 0 が返る)。
        private const int ProbeValue = 63;

        public void Initialize(CreatureBrain creatureBrain, ActionRunner runner, CreatureActionCatalog actionCatalog)
        {
            brain = creatureBrain;
            actionRunner = runner;
            catalog = actionCatalog;
            CreatureCore core = GetComponent<CreatureCore>();
            if (core != null) debugLog = core.debugLog;
            ResolveTargets();
        }

        void Start()
        {
            // Initialize(Core 経由)が呼ばれない構成でも反映先を解決しておく。
            if (!resolved) ResolveTargets();
        }

        /// <summary>
        /// 反映先 Animator を解決する(起動時に1回)。
        /// animator が明示指定されていればそれだけ、未指定なら配下の全 Animator を探査して
        /// MotionState パラメータを持つものすべてを反映先にする。
        /// </summary>
        private void ResolveTargets()
        {
            resolved = true;

            Animator[] found;
            if (animator != null)
            {
                found = new Animator[1];
                found[0] = animator;
            }
            else
            {
                found = GetComponentsInChildren<Animator>();
            }

            // パラメータを持つものだけを反映先に残す(持たない Animator へ書き続けると
            // Unity が毎回警告を出すため、起動時の1回だけ探査する)。
            int n = 0;
            bool[] ok = new bool[found.Length];
            for (int i = 0; i < found.Length; i++)
            {
                Animator a = found[i];
                if (a == null) continue;
                a.SetInteger(parameterName, ProbeValue);
                if (a.GetInteger(parameterName) == ProbeValue)
                {
                    a.SetInteger(parameterName, 0);
                    ok[i] = true;
                    n++;
                }
            }

            targets = new Animator[n];
            int w = 0;
            string names = "";
            for (int i = 0; i < found.Length; i++)
            {
                if (!ok[i]) continue;
                targets[w] = found[i];
                w++;
                names = names + ((w > 1) ? ", " : "") + found[i].name;
            }

            if (n == 0)
                Debug.LogWarning("[Animator] " + name + " : パラメータ '" + parameterName +
                    "' を持つ Animator が見つかりません。Animator に CreatureAnimator.controller" +
                    "(または " + parameterName + " を持つ Controller)を割り当ててください。" +
                    "メニュー『CreatureAI > 3. Animator Controller を再生成』で割り当て直せます。");
            else if (debugLog)
                Debug.Log("[Animator] " + name + " 反映先 " + n + " 件: " + names +
                    " (param='" + parameterName + "')");
        }

        /// <summary>CreatureCore の Tick から毎回呼ばれる。</summary>
        public void UpdateAnimation()
        {
            currentKind = DecideMotion();
            int v = (int)currentKind;

            // 毎 Tick、全反映先へ書き込む(モデル差し替えや再バインドでパラメータが
            // 既定値に戻っても、次の Tick で自動復元されるように)。
            for (int i = 0; i < targets.Length; i++)
            {
                Animator a = targets[i];
                if (a != null) a.SetInteger(parameterName, v);
            }

            if (v != lastSent)
            {
                lastSent = v;
                if (debugLog)
                {
                    Debug.Log("[Animator] " + name + " MotionState=" + v + " (" + GetMotionName() + ")");
                    // 読み戻し検証: 書き込んだ値が実際に Animator 側で保持されているか確認する。
                    // ここが一致していれば「値は正しく届いている」ので、原因は
                    // AnimatorController 内部の遷移設定(Has Exit Time / Condition 不一致等)。
                    // 不一致なら、他のスクリプトが同じパラメータへ上書きしている等の疑いがある。
                    for (int i = 0; i < targets.Length; i++)
                    {
                        Animator a = targets[i];
                        if (a == null) continue;
                        int readback = a.GetInteger(parameterName);
                        if (readback != v)
                            Debug.LogWarning("[Animator] " + name + " 読み戻し不一致: '" + a.name +
                                "' に " + v + " を書いたが、読み戻しは " + readback +
                                "(他のスクリプトが同じパラメータを上書きしている可能性)");
                    }
                }
            }
        }

        /// <summary>状態 → 動作種別の決定(モーション決定の一元化箇所)。</summary>
        private MotionKind DecideMotion()
        {
            Goal goal = (brain != null) ? brain.GetCurrentGoal() : Goal.None;

            // 逃走は最優先。
            if (goal == Goal.Flee) return MotionKind.Flee;

            AgentState st = (actionRunner != null) ? actionRunner.GetState() : AgentState.Idle;

            // 地点で行動中なら、その Goal 用のモーション(対応表 = CreatureActionCatalog)。
            // 専用モーションが無い Goal は対応表が Idle を返すので、ここに条件を足す必要はない。
            if (st == AgentState.Acting)
                return (catalog != null) ? catalog.MotionForGoal(goal) : MotionKind.Idle;

            if (st == AgentState.Moving) return MotionKind.Walk;

            return MotionKind.Idle;
        }

        // ================= 参照(デバッグ表示用) =================

        public MotionKind GetMotionKind() { return currentKind; }
        public string GetMotionName() { return (catalog != null) ? catalog.MotionName(currentKind) : "Idle"; }

        /// <summary>反映先 Animator の数(HUD の診断表示用)。0 なら接続不良。</summary>
        public int GetTargetCount() { return targets.Length; }
    }
}
