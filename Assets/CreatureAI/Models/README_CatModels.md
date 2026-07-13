# 猫モデル & アニメーション

`Cat_12221.fbx` / `Cat_12222.fbx`（同一リグ・同一クリップ構成、メッシュとテクスチャが別）。
Unity Generic 用の猫リグ（Humanoidではありません）+ 全13アニメーション入り。

## セットアップ手順（CreatureAIメニューの番号メニューと連携）

前提: `CreatureAI > 1. Program Asset を作成` と `2. テスト用の猫を作成` を実行済みで、
シーンに `Cat`（本体AIコンポーネント一式＋仮の Body カプセル）が存在すること。

1. メニュー **CreatureAI > 5. 猫モデルをセットアップ (Import+Controller)** を実行
   - 全クリップのループ設定、クリップ名の正規化（`Rig|Cat_Walk` → `Cat_Walk`）
   - `Assets/CreatureAI/Animations/Cat_12221_Animator.controller`（12222も同様）を
     int パラメータ `MotionState`（`CreatureAnimator.parameterName` と一致）で生成
2. `Cat` の子にある仮の **Body**（オレンジのカプセル、その子にSphereのHead）を削除
3. `Models/Cat_12221.fbx`（または `Cat_12222.fbx`）をシーンへドラッグし、`Cat` の子にする
   （Transformは position (0,0,0) / scale (1,1,1) のままでOK。地面の高さに合わせて
   正規化済みなので、仮Bodyの底面と同じ高さに自然に立ちます）
4. ドラッグしたモデルに付いている **Animator** の **Controller** に、手順1で生成した
   `Cat_12221_Animator`（モデルに合わせて12222なら12222の方）を割り当てる
5. （任意・推奨）`Cat` の **CreatureGaze** コンポーネントの **Head Transform** を、
   仮Headの代わりにモデル内の `Head` ボーン（`.../Chest/Neck/Head`）にドラッグし直す
6. テクスチャ割り当て（下記）
7. Playして確認: Console に `[Animator] Cat 反映先 n件` (n≥1) が出ればOK。
   `n=0` の警告が出る場合は手順4のController割り当てを見直してください
   （`CreatureAI > 3. Animator Controller を再生成` は **空のAnimatorだけ**に
   割り当てるツールなので、手順4を飛ばして単独では繋がりません）

## テクスチャ

FBXにはテクスチャ画像は含まれていません。元素材のJPGを以下の名前でこのフォルダに置くと、
マテリアル `Cat_12221_Mat` / `Cat_12222_Mat` の BaseColor に割り当てられます
（自動で繋がらない場合はマテリアルのAlbedoに手動ドラッグ）。

| モデル | 置くファイル | 元ファイル名 |
|---|---|---|
| Cat_12221 | `Cat_12221_diffuse.jpg` | 12221素材の `Cat_diffuse.jpg` |
| Cat_12222 | `Cat_12222_diffuse.jpg` | 12222素材の `Cat_diffuse.jpg` |

（`Cat_bump.jpg` はNormal/Heightとして任意で追加）

## アニメーション一覧（Animator int パラメータ `MotionState` = `MotionKind` と一致）

| MotionState | クリップ | 内容 | 長さ | ループ |
|---|---|---|---|---|
| 0 | Cat_Idle | 待機（呼吸・尻尾・耳・視線の揺らぎ） | 8.0s | ○ |
| 1 | Cat_Walk | 歩行（4拍ウォーク） | 0.8s | ○ |
| 2 | Cat_Eat | 食事（頭を下げてついばむ） | 3.0s | ○ |
| 3 | Cat_Sleep | 睡眠（香箱座り・ゆっくり呼吸） | 8.0s | ○ |
| 4 | Cat_Flee | 逃走（ギャロップ） | 0.4s | ○ |
| 5 | Cat_Drink | 水飲み（速いラッピング） | 3.0s | ○ |
| 6 | Cat_Play | 遊び（前傾＋左右の前足でパンチ） | 2.8s | ○ |
| 7 | Cat_Scratch | 爪とぎ（立ち上がって交互に引っ掻く） | 2.0s | ○ |
| 8 | Cat_Groom | 毛づくろい（お座りで前足を舐める） | 4.0s | ○ |
| 9 | Cat_Stretch | 伸び（前伸ばし→戻る、待機に接続可） | 2.8s | ○ |
| 10 | Cat_Yawn | あくび（頭上げ＋口開け→戻る） | 2.2s | ○ |
| 11 | Cat_Sit | お座り（尻尾巻き・微動） | 6.7s | ○ |
| 12 | Cat_LookAround | 周囲を見回す（左→右→戻る） | 5.3s | ○ |

- すべて **その場（in-place）** アニメーション。移動はスクリプト側でTransformを動かす前提
- StretchとYawnはループ可能だが実質ワンショット演出（開始・終了が待機姿勢に一致）

## 移動速度の目安（フットスライド防止）

歩幅とサイクル長から算出した「アニメと一致する移動速度」:

| クリップ | Transform移動速度 |
|---|---|
| Cat_Walk | **0.271 m/s** |
| Cat_Flee | **1.54 m/s** |

`CreatureProfile` の移動速度をこの値に合わせると足滑りが最小になります。
（速度を変えたい場合は Animator の State の Speed を `希望速度 / 上記値` に設定）

## 複数匹をシーンに置く場合

`2. テスト用の猫を作成` は既存の `Cat` 数だけ位置をずらして複数体作れます
（`Cat`, `Cat 2`, `Cat 3`...）。それぞれに手順3-5を行い、`Cat_12221.fbx` と
`Cat_12222.fbx` を使い分けると、同じリグ・同じ13アニメーションのまま
毛色違いの猫が混在する群れになります。

## リグ構成（Generic）

```
Root ─ Body ─ Pelvis ─ Spine1..3 ─ Chest ─ Neck ─ Head ─ (Jaw, Ear_L/R)
              ├ Tail1..5
              ├ Thigh/Shin/Foot/Toe (_L/_R)
              └ (Chest) Scapula/UpperArm/Forearm/FrontPaw (_L/_R)
```

- 単位: メートル（体高 約28cm・実寸大の猫）。前方は +Z（Unity標準）
- 追加アニメーションは同名ボーンでBlender側から追記可能
  （制作パイプラインは `vrc-ai-sozai` リポジトリの `cats/` を参照）
