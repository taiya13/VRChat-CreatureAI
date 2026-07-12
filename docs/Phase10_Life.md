# Phase 10 — 生活感(自由時間・視線・危険回避の一時停止)

猫が「食べる・寝る・移動する」だけでなく、**暇な時間に猫らしく振る舞い、近くの対象へ
視線を向ける**ようにして生活感を高めるフェーズ。あわせて、プレイヤーとの触れ合いを
優先するため危険回避(逃走)を一時停止する。既存アーキテクチャ・各層の責務は不変。

## 追加したクラス

| クラス | 役割 |
|---|---|
| **`CreatureIdleBehavior`** | 自由時間(AgentState=Idle)に、Needs と性格から所作(毛づくろい/あくび/伸び/見回す/座る/遊ぶ)を重み付き抽選し、その MotionKind を CreatureAnimator へ渡す。 |
| **`CreatureGaze`** | 近くの気になる対象(プレイヤー/地点/他の猫/任意)へ「頭だけ」を向ける。体・移動・行動には触れない(自己駆動 Update)。 |
| `IdleActivity`(enum) | 自由行動の種類(Stand/Sit/Groom/Yawn/Stretch/LookAround/Play)。 |

## 変更したクラス

| クラス | 変更内容 |
|---|---|
| `MotionKind`(enum) | `Yawn=10 / Sit=11 / LookAround=12` を追加。 |
| `CreatureActionCatalog` | `MotionName` に上記3種を追加(表示用)。 |
| `CreatureAnimator` | Idle 時に `CreatureIdleBehavior.GetIdleMotion()` の演出へ差し替え(注入を1つ追加)。 |
| `CreatureCore` | `CreatureIdleBehavior` をキャッシュ・注入・毎 Tick 実行。危険回避処理を `fleeEnabled` でゲート。 |
| `ThreatEvaluator` | `fleeEnabled`(既定 **false**)を追加。逃走の一時停止/再有効化のスイッチ。 |
| `CreaturePointRegistry` | 猫インデックス(`RegisterCreature`/`CollectNearbyCreatures`)を追加。視線が「他の猫」を探すのに使う(`FindObjectsOfType` を使わない)。 |
| `CreatureAISetup`(Editor) | `CreatureIdleBehavior`/`CreatureGaze` を同梱。頭の仮オブジェクト(Head)を作り `headTransform` に割当。Animator に Yawn/Sit/LookAround 状態を追加。 |
| `CreatureAIGizmos`(Editor) | 視線を Scene に可視化(黄の線)。モーション名表示を全種対応に更新。 |

**Brain / Needs / ActionRunner / MovementController は無改造。**

## 1. 自由行動はどう選ばれるか

`CreatureIdleBehavior` は毎 Tick、AgentState が **Idle(空腹・眠気・移動・逃走が無い暇な時間)**
のときだけ働く。動きは以下:

1. 前回の選択から一定時間(`actionDurationMin`〜`Max`、のんびりな猫ほど長い)経つと、次の
   所作を選び直す(せわしなく切り替わらない)。
2. 選択は**完全ランダムではなく重み付き抽選**。各所作の重み `Weight()` を Needs と性格から算出し、
   その比率で1つ選ぶ。傾向は出るが毎回同じにはならない(生き物らしいゆらぎ)。
   - **座る/毛づくろい**: のんびりさ(relaxedness)が高いほど増える。毛づくろいは爪とぎ欲でも増える。
   - **あくび**: 眠気(Sleepiness、閾値未満でも)が溜まるほど増える。
   - **伸び**: 活発さ(activeness)が低い猫ほど増える。
   - **見回す**: 好奇心(curiosity)・臆病さで増える。
   - **遊ぶ**: 活発さ・遊びたさ(Playfulness)で増える。
3. 選んだ所作に対応する MotionKind を `CreatureAnimator` が Idle 時に再生する。
   「少し歩き回る」は徘徊(Wander)が担うので、ここではその場の所作のみ。

**新しい自由行動の追加**: `IdleActivity` に列挙を1つ足し、`MotionForActivity` と `Weight` に
1行ずつ、`MotionKind` と AnimatorController に状態を足すだけ(他は不変)。

## 2. 視線システムの仕組み

`CreatureGaze` は自己駆動(Update)で、**行動を中断せず頭(headTransform)だけ**を対象へ向ける。

- **対象選択(`retargetInterval` ごと)**: 候補を集めて `score = 興味度 / (1 + 距離)` が最大のものを選ぶ。
  候補は既定で **プレイヤー(興味度最大)/ 近くの CreaturePoint(Food/Water/Bed 等)/ 近くの他の猫 /
  extraTargets(Inspector)**。対象カテゴリは `EvaluateBest()` に候補を足すだけで増やせる。
- **頭の回転(毎フレーム)**: 対象方向へ `headTurnSpeed` で滑らかに回す。ただし体の正面から
  `maxYaw`/`maxPitch` を超える対象は「見えない」ものとして無視し、正面へ戻す(首が不自然に
  ねじれない)。体の向き・位置・移動・アニメ状態には一切触れないので、食事中でも歩行中でも
  視線だけが動く。
- **他の猫**: 各 `CreatureGaze` が起動時に自分を `CreaturePointRegistry` の猫インデックスへ登録し、
  そこから近くの猫を引く(重い全体検索をしない)。
- **モデル差し替え**: 実モデルでは頭ボーンを `headTransform` に割り当てる。未割り当てなら視線対象の
  計算のみ行い見た目は変えない(Scene の黄色い線で対象を確認できる)。

Inspector 主パラメータ: `gazeRange`(見る距離)/ `maxYaw`・`maxPitch`(首振り上限)/
`headTurnSpeed` / `retargetInterval` / 各 `interest*`(対象ごとの興味度)/ `extraTargets`。

## 3. 危険回避を再び有効化する方法

危険回避(プレイヤーから逃げる)は **`ThreatEvaluator.fleeEnabled`(既定 false)** で止めている。
`ThreatEvaluator` も逃走用の Movement 経路も**削除せず温存**してあるので、再有効化は簡単:

- **Inspector**: Cat の **ThreatEvaluator > Flee Enabled** に**チェックを入れる**だけ。
- 全 Cat をまとめて戻したい場合は、各 Cat の同項目を ON にする(将来メニュー化も可能)。

ON にすると `CreatureCore` の危険検知ブロックが復活し、従来どおり「プレイヤー接近 → 行動中断
(AbortCurrent)→ Flee」で逃げる。OFF の間は検知・中断・逃走を一切行わない(視線はプレイヤーを
見るが、体は逃げない)。

## Unity でのテスト方法

1. `CreatureAI > 1. Program Asset を作成` →（Animator を更新するなら）`3. Animator Controller を再生成`。
2. `CreatureAI > 2. テスト用の猫を作成` → ▶ Play。
3. **自由行動**: 空腹・眠気が低い間、猫が座る/毛づくろい/あくび/見回す等をする。頭上 HUD の
   `Motion : …` が Sit/Groom/Yawn 等に変わる。性格プリセット(`CreatureAI > 性格`)を変えると傾向が変わる。
4. **視線**: Scene ビューで、猫から対象へ**黄色い線**が出る。Play 中にシーンを歩く(ClientSim)と、
   頭がプレイヤーを追う。近くに餌皿/他の猫を置くと、状況で視線が移る。
5. **危険回避 OFF**: プレイヤーが近づいても逃げないことを確認。ThreatEvaluator の `fleeEnabled` を
   ON にすると再び逃げる。

## 既知の制限事項

- 自由行動は「その場の所作」中心。歩き回りは徘徊(Wander)に委ねる(移動は Movement の責務のまま)。
- 視線は `headTransform` を割り当てないと見た目に反映されない(仮モデルは Head 球を自動割当)。
  1体1つの頭のみ(複数ボーンの分割注視は範囲外)。
- 他の猫の注視は同一シーンの Registry 経由。動的に大量生成/破棄する猫には未対応(登録は起動時のみ)。
- 危険回避 OFF 中は DangerSpot 等の他の脅威源にも反応しない(fleeEnabled 一括制御のため)。
