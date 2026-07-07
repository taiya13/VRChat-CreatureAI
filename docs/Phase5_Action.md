# Phase 5: ActionRunner(行動の実行と欲求回復)+ 行動しきい値

対象: `ActionRunner`(新規)、`CreatureBrain`(ヒステリシス化)、
`CreatureProfile`(actionThreshold / actionRecoverRate 追加)、行動しきい値メニュー。
アニメーションは**まだ実装しない**。

## 閉じたループ

```
欲求が溜まる (NeedsController)
   │  値 >= actionThreshold
   ▼
Brain: Goal 開始 (Eat/Sleep)
   ▼
TargetSelector: 対応 Point を Reserve
   ▼
MovementController: 歩いて到着 (IsAtTarget)
   ▼
ActionRunner: Occupy → 対応 Need を actionRecoverRate で回復
   │  値 <= CompleteLevel(=5)
   ▼
Brain: Goal 完了 → None (ヒステリシス)
   ▼
TargetSelector: Release(Free に戻る)  /  ActionRunner: 行動終了
   ▼
Brain: 次を再評価(空腹→満腹になったので今度は睡眠…)
```

## 行動しきい値(ヒステリシス)

- **開始**: 最優先 Need の値が `Profile.actionThreshold`(既定 40)**以上**で Goal 開始。
- **継続**: 行動中はその Need が `CompleteLevel`(=5)以下になるまで同じ Goal を維持
  (食べ始めたら満たされるまで食べ続ける)。
- **完了**: 満たされたら Goal を手放し再評価。

しきい値はメニュー **`CreatureAI > 行動しきい値`**(すぐ動く 10 / ふつう 40 /
なまけ 70 / ギリギリ 90)か、`Profile` の Inspector で変更可。再生中は即反映。

## 各クラス

| クラス | 変更/責務 |
|---|---|
| `ActionRunner` | 到着中かつ Goal ありなら Occupy し、対応 Need を回復。条件が崩れたら終了。占有解放はしない(Selector が Goal 変化で Release) |
| `CreatureBrain` | しきい値＋ヒステリシスで Goal を開始/継続/完了。Profile.actionThreshold を参照 |
| `CreatureProfile` | `actionThreshold`(開始ゲージ) / `actionRecoverRate`(回復速度) を追加 |
| `CreatureStatusDisplay` | HUD に `Action:` 行を追加 |

## ログ例

```
[Brain]  Cat current goal: Eat  (Hunger=41.0, Sleepiness=12.0, th=40.0)
[Target] Cat reserved 'FoodBowl' for goal Eat
[Move]   Cat arrived at 'FoodBowl' (dist=0.55m)
[Action] Cat started Eat at 'FoodBowl'      ← ここで FoodBowl が Occupied(赤)
[Brain]  Cat goal complete: Eat (satisfied)  ← Hunger が 5 以下に回復
[Action] Cat finished Eat
```

## 更新手順

1. zip を上書き → コンパイル待ち
2. `CreatureAI > 1. Program Asset を作成`(新規 ActionRunner を生成)
3. 旧 Cat を削除 → `CreatureAI > 2. テスト用の猫を作成`
4. `CreatureAI > 欲求の速さ > 食欲 > はやい` などで溜まりを速く、
   `CreatureAI > 行動しきい値 > すぐ動く` で早く動かすと観察しやすい
5. ▶ Play。空腹 → 餌へ歩く → 食べる(Occupied)→ 満腹 → 離れる → 次の欲求…が回る。
