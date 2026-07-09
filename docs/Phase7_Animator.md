# Phase 7: アニメーション制御の基盤(CreatureAnimator)

## 目的

これまでの AI(Brain・ActionRunner・Movement・Threat・AgentState)の状態を
「見た目」へ反映する土台を作る。**アニメーション自体は作らず**、状態に応じて
Animator を切り替える仕組みだけを用意する。

## 追加したクラス

| クラス | 役割 |
|---|---|
| `MotionKind`(列挙) | 動作種別: 待機(Idle)/歩く(Walk)/食べる(Eat)/眠る(Sleep)/逃げる(Flee)。値は Animator の整数パラメータ `MotionState` と一致 |
| `CreatureAnimator` | AgentState と Goal を読み、動作種別を決めて Animator の整数パラメータに反映する**だけ**。Brain・Movement には一切アニメ処理を書かない |

## 各クラスの責務(責務分離は維持)

- **決定** = `Brain`(Goal)、`ActionRunner`(AgentState)。← 従来どおり、変更なし。
- **見た目への反映** = `CreatureAnimator`(新規)。状態→動作種別→Animator パラメータ。
- **アニメの中身** = `AnimatorController`(Unity 側)。クリップと遷移。
- **指揮** = `CreatureCore` が毎 Tick `UpdateAnimation()` を呼ぶだけ。

### 動作種別の決定表(唯一の対応箇所 = 拡張点)
`CreatureAnimator.DecideMotion()`:
```
Goal == Flee                → 逃げる
AgentState == Acting かつ Eat → 食べる
AgentState == Acting かつ Sleep → 眠る
AgentState == Moving         → 歩く
それ以外                      → 待機
```
毛づくろい・あくび・伸び等を足すときは、**ここに条件を1行**、`MotionKind` に種別、
`AnimatorController` に状態を足すだけ。

## AnimatorController の構成(自動生成)

セットアップが `Assets/CreatureAI_Generated/CreatureAnimator.controller` を自動生成する。

- 整数パラメータ **`MotionState`**(0=待機,1=歩く,2=食べる,3=眠る,4=逃げる)
- 状態: Idle / Walk / Eat / Sleep / Flee(既定 = Idle)
- 遷移: **AnyState → 各状態**(条件 `MotionState == 値`、Has Exit Time オフ、
  ブレンド0.12秒、自己遷移オフ)
- 各状態にはプレースホルダのモーション(体が少し弾む/沈む/揺れる)を入れてある
  → クリップが無くても「動いて見える」。**本番は自分のクリップに差し替える**。

`CreatureAnimator` は `Animator.SetInteger("MotionState", 値)` を、状態が変わった時だけ送る。

## Unity での設定方法

**自動(推奨)**: `CreatureAI > 2. テスト用の猫を作成` で、Cat に `Animator` と
`CreatureAnimator` が付き、生成した Controller が自動割り当てされる。何もしなくてよい。

**手動 / 自分のモデルに差し替える場合**:
1. モデルの `Animator` を用意し、`CreatureAnimator.animator` に割り当てる。
2. その Controller に整数パラメータ `MotionState` を作る。
3. Idle/Walk/Eat/Sleep/Flee の状態を作り、AnyState から
   `MotionState == 0..4` の遷移を張る。
4. 各状態に自分のアニメクリップを割り当てる。
(生成済み Controller をコピーして中身のクリップだけ差し替えるのが早い)

**他の動物(犬・鹿)**: `CreatureAnimator` は種族非依存。Controller を差し替えるだけで
同じ仕組みで別モーションになる。

## 実機でのテスト方法

1. zip 上書き → コンパイル待ち。
2. `CreatureAI > 1. Program Asset を作成`(新規 CreatureAnimator を生成)。
3. 旧 Cat を削除 → `CreatureAI > 2. テスト用の猫を作成`。
4. ▶ Play。
   - 猫が歩くと体が弾み(Walk)、餌前で沈み(Eat)、寝ると平たくなり(Sleep)、
     プレイヤーが近づくと震えて逃げる(Flee)——プレースホルダの動きで確認できる。
   - Console に `[Animator] … motion → Walk/Eat/Sleep/Flee/Idle` が状態変化時に出る。
   - Scene ラベルに `[AgentState / Motion]`(例 `[Moving / Walk]`)が出る。
   - Animator ウィンドウを開くと、現在の状態がハイライト移動するのが見える。

## Phase 7 の成功条件

- AI の状態(移動/食事/睡眠/逃走/待機)に応じて **Animator の状態が正しく切り替わる**。
- アニメ処理が `CreatureAnimator` に閉じており、**Brain・Movement には無い**(責務分離)。
- 動作種別・Animator パラメータ・Controller 状態を**足すだけ**で演出拡張できる構造になっている。

## 今後の演出拡張(この土台で対応可能)

- 毛づくろい/あくび/伸び: `MotionKind` に足し、`DecideMotion()` に条件、Controller に状態。
- しっぽ・耳・表情: Animator に**別パラメータ/別レイヤー**を足し、必要なら
  CreatureAnimator に「感情」等の反映を1つ足す(既存構造は不変)。
- 個体差(Personality): 同じ Idle でも待ち時間や癖を変える等、Animator 側や
  追加パラメータで表現可能。
