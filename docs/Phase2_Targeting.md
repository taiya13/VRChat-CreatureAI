# Phase 2: TargetPoint 選択 + Reserve と 状態表示

対象: `CreatureTargetSelector`(新規)、`CreatureStatusDisplay`(新規)、
セットアップツールの壊れアセット掃除。移動・アニメ・行動実行は**まだ実装しない**。

## CreatureTargetSelector(責務: Goal→TargetPoint)

```
Brain.CurrentGoal ──▶ PointTypeForGoal(goal)   Eat→Food / Sleep→Bed / …
                          │
                          ▼
             Sensor.FindBest(need)  … Free の最良候補(Registry へ委譲)
                          │
                          ▼
                  best.Reserve(this)  … 占有先取り(仕様3.5)
                          │
                          ▼
                   TargetPoint 保持 + [Target] ログ
```

- **予約は Goal 決定と同時**(到達時ではない)。holder は暫定的に Selector 自身。
  ActionRunner 実装後に holder を移し、解放は AbortCurrent() に集約する。
- **同じ Goal 用の有効ターゲットを保持中は再検索しない**(毎 Tick 取り合う事故を防ぐ)。
- Goal 変更・無効化・候補消失の全経路で `ReleaseTarget()`(＝`CreaturePoint.Release`)を通す。
- 拡張点: `PointTypeForGoal()`。Utility 化しても Selector はそのまま使える。

CreatureCore の Tick(%5)で `GrowNeeds → Brain.Evaluate → Selector.SelectTarget` の順に実行。

ログ例:
```
[Brain]  Cat current goal: Eat  (Hunger=60.0, Sleepiness=52.0)
[Target] Cat reserved 'FoodBowl' for goal Eat
```

## CreatureStatusDisplay(責務: 表示のみ)

World Space Canvas 上の `UnityEngine.UI.Text` に、猫の状態をリアルタイム表示する
(仕様0章で延期していた頭上 HUD の簡易版)。判断はしない。

表示内容:
```
Cat
Goal   : Eat
Target : FoodBowl

Hunger     [■■■■■■□□□□] 60%
Sleepiness [■■■■■□□□□□] 52%
```

- CreatureCore が毎 Tick `UpdateDisplay()` を呼ぶ。
- `targetText` 参照はセットアップツールが Canvas ごと自動生成して割り当てる。

## セットアップツールの修正(重要)

「Value cannot be null. Parameter name: key」でコンポーネント追加が失敗していた原因は、
**ソース未設定(sourceCsScript=None)の壊れた ProgramAsset** が残っていて、UdonSharp の
内部辞書(クラス→アセット)作成が null キーで落ちていたこと。

- メニュー1 と 2 の冒頭で、壊れ ProgramAsset を自動削除するようにした。
- メニュー0(診断)で壊れアセット数を表示。
- メニュー9「壊れた Program Asset を掃除」を単体でも実行可能。

## 更新手順(Unity)

1. zip を今の場所に上書き(新規 .cs が4つ増える)
2. コンパイル完了を待つ
3. **`CreatureAI > 1. Program Asset を作成`**(壊れ掃除＋新規2種の生成)
4. 旧 Cat を削除 → **`CreatureAI > 2. テスト用の猫を作成`**
   (Cat + FoodBowl + Bed + 頭上ボードが生成される)
5. ▶ Play。頭上ボードにバーと Goal/Target が出て、Console に [Target] ログが出れば成功。

> Eat/Sleep の切り替わりを見るには、Profile の GrowthRate を大きめにすると早い。
