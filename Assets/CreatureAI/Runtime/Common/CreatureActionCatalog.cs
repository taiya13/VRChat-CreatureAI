using UdonSharp;

namespace CreatureAI
{
    /// <summary>
    /// Action 対応表(唯一の定義箇所 / Single Source of Truth)。
    ///
    /// [目的] 「1つの Action(Goal)」に関する 5 つの対応
    ///          Goal ⇔ Need ⇔ PointType ⇔ Motion ⇔ 表示名
    ///        を、このクラス 1 箇所だけに集約する。以前は Brain / TargetSelector /
    ///        CreatureAnimator にそれぞれ switch が散っていたが、これを廃してすべて
    ///        ここを参照する。新しい Action・欲求・モーションを足すときに「触る場所」を
    ///        1 ファイルに閉じ込め、アセット制作の足かせにならないようにするのが狙い。
    ///
    /// [1つの Action を足す手順(この 1 ファイルで完結)]
    ///   1. enum(Goal / NeedType / MotionKind)に必要な列挙を足す(末尾に連番で)。
    ///   2. 下の 6 つの表(GoalForNeed / NeedForGoal / PointTypeForGoal /
    ///      MotionForGoal / GoalName / NeedName / MotionName)にそれぞれ 1 行足す。
    ///   3. (点を使う Action なら)CreaturePoint 側で PointType を割り当てる。
    ///   4. (専用モーションを出すなら)AnimatorController に状態を 1 つ足す
    ///      (CreatureAISetup.MotionStates に 1 行、または既存 Controller に手で追加)。
    ///   → Brain / TargetSelector / ActionRunner / CreatureAnimator は無改造で対応する。
    ///
    /// [責務] 純粋な対応表のみ。判断(いつ動くか)は Brain、実行は ActionRunner、
    ///        表示は CreatureAnimator が持つ。ここに状態やロジックは置かない。
    ///
    /// Cat のルート(他の層と同じ GameObject)に付け、CreatureCore が各層へ注入する。
    /// </summary>
    public class CreatureActionCatalog : UdonSharpBehaviour
    {
        // ================= Need ⇔ Goal =================

        /// <summary>欲求 → その欲求を満たす Goal。Brain が「最優先 Need」から Goal を決めるのに使う。</summary>
        public Goal GoalForNeed(NeedType type)
        {
            switch (type)
            {
                case NeedType.Hunger: return Goal.Eat;
                case NeedType.Sleepiness: return Goal.Sleep;
                case NeedType.Thirst: return Goal.Drink;
                case NeedType.Playfulness: return Goal.Play;
                case NeedType.Affection: return Goal.SeekAffection;
                case NeedType.Scratchiness: return Goal.Scratch;
                default: return Goal.None;
            }
        }

        /// <summary>Goal → 対応する欲求(GoalForNeed の逆)。ヒステリシス判定・回復対象に使う。</summary>
        public NeedType NeedForGoal(Goal goal)
        {
            switch (goal)
            {
                case Goal.Eat: return NeedType.Hunger;
                case Goal.Sleep: return NeedType.Sleepiness;
                case Goal.Drink: return NeedType.Thirst;
                case Goal.Play: return NeedType.Playfulness;
                case Goal.SeekAffection: return NeedType.Affection;
                case Goal.Scratch: return NeedType.Scratchiness;
                default: return NeedType.Hunger; // Flee / None は欲求を持たない(この値は使われない)
            }
        }

        // ================= Goal ⇔ PointType =================

        /// <summary>
        /// Goal を満たすのに必要な地点種別。TargetSelector がこの種別で最良の CreaturePoint を探す。
        /// PointType.None を返す Goal は「地点を使わない」(SeekAffection=対人 等)。
        /// </summary>
        public PointType PointTypeForGoal(Goal goal)
        {
            switch (goal)
            {
                case Goal.Eat: return PointType.Food;
                case Goal.Drink: return PointType.Water;
                case Goal.Sleep: return PointType.Bed;
                case Goal.Play: return PointType.Toy;
                case Goal.Scratch: return PointType.ScratchPost;
                default: return PointType.None; // None / SeekAffection / Flee
            }
        }

        // ================= Goal ⇔ Motion =================

        /// <summary>
        /// その Goal を「地点で実行中(Acting)」のときに再生するモーション。
        /// CreatureAnimator が参照する(Idle/Walk/Flee は状態から直接決まるのでここには無い)。
        /// </summary>
        public MotionKind MotionForGoal(Goal goal)
        {
            switch (goal)
            {
                case Goal.Eat: return MotionKind.Eat;
                case Goal.Drink: return MotionKind.Drink;
                case Goal.Sleep: return MotionKind.Sleep;
                case Goal.Play: return MotionKind.Play;
                case Goal.Scratch: return MotionKind.Scratch;
                default: return MotionKind.Idle; // 専用モーションが無い Action は待機で代替
            }
        }

        // ================= 表示名(Udon は enum.ToString() で名前を返さない) =================

        public string GoalName(Goal g)
        {
            switch (g)
            {
                case Goal.Eat: return "Eat";
                case Goal.Drink: return "Drink";
                case Goal.Sleep: return "Sleep";
                case Goal.Play: return "Play";
                case Goal.SeekAffection: return "SeekAffection";
                case Goal.Scratch: return "Scratch";
                case Goal.Flee: return "Flee";
                default: return "None";
            }
        }

        public string NeedName(NeedType t)
        {
            switch (t)
            {
                case NeedType.Hunger: return "Hunger";
                case NeedType.Sleepiness: return "Sleepiness";
                case NeedType.Thirst: return "Thirst";
                case NeedType.Playfulness: return "Playfulness";
                case NeedType.Affection: return "Affection";
                case NeedType.Scratchiness: return "Scratchiness";
                default: return "?";
            }
        }

        public string MotionName(MotionKind k)
        {
            switch (k)
            {
                case MotionKind.Walk: return "Walk";
                case MotionKind.Eat: return "Eat";
                case MotionKind.Sleep: return "Sleep";
                case MotionKind.Flee: return "Flee";
                case MotionKind.Drink: return "Drink";
                case MotionKind.Play: return "Play";
                case MotionKind.Scratch: return "Scratch";
                case MotionKind.Groom: return "Groom";
                case MotionKind.Stretch: return "Stretch";
                case MotionKind.Yawn: return "Yawn";
                case MotionKind.Sit: return "Sit";
                case MotionKind.LookAround: return "LookAround";
                default: return "Idle";
            }
        }
    }
}
