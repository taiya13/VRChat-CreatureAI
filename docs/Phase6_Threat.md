# Phase 6: 割り込み・中断・危険回避(Flee)の基盤

## 追加/変更

- `Goal.Flee` を追加(Need に対応しない特別 Goal。危険時に最優先)。
- `ThreatEvaluator`(新規): 危険検知のみ。今回はローカルプレイヤーが `threatDistance`
  以内に近づいたら危険とみなす。将来 PlayerRecognition / DangerSpot / 複数プレイヤーを
  この中で合成できる土台。
- 割込みの指揮は `CreatureCore`(毎 Tick):
  `ThreatEvaluator.Check()` → 危険なら `ActionRunner.AbortCurrent()`(中断・解放)
  → `Brain.ForceFlee()`(Goal→Flee)。危険が去れば `Brain.EndFlee()`。
- `MovementController`: Flee 中は TargetPoint を使わず、脅威源から離れる方向へ
  `fleeSpeedMultiplier` 倍速で走る。
- 重複排除(レビュー項目C): `GoalName` / `NeedForGoal` を `CreatureBrain` の public
  メソッドに一本化。ActionRunner / TargetSelector は brain 参照から呼ぶ
  (新クラス・新パターン無しの安全な最小整理)。

## 状態遷移(割込み)

```
[通常ループ]  Idle → Moving → Acting → …(Needs駆動)
     │
     │ ThreatEvaluator が危険を検知(プレイヤー接近)
     ▼
CreatureCore が割込み:
   ActionRunner.AbortCurrent()   // 行動停止・予約解放(Release)・到着リセット
   Brain.ForceFlee()             // Goal = Flee(ヒステリシス無視)
     ▼
[Flee]  AgentState=Moving、MovementController が脅威源の逆方向へ走る
     │
     │ プレイヤーが threatDistance の外へ
     ▼
CreatureCore: Brain.EndFlee() → Goal=None → 次の Evaluate で通常再評価に復帰
```

- 解放は `TargetSelector.ReleaseTarget()` の1経路のみ。割込みも `AbortCurrent()`
  経由でそこを通るため、占有が残る事故が起きない。

## 責務(Phase 6 時点)

| クラス | 責務 |
|---|---|
| ThreatEvaluator | 危険の**検知のみ**(IsThreatened / GetThreatPosition) |
| CreatureCore | 割込みの**指揮**(検知→中断→Goal切替→復帰) |
| CreatureBrain | Goal 保持。ForceFlee/EndFlee で割込みを受理。対応表(GoalName/NeedForGoal)の唯一の定義 |
| ActionRunner | 占有・行動・状態。AbortCurrent が単一中断経路。Flee 中は行動しない |
| TargetSelector | 予約/解放プリミティブ。Flee では地点を取らない |
| MovementController | 通常は Target へ、Flee 中は脅威源から離れて走る |

## テスト方法

1. zip 上書き → コンパイル待ち。
2. `CreatureAI > 1. Program Asset を作成`(新規 ThreatEvaluator を生成)。
3. 旧 Cat を削除 → `CreatureAI > 2. テスト用の猫を作成`。
4. ▶ Play。Scene ビューで猫の周囲に**危険範囲(赤い薄い球, 既定3m)**が見える。
5. Play 中に**プレイヤー(シーンビューのカメラではなく、ゲーム内の自分)を猫に近づける**。
   - デスクトップテスト時は、猫の `ThreatEvaluator > Threat Distance` を大きく(例 8)
     すると発火させやすい。
6. 近づくと: `[Threat] 危険検知` → `[Brain] INTERRUPT → Flee` →
   `[Action] finished`(中断)→ 猫が離れる方向へ走る。ポイントは Free に戻る。
7. 離れると: `[Threat] 安全` → `[Brain] flee end` → 通常の生活ループへ復帰。

HUD/Scene ラベルに `Goal: Flee` と `[Moving]`、`Threat: YES/no` が出る。

## 将来拡張の入り口

- **PlayerRecognition**: `PlayerRelationshipManager` を作り、関係(警戒/信頼)で
  `ThreatEvaluator.threatDistance` を可変に。慣れた相手には逃げない等。
- **DangerSpot**: 危険地点(掃除機/水場)を `CreaturePoint` 的に配置し、
  ThreatEvaluator.Check() で最短の脅威源として合成。
- **Memory/Personality/Utility AI**: Brain の `ScoreOf()` に記憶・性格・時間帯の係数を
  足すだけで発展可能(構造は不変)。
