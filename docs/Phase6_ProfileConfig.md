# Need パラメータの個別設定(SDK 化)+ ステータス設定ウィンドウ

## 目的

各 Need を個別に調整できるようにし、SDK として拡張しやすくする。
Need ごとに次の4つを設定可能:

| 項目 | 意味 | 使う場所 |
|---|---|---|
| Increase Rate | 時間経過による増加速度(/秒) | NeedsController.GrowNeeds |
| Decrease Rate | 行動中の回復(減少)速度(/秒) | ActionRunner |
| Threshold | 行動開始の基準値 | CreatureBrain(開始判定) |
| Weight | Brain の優先度計算の重み | CreatureBrain.ScoreOf |

## データ構造(配列化)

`CreatureProfile` は Need ごとの値を **NeedType をインデックスとする配列**で持つ:

```csharp
public float[] increaseRate; // Hunger, Sleepiness, Thirst, Playfulness, Affection
public float[] decreaseRate;
public float[] threshold;
public float[] weight;
```

アクセサ `GetIncreaseRate(NeedType)` などは範囲外を既定値で返すので、
配列が短くても壊れない。消費側は全て `NeedType` で引く:

- NeedsController: 全 Need を `increaseRate[nt]` で増加(0 の Need は増えない)
- Brain: `weight[nt]` で優先度、`threshold[best]` で開始判定
- ActionRunner: `decreaseRate[nt]` で回復

## 編集ウィンドウ

メニュー **`CreatureAI > ステータス設定 (Need パラメータ編集)`** で開く。

- 対象 Profile を指定(または「シーンから自動取得」)
- Need × {Increase / Decrease / Threshold / Weight} を**表で一括編集**
- Move Speed / Reserve Timeout も編集
- クイックプリセット(観察向け / ふつう / テスト向け)で増加・回復をまとめて設定
- **適用**で保存。「全 Cat に適用」ON なら全 CreatureProfile へ。
  再生中は Udon 変数へ直接反映(即時)

旧メニュー「欲求の速さ / 行動しきい値」はこのウィンドウに統合・置き換え。

## 新しい Need(Water / Fun / Social 等)の追加手順

1. `NeedType` に列挙を追加(例: `Water = 5`)。
2. `NeedsData` の `NeedCount` をその数に合わせる。
3. `Goal` と、`Brain.GoalForNeed` / `Brain.NeedForGoal` / `TargetSelector.PointTypeForGoal`
   に対応(例: Water→Drink→PointType.Water)を追加。
4. `CreatureProfile` の各配列を同じ長さに拡張(ウィンドウが行を自動表示するので、
   値はウィンドウで入力すればよい)。

→ パラメータ管理は配列＋ウィンドウで共通化されているので、増やすのは
   「enum＋対応表＋NeedCount」の最小限で済む。
