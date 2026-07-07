# Phase 4: Movement(移動と到着判定)+ 表示強化

対象: `MovementController`(新規)、`CreaturePointStatusDisplay`(新規)、
`Billboard`(新規)、セットアップの見た目オブジェクト生成。
食事・睡眠・アニメ・Action 実行は**まだ実装しない**。

## MovementController(責務: 移動と到着判定)

- TargetSelector が持つ TargetPoint へ、水平(XZ)で `moveSpeed`(Profile)で移動。
- 進行方向へ `turnSpeed` で回頭。
- `StopDistance` 以内で「到着」。**1回だけ**ログ、以降は停止。
- 滑らかさが要るため Tick ではなく `Update()` 駆動(仕様どおり)。占有状態は触らない。
- 到着状態は `IsAtTarget()` で公開 → 後で ActionRunner が
  「到着したら Occupy → 行動 → Satisfy → Release」と繋ぐ拡張点。

ログ例:
```
[Target] Cat reserved 'FoodBowl' for goal Eat
[Move]   Cat arrived at 'FoodBowl' (dist=0.55m)
```

## 表示の追加

- **CreaturePointStatusDisplay**: 各ポイントの上に Free/Reserved/Occupied を
  色つきで**ワールド内表示**(Scene の Gizmo と違い Play 中も見える)。予約者名も表示。
- **Billboard**: 状態ボードが常に閲覧者(VRChat のローカルプレイヤー頭/エディタは
  MainCamera)を向く。猫が回頭してもボードは読める向きを保つ。

## 見えるオブジェクト(差し替え可能)

セットアップが以下を自動生成:
- Cat に体(Capsule)/ FoodBowl(Cylinder)/ Bed(Cube)。色付き・コライダー無し。
- **差し替え方法**: 各オブジェクトの子 `Body` / `Mesh` を消して、好きなモデルを
  同じ場所(Cat の子 / ポイントのルート下)へ置くだけ。CreaturePoint は
  transform.position で位置を見るので、どんなオブジェクトでも動く。

構造(ポイント):
```
FoodBowl (root, CreaturePoint, 無スケール)
├─ Mesh        (見た目・差し替え可)
└─ StatusLabel (World Canvas + CreaturePointStatusDisplay + Billboard)
```

## 更新手順

1. zip を上書き → コンパイル待ち
2. `CreatureAI > 1. Program Asset を作成`(新規 3 種: Movement/PointStatus/Billboard)
3. 旧 Cat / 旧 FoodBowl / 旧 Bed を削除 → `CreatureAI > 2. テスト用の猫を作成`
4. ▶ Play → 猫が Target まで歩いて停止。ポイント上の表示が Free→Reserved に変わる。

> Bed へ歩かせたい場合は `CreatureAI > 欲求の速さ > 睡眠 > はやい` かつ
> `食欲 > のんびり` にすると Sleep が優勢になり、猫が Bed へ向かう。
