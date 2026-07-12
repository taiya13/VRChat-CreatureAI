# Creature AI Framework v1.0 — VRChat 向け 生き物 AI

VRChat SDK3 Worlds + UdonSharp 向けの、猫(生き物)AI フレームワーク。
欲求に基づいて自律的に生活し、危険を避け、性格で個体差が出る猫を、
コードを書かずに配置できる。正式仕様は
[`docs/creature_ai_spec_v3.1.md`](docs/creature_ai_spec_v3.1.md)(v3.1 凍結版)。

## できること(v1.0)

- 欲求(空腹・眠気ほか)が時間で増え、閾値を超えると行動を開始する
- 対応する地点(餌・水・ベッド・爪とぎ等)を探して予約・移動・行動し、欲求を回復する
- Idle 中は起点の周りをうろうろ徘徊する
- 壁や家具(コライダー)を Raycast で検知し、貫通せず自然に回り込む(CreatureLocomotion)
- 暇な時間は Needs と性格に応じて自由行動(毛づくろい/あくび/伸び/見回す/座る/遊ぶ)を行う
- 近くのプレイヤー・餌皿・他の猫へ、行動を中断せず視線(頭)だけを向ける
- (任意)プレイヤーが近づくと逃げる機能。既定は OFF、ThreatEvaluator.fleeEnabled で ON
- 状態に応じてアニメーションを切り替える(Idle/Walk/Eat/Sleep/Flee/Drink/Scratch ほか)
- 性格(臆病さ/好奇心/活発さ/のんびりさ)で行動傾向が変わる
- 複数匹を置いても、地点の占有(予約)で取り合いにならない

## アーキテクチャ(層と責務)

```
CreatureCore ── 参照キャッシュ + 単一 Tick ループ + 割込み指揮
  Needs層     : NeedsData(値) / NeedsController(増減) / CreatureProfile(パラメータ)
  意思決定層  : CreatureBrain(Goal 決定・ヒステリシス)
  対応表      : CreatureActionCatalog(Goal⇔Need⇔PointType⇔Motion⇔表示名 の唯一の定義)
  ターゲット  : CreatureTargetSelector(Goal→地点を予約) / CreaturePointSensor / CreaturePointRegistry
  行動層      : ActionRunner(占有・回復・状態 AgentState・単一中断 AbortCurrent)
  自由時間    : CreatureIdleBehavior(暇なときの所作を Needs+性格で選ぶ)
  身体制御    : MovementController(移動/徘徊/逃走) / CreatureLocomotion(障害物回避) / CreatureAnimator(状態→Animator)
  視線        : CreatureGaze(近くの対象へ頭だけ向ける・行動は中断しない)
  反射・警戒  : ThreatEvaluator(危険検知 / fleeEnabled で逃走 ON・OFF)
  性格        : CreaturePersonality(各層へ倍率を提供)
  ワールド    : CreaturePoint(餌・水・ベッド・爪とぎ等)
  表示(任意) : CreatureStatusDisplay / CreaturePointStatusDisplay / Billboard
```

各層は「判断は Brain、実行は各層、性格は倍率提供」と責務が分かれ、
Goal⇔Need⇔PointType⇔Motion⇔表示名 の対応表は **CreatureActionCatalog** に一本化されている。
新しい Action・モーション・地点・欲求は、この 1 箇所を中心に足すだけで各層が無改造で追従する
(手順は [`docs/v1.1_AssetReady.md`](docs/v1.1_AssetReady.md))。

## 導入とセットアップ

1. UdonSharp 導入済みの VRChat World プロジェクトに `Assets/CreatureAI` を入れる
   (`.unitypackage`(dist/) をインポート、またはフォルダごとコピー)。
2. メニュー **`CreatureAI > 1. Program Asset を作成`**(初回・スクリプト追加時)。
3. メニュー **`CreatureAI > 2. テスト用の猫を作成`**
   (もう一度押すと2匹目・3匹目を位置ずらしで追加。餌・ベッドは共有)。
4. ▶ Play。

### 調整メニュー
- `CreatureAI > ステータス設定`: 欲求ごとの 増加/回復/開始値/重み を表で編集
- `CreatureAI > 性格`: 平均/臆病/元気/のんびり/好奇心旺盛 のプリセット
- `CreatureAI > 0. 状態を確認 (診断)` / `9. 壊れた Program Asset を掃除`

### 主な Inspector パラメータ
- CreatureCore: `tickInterval` / `debugLog`(ログ ON/OFF・公開時 OFF 推奨)
- CreaturePersonality: 4つの性格スライダー(0〜1)
- ThreatEvaluator: `threatDistance`(逃げ出す距離)
- MovementController: 移動/徘徊/逃走の各種数値
- CreaturePoint: 種類 / 評価値 / 使用可否 / 検索半径

## フォルダ

```
Assets/CreatureAI/Runtime/{Core,Needs,Brain,Targeting,Perception,Action,
                            Movement,Animation,Threat,Personality,World,Debug,Common}
Assets/CreatureAI/Editor        … セットアップ・設定ウィンドウ・Gizmo・PropertyDrawer
docs/                            … 各フェーズと v1.0 の解説
```

## ドキュメント
`docs/Phase1〜10` に各機能の詳細、`docs/v1.0_Review.md` に v1.0 の
アーキテクチャ評価・拡張性・不足点、`docs/v1.1_AssetReady.md` に対応表の集約と
「アセット/アニメ/地点/Action の追加手順」、`docs/Phase9_Locomotion.md` に
障害物回避、`docs/Phase10_Life.md` に自由時間・視線・危険回避の一時停止を
まとめてある。

## 差し替え(自分のモデル/アニメ)
- 見た目: Cat 直下の `Body`(仮のカプセル)を消して自分のモデルを置く。
- アニメ: `Assets/CreatureAI_Generated/CreatureAnimator.controller` の各状態の
  Motion を自分のクリップに差し替える(パラメータ `MotionState` 0〜4 で切替)。

## .unitypackage の再生成
`.cs` を追加・変更したら `python3 Tools/build_unitypackage.py`。
GUID はパスの md5 で決定的に決まるため既存参照は壊れない。
