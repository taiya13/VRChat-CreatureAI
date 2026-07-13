# 猫モデル & アニメーション

`Cat_12221.fbx` / `Cat_12222.fbx`（同一リグ・同一クリップ構成、メッシュとテクスチャが別）。
Unity Generic 用の猫リグ（Humanoidではありません）+ 全13アニメーション入り。

## セットアップ手順

1. このリポジトリをUnityで開く（FBXが自動インポートされる）
2. メニュー **CreatureAI > Setup Cat Animations** を実行
   - 全クリップのループ設定、クリップ名の正規化（`Rig|Cat_Walk` → `Cat_Walk`）
   - `Assets/CreatureAI/Animations/Cat_12221_Animator.controller`（12222も同様）を自動生成
3. シーンにFBXを配置し、`Animator` に生成されたControllerを割り当て
4. テクスチャ割り当て（下記）

## テクスチャ

FBXにはテクスチャ画像は含まれていません。元素材のJPGを以下の名前でこのフォルダに置くと、
マテリアル `Cat_12221_Mat` / `Cat_12222_Mat` の BaseColor に割り当てられます
（自動で繋がらない場合はマテリアルのAlbedoに手動ドラッグ）。

| モデル | 置くファイル | 元ファイル名 |
|---|---|---|
| Cat_12221 | `Cat_12221_diffuse.jpg` | 12221素材の `Cat_diffuse.jpg` |
| Cat_12222 | `Cat_12222_diffuse.jpg` | 12222素材の `Cat_diffuse.jpg` |

（`Cat_bump.jpg` はNormal/Heightとして任意で追加）

## アニメーション一覧（Animator int パラメータ `State`）

| State | クリップ | 内容 | 長さ | ループ |
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
