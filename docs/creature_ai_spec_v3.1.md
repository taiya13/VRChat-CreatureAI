# VRChat向け 猫AIフレームワーク 実装仕様 v3.1(凍結版)
バージョン: v3.1(凍結・実装フェーズ移行) / 対象: VRChat SDK3 Worlds + UdonSharp / 作成日: 2026-07-06

本書はv3の最終レビューを経て確定させた**凍結版正式仕様**である。以降は設計議論を行わず、この内容に沿って実装を進める。

**v3.1での変更点(v3からの差分)**

| # | 変更内容 | 理由 |
|---|---|---|
| ① | `CreaturePoint`の占有状態遷移・同期方針を仕様として明記(2章・新設3.5節・8章) | 後付けすると全層に手が入る最重要箇所のため凍結前に確定 |
| ② | `CreatureProfile`の実装形態をScriptableObjectからUdonSharpBehaviourに変更(3.5節) | UdonSharpは自作SOをランタイム参照不可のため、実装不可能な設計を修正 |
| ③ | Registry自己選出の再取得ロジックを仕様に追記(6章) | 選出Registry側オブジェクトが非アクティブ化された場合の永久沈黙を防止 |
| ④ | `[InspectorName]`とFlagsの組み合わせに関する実装注意点を追記(5章) | PropertyDrawer実装時の抜けを防止 |

---

## 0. v2からの主なトリミング

| v2にあったもの | v3での扱い |
|---|---|
| Action(7個)+ Behavior(5個)の二層構造 | **`ActionRunner`1個に統合**(内部でActionKindをswitch分岐) |
| `CreatureEventBus`(Pub/Sub) | **廃止**。直接メソッド呼び出し/直接参照に戻す |
| `CreatureTickHub`(SDK全体の共有ディスパッチャ) | **廃止**。`CreatureCore`内の単純なタイマーに統合 |
| Core Prefabをランタイム生成(`VRCInstantiate`) | **廃止**。RegistryはCat Prefabに最初から同梱し、自己選出方式に |
| `PointTypeDefinition`/`PointTypeCatalog`(SO) | **廃止**。enumの`[InspectorName]`属性で表示名を管理 |
| NavMeshAgentをデフォルト移動手段に | **格下げ**。v1は簡易ステアリング移動。NavMeshは任意の後付け |
| Core/種族パッケージの明確な分離(asmdef分割・VPM化) | **延期**。フォルダ分けのみ行い、asmdef分割・複数パッケージ配布は犬猫が実在してから |
| `CreatureDebugOverlay`(頭上ワールド空間HUD) | **延期**。まずは状態変化時のコンソールログのみ |

これらは「間違っていた」というより「猫のみ・開発者ほぼ1名という現状に対して過剰だった」という判断によるトリミングである。将来必要になった際に立ち戻れるよう、9章に拡張の考え方だけ残す。

---

## 1. 全体構成(確定版)

```
[ Layer 0 ] ワールドコンテンツ層(ワールド制作者が触る)
   CreaturePoint

        │ 自己登録
        ▼
[ Layer 1 ] 知覚層
   CreaturePointRegistry(Cat Prefabに同梱・自己選出で1つだけ有効になる)
   CreaturePointSensor / PlayerSensor

        │
        ▼
[ Layer 2 ] 欲求層
   NeedsController × CreatureProfile(SO)

        │
        ▼
[ Layer 3 ] 意思決定層          ◀── 割込み ── [ Layer X ] 反射・警戒層
   UtilityDecisionMaker → Goal                  ThreatEvaluator

        │
        ▼
[ Layer 4 ] 行動層
   ActionRunner(Eat/Drink/Sleep/Play/Flee/ApproachPlayer/IdleWanderを内包)

        │
        ▼
[ Layer 5 ] 身体制御層
   CreatureLocomotion(簡易ステアリング移動) / CreatureAnimator

   横断的に参照される層:
   PlayerRelationshipManager(対人関係)
```

---

## 2. コンポーネント一覧(全11個)

| コンポーネント | 役割 |
|---|---|
| `CreaturePoint` | ワールドオブジェクトに付与。種類・評価値・使用可否・検索半径 ＋ 占有状態(ランタイム: occupancy / holder / reservedAt) |
| `CreaturePointRegistry` | 全`CreaturePoint`の自己登録先。Cat Prefabに同梱、自己選出で実質1個だけ稼働(7章) |
| `CreaturePointSensor` | 周囲のポイント候補をキャッシュ |
| `PlayerSensor` | 近傍プレイヤーの検出 |
| `NeedsController` | 欲求値の管理 |
| `CreatureProfile` | 種族パラメータ(欲求の増加速度・重み・移動速度・Animator参照)。**UdonSharpBehaviourとして実装**し、Cat Prefab内の子オブジェクト`Profile`に配置(3.5節参照) |
| `UtilityDecisionMaker` | Goalの決定(意思決定の脳) |
| `ThreatEvaluator` | 危険監視・Fleeへの割込み |
| `ActionRunner` | 全行動の実行(Eat/Drink/Sleep/Play/Flee/ApproachPlayer/IdleWander) |
| `CreatureLocomotion` | 実際の移動 |
| `CreatureAnimator` | Animatorパラメータ制御 |
| `PlayerRelationshipManager` | プレイヤーとの関係(警戒/中立/慣れ/信頼) |
| `CreatureCore` | 兄弟コンポーネントの参照キャッシュ ＋ 起動時一度きりの初期化(3.1節で役割を明確化) |

---

## 3. クラス設計

### 3.1 CreatureCore(役割の明確化)

v1/v2で「ロジックを持たない」としていたが、実際には起動時の初期化が必要なため、役割を以下のように確定する。

```
class CreatureCore : UdonSharpBehaviour
  [役割] 兄弟コンポーネントの参照キャッシュ。起動時に1回だけ実行される初期化(GetComponent収集、
        Registryの自己選出チェック)を行う。以後は判断・行動ロジックを一切持たない。
  [公開参照]
    needsController, pointSensor, playerSensor, decisionMaker,
    threatEvaluator, actionRunner, locomotion, animator, relationshipManager, profile
  [内部]
    Start() で上記を GetComponent し、7章のRegistry選出チェックも実行する
```

### 3.2 ActionRunner(統合版)

```
class ActionRunner : UdonSharpBehaviour
  [内部]
    currentActionKind : ActionKind   // Eat / Drink / Sleep / Play / Flee / ApproachPlayer / IdleWander
    currentGoal       : Goal
    stepState         : int          // 0:移動中 1:到達後の演出中 2:完了処理
  [公開メソッド]
    StartAction(goal) : void    // 実行中の行動がある場合は必ず AbortCurrent() を先に呼ぶ
    AbortCurrent()    : void    // 予約解放(CreaturePoint.Release) + Animatorリセット。Flee割込み・新Goal切替を含む全中断経路はここを通す
    Tick()            : void    // currentActionKind に応じて switch で分岐し、移動→演出→完了 を進める
```

各`ActionKind`の中身(擬似コード)は「移動して」「到達したらAnimatorのStateをセットして」「完了したらNeedsController.Satisfyを呼ぶ」という3ステップの繰り返しであり、`Tick()`内の switch 文で素直に書き下す。将来行動が10種類を大きく超えるなど、switch文が明らかに肥大化した段階で初めて、v2で検討したAction/Behavior分離への切り出しを再検討すればよい(今は不要)。

### 3.3 CreaturePointRegistry(自己選出・同梱版)

```
class CreaturePointRegistry : UdonSharpBehaviour   // Cat Prefabの子オブジェクトとして最初から同梱
  [内部]
    points     : CreaturePoint[]
    pointCount : int
    isActiveSingleton : bool
  [公開メソッド]
    Register(point) / Unregister(point)
    FindBestCandidate(fromPosition, requiredTypes) : CreaturePoint
```

起動時の選出ロジック(7章)により、複数の猫を置いてもこのコンポーネントが実際に機能するのは1インスタンスだけになる。ランタイムでの動的生成(`VRCInstantiate`)は行わない。

### 3.4 その他のクラス

`NeedsController` / `UtilityDecisionMaker` / `ThreatEvaluator` / `CreatureLocomotion` / `CreatureAnimator` / `PlayerRelationshipManager` は v1で示した設計イメージのまま変更なし。

### 3.5 CreaturePoint 占有状態仕様(v3.1新設) ★

**占有は `UtilityDecisionMaker`(候補除外)・`ActionRunner`(取得/解放)・`ThreatEvaluator`(割込み解放)・ネットワーク同期方針の4箇所にまたがるため、ここで一括定義する。**

#### 占有状態の定義

```
enum PointOccupancy { Free, Reserved, Occupied }

CreaturePoint のランタイムフィールド(Inspector非公開)
  occupancy  : PointOccupancy   // 初期値: Free
  holder     : UdonSharpBehaviour(ActionRunnerの参照)  // 初期値: null
  reservedAt : float            // Reserve時の Time.time。タイムアウト管理用
```

#### 状態遷移

```
Free
  → Reserved  : UtilityDecisionMaker が候補を確定した瞬間(Goal決定と同時)
                holder = 自分の ActionRunner、reservedAt = Time.time

Reserved
  → Occupied  : ActionRunner が到達判定した時
  → Free      : タイムアウト(reservedAt から RESERVE_TIMEOUT 秒経過)
                ※ Registry の低頻度チェック(OnCoreTick の tickCounter % 10)で一括掃除する

Occupied
  → Free      : ① ActionRunner.Tick() が行動完了を検知した時
                ② ActionRunner.AbortCurrent() が呼ばれた時(Flee割込み・新Goal切替を含む全中断)

RESERVE_TIMEOUT の推奨初期値: 10秒
  (移動速度と最大検索半径から「最悪到達時間」を超えない値として設定。
   CreatureProfile に定数として持ち、Inspector から調整可能にする)
```

#### 設計上の原則

- **予約は Goal 決定と同時**に行う。到達時初占有では2匹が同一ポイントへ同時移動し始め、後着側が目前でリジェクトされる挙動になるため不可。
- **全ての中断経路は `AbortCurrent()` を通す**。Flee割込みも新Goalへの切替も、`AbortCurrent()` → `StartAction(newGoal)` の順を必ず踏む。これにより解放漏れの経路が構造的に生じない。
- **タイムアウトは必ず実装する**。バグや例外的な経路で Release が呼ばれなかった場合でも、ポイントが永久使用不能になることを防ぐ唯一の安全網である。

#### ネットワーク同期方針(8章と連動)

占有状態は**同期しない**。全猫の Udon Owner を同一クライアント(インスタンスマスター等)に揃えることで、占有判断は常に1クライアント内のローカル状態として完結させる。これにより `[UdonSynced]` と ownership 移譲の複雑な配線が不要になり、8章の Owner 権威型と整合する。**この前提はマルチプレイヤー配線着手前に必ず確認すること。**

#### 実装チェックリスト

- [ ] `CreaturePoint` に `occupancy / holder / reservedAt` フィールドを追加
- [ ] `Reserve(holder) / Occupy() / Release()` の3メソッドを `CreaturePoint` に実装
- [ ] `UtilityDecisionMaker` の候補スコアリングで `occupancy != Free` を除外
- [ ] `ActionRunner.AbortCurrent()` が全中断経路から呼ばれることをコードレビュー時に確認
- [ ] Registry の低頻度チェックにタイムアウト掃除を追加(3行程度)

---

### 3.6 CreatureProfile の実装形態(v3.1変更) ★

**ScriptableObject は UdonSharp のランタイムから参照不可**のため、`CreatureProfile` は `UdonSharpBehaviour` として実装する。

```
class CreatureProfile : UdonSharpBehaviour
  [配置] Cat Prefab の子オブジェクト "Profile" に AddComponent
  [CreatureCore.Start()] profile = GetComponentInChildren<CreatureProfile>() で取得

  [公開フィールド(Inspector設定)]
    // 欲求増加速度(1秒あたり、0-100スケール)
    float hungerGrowthRate    = 1.0
    float thirstGrowthRate    = 1.5
    float sleepinessGrowthRate = 0.8
    float playfulnessGrowthRate = 0.6
    float affectionGrowthRate  = 0.4

    // 意思決定の重み(UtilityDecisionMakerが参照)
    float hungerWeight    = 1.0
    float thirstWeight    = 1.2
    float sleepWeight     = 0.9
    float playWeight      = 0.7
    float affectionWeight = 0.5

    // 移動・行動パラメータ
    float moveSpeed          = 1.5
    float reserveTimeout     = 10.0   // 占有タイムアウト秒数(3.5節参照)

    // Animator参照
    RuntimeAnimatorController animatorController
```

**犬・鹿など他種族への展開**: Cat Prefab の Prefab Variant を作成し、`Profile` 子オブジェクトのフィールド値を上書きする。設計思想はSOと同等のまま、実装形態だけ変わる。asmdef分割は9章の方針通り2種類目が実在してから行う。

---

## 4. 更新処理の流れ(CreatureCore内蔵の単純Tick)

`CreatureTickHub`のようなSDK全体の共有ディスパッチャは廃止し、**`CreatureCore`自身が持つ1本のタイマーループ**で完結させる。

```
CreatureCore.Start()
  → SendCustomEventDelayedSeconds("OnCoreTick", 起動時ランダムオフセット)

CreatureCore.OnCoreTick()   // 例:0.2秒間隔
  tickCounter++
  if (tickCounter % 1 == 0)  pointSensor.RefreshIfNeeded()      // 中頻度
  if (tickCounter % 1 == 0)  threatEvaluator.Check()            // 中頻度
  if (tickCounter % 5 == 0)  needsController.GrowNeeds()        // 低頻度(欲求増加)
  if (tickCounter % 5 == 0)  decisionMaker.Evaluate()           // 低頻度(意思決定)
  if (tickCounter % 10 == 0) relationshipManager.Update()       // より低頻度
  actionRunner.Tick()                                           // 行動実行だけは動き続ける必要がある
  → SendCustomEventDelayedSeconds("OnCoreTick", 0.2秒)
```

意思決定ロジック自体(欲求評価→Goal決定→候補探索→行動)はv1 4.2節のまま。「誰が・どう定期実行するか」を1つのタイマーに集約しただけであり、猫の数が増えても「1匹につき1本のタイマー」で済む(=大量のタイマーが並走しない)。

`CreatureLocomotion`と`CreatureAnimator`のみ、滑らかさが必要なため通常の`Update()`を使う(v1のまま)。

---

## 5. Creature Point仕様(簡略化)

Inspector項目(種類・評価値・使用可否・検索半径)はv1のまま。「種類」の表示名は、`PointTypeDefinition`のようなScriptableObjectを介さず、enum宣言に`[InspectorName("餌")]`のような属性を直接付与して実現する(Unity標準機能で完結し、追加のアセット管理が不要)。

```
[Flags]
enum PointType
  None        = 0
  [InspectorName("餌")]       Food        = 1 << 0
  [InspectorName("水")]       Water       = 1 << 1
  [InspectorName("ベッド")]    Bed         = 1 << 2
  [InspectorName("おもちゃ")]  Toy         = 1 << 3
  [InspectorName("日向")]      Sunny       = 1 << 4
  [InspectorName("高い場所")]  HighPlace   = 1 << 5
  [InspectorName("隠れ場所")]  Hideout     = 1 << 6
  // 将来拡張用の予約(値は変更しない)
  ScratchPost = 1 << 7   // 爪とぎ
  Litterbox   = 1 << 8   // トイレ
  Window      = 1 << 9   // 窓
  Sofa        = 1 << 10  // ソファ
  Fireplace   = 1 << 11  // 暖炉

int configVersion = 1   // 将来タグ体系を変更する際の移行用(21章の教訓を反映)
```

チェックボックスUIは、上記enumを対象にした軽量なカスタムPropertyDrawer(またはカスタムEditor)1つで実現する。Inspector側の警告(種類未選択時のHelpBox表示、検索半径の下限クランプ)は最初から実装する(最低限のフェイルセーフ、v1 6章参照)。

**実装注意点: `[InspectorName]` と `[Flags]` の組み合わせ(v3.1追記)**
Unityの標準 `EnumFlagsField` / `PropertyField` は、`[InspectorName]` の表示名反映がUnityバージョンによって不安定な挙動を示す場合がある。自前の PropertyDrawer を書く場合は `EnumFlagsField` に頼らず、`Enum.GetNames()` でフラグ一覧を取得し、`[InspectorName]` 属性を `FieldInfo` 経由で自前解決することを推奨する。これにより将来のUnityバージョンアップに依存しない安定した表示が得られる。

---

## 6. Bootstrap(Registryの自己選出・簡略版)

```
[Cat Prefabに同梱された CreaturePointRegistry の Start()]
        │
        ▼
  GameObject.Find("__CatAI_Registry") を実行
        │
   ┌────┴─────────────────┐
   │ 見つかったのが自分自身   │ 見つかったのが他のインスタンス
   ▼                       ▼
  自分を isActiveSingleton    自分を isActiveSingleton = false にし、
  = true にして稼働開始       以後は完全に何もしない(登録も検索も無効)
```

- 別インスタンスの猫にある`CreaturePointSensor`は、自分自身の`Registry`ではなく`GameObject.Find("__CatAI_Registry")`で見つかる**稼働中のインスタンス**を参照する。
- ランタイムでの動的生成は行わない(v2で検討した`VRCInstantiate`によるCore Prefab生成は不要と判断した、0章参照)。
- 猫を1匹しか置かないワールドでは、このロジックは実質「常に自分が選ばれる」だけになり、オーバーヘッドはほぼ無い。

**Registry参照の遅延再取得(v3.1追記)**
選出された側の猫オブジェクトがワールドギミック等で非アクティブ化されると、他インスタンスの`CreaturePointSensor`が保持するRegistry参照が死んだままになり、全猫が永久沈黙する。これを防ぐため、`CreaturePointSensor` はRegistryへのアクセス時に**参照がnullまたはGameObjectが非アクティブであれば`GameObject.Find`を再実行する**遅延再取得を実装すること。

```csharp
// CreaturePointSensor 内のヘルパーイメージ
private CreaturePointRegistry GetRegistry() {
    if (cachedRegistry == null || !cachedRegistry.gameObject.activeInHierarchy)
        cachedRegistry = GameObject.Find("__CatAI_Registry")
            ?.GetComponent<CreaturePointRegistry>();
    return cachedRegistry;
}
```

`Find` はコストが高いため、上記のように「正常時はキャッシュを返す」構造にして毎フレーム呼び出しを避けること。

---

## 7. プレイヤーとの関係性システム

v1 8章のまま(Wary → Neutral → Familiar → Trusted の4段階、familiarity 0-100)。ただし実装初期は「脅威シグナル」の判定条件を**距離としきい値以上の接近速度のみ**に絞り、視線判定などの複雑な条件は後回しにする。

---

## 8. ネットワーク同期方針

v1 11章のまま(Owner権威型:意思決定はOwnerクライアントのみで計算し、位置・状態のみ同期)。この方針はクラス設計に直接影響するため、実装着手前に確定させておく。ただし実際の`[UdonSynced]`配線は、まず1クライアントでの動作確認を終えてから着手して構わない。

**占有状態の同期方針(3.5節と連動・v3.1確定)**: 占有状態(`PointOccupancy`)は**同期しない**。全猫のUdon Ownerを同一クライアントに揃え、占有判断をローカル完結とすることで、`[UdonSynced]` と ownership 移譲の複雑な配線を回避する。マルチプレイヤー配線着手時は、この前提が崩れていないことを最初に確認すること。

---

## 9. 将来の拡張(今は実装しないが、設計上ふさぎ込まない点)

- **犬・鹿・鳥への拡張**: `CreatureProfile`を種族ごとに差し替える方針は維持する。ただし現時点でCore/種族パッケージを物理的に分離する作業(asmdef分割等)は行わない。2種類目の生物を作る段階で、必要な部分だけ切り出す。
- **ActionのBehavior分割・イベントバス**: `ActionRunner`のswitch文が肥大化した、または第三者が実際に拡張したいと言ってきた時点で検討する。
- **PointTypeのTier2(完全データ駆動化)**: 猫のみの運用が続く限り不要。
- **複数生物間の相互作用、頭上デバッグHUD、NavMesh移動**: いずれも後回しで問題ない機能として9章に記録するのみ。

---

## 10. 実装ロードマップ(最終版)

| フェーズ | 内容 |
|---|---|
| Phase 1 | `CreaturePoint` / `CreaturePointRegistry`(自己選出込み)/ `CreatureCore`のTickループ。空腹・眠気の2欲求のみで最小ループを通す |
| Phase 2 | 残りの欲求(渇き・遊び・甘え)、`ActionRunner`の全ActionKind実装、占有(Occupied)制御 |
| Phase 3 | `ThreatEvaluator`・`PlayerRelationshipManager`の実装 |
| Phase 4 | Inspector警告・Gizmo等の最低限のフェイルセーフ、状態変化ログ |
| Phase 5(任意) | 個体差・ヒステリシスの磨き込み、NavMesh移動への切替検討 |

---

## 11. まとめ

猫のみを対象に、UdonSharpで無理なく実装・保守できる形まで設計をトリミングした。コンポーネント数は11個、Action層は1コンポーネントに統合、Bootstrapはランタイム生成なしの自己選出方式とした。将来の拡張(犬・鹿・鳥、イベントバス、データ駆動化)は9章に「今は塞がない設計」として記録してあるが、実装には含めない。

v3.1では以下4点を追加確定した: ①`CreaturePoint`占有状態の状態遷移・タイムアウト・同期方針(3.5節)、②`CreatureProfile`のUdonSharpBehaviour化(3.6節)、③Registry参照の遅延再取得(6章)、④PropertyDrawerの`[InspectorName]`自前解決(5章)。

**本書はv3.1をもって凍結版とする。以降は設計議論を行わず、10章のロードマップに従い実装を進める。**
