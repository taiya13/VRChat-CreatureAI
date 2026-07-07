# Brain 基盤(意思決定層の土台)

対象: `NeedType` / `Goal`(enum)、`NeedsData`、`NeedsController`(改修)、`CreatureBrain`

目的: **「行動を決める」のではなく「今もっとも優先すべき Need を判断し CurrentGoal を決める」**
ところまで。点検索・Utility AI 本体・Action 選択・移動・アニメは含めない。

## データの流れ

```
NeedsController.GrowNeeds()   … Hunger/Sleepiness を時間で増やす
        │  書き込む
        ▼
     NeedsData                … 全 Need を配列 1 本で保持(NeedType がインデックス)
        │  読み取る
        ▼
CreatureBrain.Evaluate()      … 値×重み が最大の Need を選び CurrentGoal を更新
        │
        ▼
   CurrentGoal (Eat / Sleep / None …)   … 変化時のみ [Brain] ログ
```

CreatureCore の Tick(`tickCounter % 5`)で「GrowNeeds → Evaluate」の順に呼ぶ。

## 各クラスの責務

| クラス | 責務 |
|---|---|
| `NeedType`(enum) | Need の種類。NeedsData 配列の添字。Hunger/Sleepiness のみ稼働、残りは拡張枠 |
| `Goal`(enum) | Brain の出力。Eat/Sleep が稼働、None=待機、残りは拡張枠 |
| `NeedsData` | 現在値を配列で一元保持するだけ(ロジックなし)。0-100 クランプ |
| `NeedsController` | NeedsData の値を増やす/充足する。優先度判断は持たない |
| `CreatureBrain` | NeedsData を読み、最優先 Need → CurrentGoal を決定・保持。変化時ログ |

## 優先度の決め方(Utility AI への発展)

`CreatureBrain.ScoreOf(need, value) = value × weight(Profile)` の最大を選ぶ。
- 例: Hunger=60(重み1.0)=60、Sleepiness=70(重み0.9)=63 ⇒ **Sleep**
- 重み既定値では概ね「値が大きい Need」が勝つ

将来 Utility AI にするときは **`ScoreOf()` に項を足す**だけ(距離・時間帯・性格・ヒステリシス等)。
Need→Goal の対応は `GoalForNeed()` に集約。この2メソッドが拡張点。

## Cat の構成(更新後)

```
Cat (root)
├─ CreatureCore
├─ NeedsController
├─ NeedsData          ← 追加
├─ CreatureBrain      ← 追加
├─ CreaturePointSensor
├─ Profile              (子) └ CreatureProfile
└─ __CatAI_Registry     (子) └ CreaturePointRegistry
```

## 動作確認

`activationThreshold = 0`(既定)なら、Need が増え始めると即 Goal が付く。

1. セットアップツールで Program Asset を作り直し(`CreatureAI > 1`)。NeedsData/CreatureBrain 用が追加生成される
2. 旧 Cat を削除 → `CreatureAI > 2` で作り直し
3. ▶ Play → Console に例:
   ```
   [Brain] Cat current goal: Eat  (Hunger=1.2, Sleepiness=0.8)
   ...(眠気が空腹を上回った時)...
   [Brain] Cat current goal: Sleep  (Hunger=60.0, Sleepiness=63.2)
   ```
   Goal が変わった瞬間だけログが出る。

> 早く Goal の切り替わりを見たい場合は Profile の GrowthRate を大きくする。
> Hunger と Sleepiness の切り替わりを見たいなら、sleepinessGrowthRate を
> hungerGrowthRate より少し大きめ(既定は 0.8 vs 1.0 なので逆に空腹が先に来る)にする。
