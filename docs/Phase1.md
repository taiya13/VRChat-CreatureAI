# Phase 1 実装ドキュメント

対象クラス: `CreaturePoint` / `CreaturePointRegistry` / `CreaturePointSensor` /
`NeedsController`(空腹・眠気のみ)/ `CreatureCore`
補助: `CreatureProfile`(欲求増加速度の供給元として必須)、`PointType` / `PointOccupancy`
(enum)、`PointTypeMaskAttribute` + `PointTypeMaskDrawer`(Inspector 表示)

Phase 1 のゴール(仕様 10章): **空腹・眠気の 2 欲求だけで最小ループを通す。**
まだ移動・行動(ActionRunner)や意思決定(UtilityDecisionMaker)は実装しない。
「Tick が回り、ポイントが登録・検知され、欲求が時間で増える」ところまでを、
コンソールログで動作確認できる状態にする。

---

## フォルダ構成

```
Assets/CreatureAI/
├─ Runtime/
│  ├─ Core/
│  │  └─ CreatureCore.cs
│  ├─ World/                 … Layer 0(ワールド制作者が触る)
│  │  └─ CreaturePoint.cs
│  ├─ Perception/            … Layer 1(知覚層)
│  │  ├─ CreaturePointRegistry.cs
│  │  └─ CreaturePointSensor.cs
│  ├─ Needs/                 … Layer 2(欲求層)
│  │  ├─ NeedsController.cs
│  │  └─ CreatureProfile.cs
│  └─ Common/                … 共有 enum / 属性
│     ├─ PointType.cs
│     ├─ PointOccupancy.cs
│     └─ PointTypeMaskAttribute.cs
└─ Editor/
   └─ PointTypeMaskDrawer.cs
```

- 仕様 0章・9章の方針どおり **asmdef 分割は行わない**(フォルダ分けのみ)。
  2 種類目の生物が実在してから切り出す。
- `Editor/` フォルダ内のスクリプトは Unity が自動的にエディタ専用アセンブリに入れる
  ため、asmdef なしで `UnityEditor` を参照できる。

---

## 1. CreatureProfile(補助・先に説明)

### 責務
種族パラメータ(欲求増加速度・重み・移動速度・タイムアウト・Animator 参照)を
保持するだけのデータ体。ロジックは持たない。ScriptableObject が UdonSharp から
参照できないため、仕様 3.6節に従い **UdonSharpBehaviour** として実装。

### Inspector 設定
| フィールド | 既定値 | Phase 1 での用途 |
|---|---|---|
| hungerGrowthRate | 1.0 | ✅ 空腹の増加速度(/秒) |
| sleepinessGrowthRate | 0.8 | ✅ 眠気の増加速度(/秒) |
| thirst/playfulness/affection GrowthRate | 1.5 / 0.6 / 0.4 | Phase 2 以降 |
| 各 Weight(5個) | 1.0〜0.5 | Phase 2 以降(意思決定) |
| moveSpeed | 1.5 | Phase 2 以降 |
| reserveTimeout | 10.0 | ✅ 占有タイムアウト掃除に使用 |
| animatorController | (なし) | 後フェーズ |

> Phase 1 で意味を持つのは ✅ の 3 つ。他は仕様どおり最初から定義しておき、
> 後フェーズで Profile を作り直さずに済むようにしている。

### Prefab 構成
Cat Prefab の子オブジェクト **`Profile`** に AddComponent する。

---

## 2. CreaturePoint(Layer 0)

### 責務
ワールド上の「猫が利用できる地点」。種類・評価値・使用可否・検索半径(静的)と、
占有状態 occupancy / holder / reservedAt(ランタイム)を持つ。起動時に稼働中
Registry へ自己登録し、`Reserve → Occupy → Release` で占有を調停する(仕様 3.5節)。

### コードの要点
- `Reserve(holder)`: Free のときだけ成功。**Goal 決定と同時**に呼ぶ想定(予約先取り)。
- `Occupy()`: 到達時に Reserved → Occupied。
- `Release()`: 完了・中断・タイムアウトの**全経路がここを通る**(解放漏れ防止)。
- `IsReservationExpired(timeout)`: Registry の掃除が使う安全網。
- 占有 3 フィールドは Inspector 非公開(仕様どおり)。

> Phase 1 では占有 API は「実装済みだが常時 Free」の状態。実際に Reserve/Occupy を
> 呼ぶのは Phase 2(意思決定・行動)から。ここで API を確定させておくことで、
> 後フェーズが全層に手を入れずに済む(仕様 3.5節の意図)。

### Inspector 設定
| 項目 | 型 | 既定 | 備考 |
|---|---|---|---|
| Point Type | PointType(Flags) | None | 日本語チェックボックスで表示。**未選択だと警告**が出る。 |
| Evaluation Value | float | 1.0 | 魅力度。 |
| Is Usable | bool | true | false で候補除外。 |
| Search Radius | float | 5.0 | **下限 0.5m にクランプ**(`[Min(0.5)]`)。 |
| Config Version | int | 1 | 移行用。通常触らない。 |

### Prefab 構成
餌皿・水飲み場・ベッド等、**ワールド側のオブジェクト**に 1 個ずつ付与する。
Cat Prefab とは独立(ワールド制作者が配置する層)。

---

## 3. CreaturePointRegistry(Layer 1)

### 責務
全 CreaturePoint の登録先。Cat Prefab に子として同梱され、**自己選出**で実質 1 個
だけ稼働する(仕様 3.3節 / 6章)。登録・登録解除・候補検索・タイムアウト掃除を担う。

### コードの要点(自己選出)
```
GameObject.Find("__CatAI_Registry") が返すのは常にヒエラルキー先頭の 1 個。
各 Registry が Find し、「返ってきたのが自分自身」だった 1 個だけ singleton になる。
CreaturePoint / Sensor も同じ Find で同一インスタンスに到達するため参照が食い違わない。
```
- `Register / Unregister`: 内部は可変長配列(`points[]` + `pointCount`)を手動管理
  (UdonSharp は `List<T>` 非対応)。満杯時は倍化。重複登録はスキップ。
- `FindBestCandidate(pos, types)`: 使用不可・占有中・種別不一致・半径外を除外し、
  `評価値 − 距離×0.1` が最大の地点を返す(Phase 2 の意思決定が使う)。
- `CollectNearby(pos, buffer)`: 種別を問わず近傍候補を詰める(Sensor が使う)。
- `CleanupExpiredReservations(timeout)`: 予約タイムアウトを一括解放(安全網)。

### Inspector 設定
公開設定なし(全フィールドがランタイム管理)。GameObject 名が
**`__CatAI_Registry` であることが必須**(Find のキーになる)。

### Prefab 構成
Cat Prefab の子オブジェクト **`__CatAI_Registry`** に AddComponent する。

---

## 4. CreaturePointSensor(Layer 1)

### 責務
猫の周囲の候補地点をキャッシュする。稼働中 Registry への参照を **遅延再取得**で
保持し(仕様 6章)、選出された猫が非アクティブ化しても全猫が沈黙しないようにする。

### コードの要点
- `RefreshIfNeeded()`: `refreshInterval` 秒間隔でだけ再取得。Registry の
  `CollectNearby` で近傍候補を `candidates[]` にキャッシュ。候補数が変化した
  ときだけログ。
- `FindBest(types)`: Registry へ委譲(Phase 2 用)。
- `GetRegistry()`: 参照が null か対象が非アクティブなときだけ `GameObject.Find`。
  正常時はキャッシュを返し毎フレームの Find を避ける。

### Inspector 設定
| 項目 | 既定 | 備考 |
|---|---|---|
| Refresh Interval | 0.5 | 候補再取得の最小間隔(秒) |
| Max Candidates | 16 | キャッシュ上限 |

### Prefab 構成
Cat のルート(CreatureCore と同じ GameObject)に付与。`transform.position` を
猫の現在位置として使う。

---

## 5. NeedsController(Layer 2 / 空腹・眠気のみ)

### 責務
欲求値の管理。Phase 1 は hunger / sleepiness の 2 つ(0-100)。増加速度は Profile 由来。
`GrowNeeds()` は実時間差分で増やすため、Tick 間隔を変えてもペースが変わらない。

### コードの要点
- `Initialize(profile)`: Core から Profile を注入。
- `GrowNeeds()`: `値 += rate × dt` を Clamp(0,100)。閾値をまたいだ瞬間だけログ。
- `SatisfyHunger / SatisfySleepiness`: Phase 2 の行動完了時に使う(充足)。
- `IsHungry / IsSleepy`: 閾値超え判定。

### Inspector 設定
| 項目 | 既定 | 備考 |
|---|---|---|
| Need Threshold | 60 | 「空腹/眠い」とみなす閾値(ログのトリガ) |

### Prefab 構成
Cat のルートに付与(Core と同じ GameObject)。

---

## 6. CreatureCore(まとめ役)

### 責務
兄弟コンポーネントの参照を起動時 1 回だけ収集し、単一タイマーループ
(`OnCoreTick`)で各層を頻度分け実行する(仕様 3.1節 / 4章)。判断・行動ロジックは
持たない。

### コードの要点
- `Start()`: `GetComponent` で needsController / pointSensor を、
  `GetComponentInChildren` で profile / localRegistry を収集 → Needs へ Profile 注入
  → 選出状況をログ → ジッタ付きで Tick 開始。
- `OnCoreTick()`(0.2秒間隔):
  - 毎回: `pointSensor.RefreshIfNeeded()`
  - `% 5`: `needsController.GrowNeeds()`
  - `% 10`: `localRegistry.CleanupExpiredReservations(profile.reserveTimeout)`
  - 末尾で自身を再予約(**1 匹 = タイマー 1 本**)。
- 未実装の層(threat / decision / relationship / actionRunner)は呼び出し位置を
  コメントで明示。各フェーズでそこに 1 行足すだけで済む。

### Inspector 設定
| 項目 | 既定 | 備考 |
|---|---|---|
| Tick Interval | 0.2 | メインループ間隔(秒) |
| Startup Jitter | 0.2 | 起動時ランダム遅延の最大(位相分散) |

### Prefab 構成
Cat のルートに付与。参照フィールドは `[HideInInspector]`(実行時に自動収集)。

---

## Cat Prefab の全体構成(Phase 1)

```
Cat (root)
├─ CreatureCore
├─ NeedsController
├─ CreaturePointSensor
├─ Profile                 (子GameObject)
│   └─ CreatureProfile
└─ __CatAI_Registry        (子GameObject / 名前が重要)
    └─ CreaturePointRegistry
```

ワールド側:
```
FoodBowl   … CreaturePoint (Point Type = 餌)
Bed        … CreaturePoint (Point Type = ベッド)
… など、地点ごとに 1 個
```

---

## 動作確認の手順

1. 上記構成で Cat Prefab を作り、シーンに 1 匹置く。
2. シーンに `CreaturePoint` を数個置く(種類を選ぶ。未選択だと Inspector に警告)。
3. Play(または VRChat ClientSim / Build&Test)する。
4. Console で以下を確認する:
   - `[CreaturePointRegistry] Singleton に選出されました: …`
   - `[CreaturePointRegistry] 登録: FoodBowl (合計 N 個)`(各 Point が自己登録)
   - `[CreaturePointSensor] 近傍候補: M 個`(猫の周囲の Point 数)
   - しばらく待つと `[NeedsController] … 空腹=限界に接近 (hunger=60.x)`
     `… 眠気=限界に接近 …`(欲求が時間で増え、閾値を越えた瞬間に出る)
5. 猫を 2 匹置くと、片方の Registry が
   `待機(別インスタンスが稼働中)` になることを確認(自己選出の動作)。

> Phase 1 ではまだ猫は動かない。「頭脳の配線と計測ができている」ことを
> ログで確認するのがゴール。移動・行動は Phase 2 で載せる。

---

## UdonSharp 実装上の注意(このコードで意識した点)

- `List<T>` 非対応 → Registry / Sensor は配列 + カウンタで手動管理。
- プロパティの取りこぼしを避け、占有状態は getter **メソッド**で公開。
- enum のビット演算は `(int)` キャストしてから行う。
- `SendCustomEventDelayedSeconds(nameof(OnCoreTick), …)` で単一タイマーを自己再予約。
  呼ばれる `OnCoreTick` は `public` 必須。
- 参照キャッシュは `[HideInInspector] public`(実行時収集・Inspector を汚さない)。
- `[Min(0.5f)]`(Unity 標準)で searchRadius の下限クランプを実現。
- `[InspectorName]` は EnumFlagsField に頼らず Editor で自前解決(仕様 5章の指示)。
```
