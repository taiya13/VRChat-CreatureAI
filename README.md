# creature_ai_spec3 — VRChat 向け 猫 AI フレームワーク

VRChat SDK3 Worlds + UdonSharp 向けの、猫 AI フレームワーク実装。
正式仕様は [`docs/creature_ai_spec_v3.1.md`](docs/creature_ai_spec_v3.1.md)(v3.1 凍結版)。

## 実装状況

| フェーズ | 内容 | 状態 |
|---|---|---|
| Phase 1 | `CreaturePoint` / `CreaturePointRegistry`(自己選出)/ `CreaturePointSensor` / `NeedsController`(空腹・眠気)/ `CreatureCore` のTickループ | ✅ 実装済み |
| Phase 2 | 残りの欲求、`ActionRunner` 全行動、占有(Occupied)制御 | 未着手 |
| Phase 3 | `ThreatEvaluator` / `PlayerRelationshipManager` | 未着手 |
| Phase 4 | Inspector 警告・Gizmo 等のフェイルセーフ、状態変化ログ | 一部先行実装 |
| Phase 5 | 個体差・ヒステリシス、NavMesh 移動検討(任意) | 未着手 |

## フォルダ

```
Assets/CreatureAI/Runtime/{Core,World,Perception,Needs,Common}
Assets/CreatureAI/Editor
docs/
```

Phase 1 の詳細(責務・Inspector 設定・Prefab 構成・動作確認手順)は
[`docs/Phase1.md`](docs/Phase1.md) を参照。

## 導入

いずれの方法でも、UdonSharp 導入済みの VRChat World プロジェクトに取り込める。

**A. .unitypackage で入れる(推奨)**
[`dist/CreatureAI_Phase1.unitypackage`](dist/CreatureAI_Phase1.unitypackage) を
プロジェクトにインポート(Assets > Import Package > Custom Package…)する。
GUID は固定生成しているため、再インポートしても参照が壊れない。

**B. フォルダごとコピーで入れる**
`Assets/CreatureAI` を `.meta` ごとプロジェクトの `Assets/` 配下にコピーする。
`.meta` はコミット済みなので GUID はチーム間で一致する。

インポート後、UdonSharp のコンパイルが通ることを確認してから、
`docs/Phase1.md` の手順で Cat Prefab を組む。

### .unitypackage の再生成
`.cs` を追加・変更したら、次で `.meta` 再生成と再パッケージ化ができる。
```
python3 Tools/build_unitypackage.py
```
GUID はパスの md5 で決定的に決まるため、既存アセットの GUID は保持される。

> **Prefab について**: UdonSharp の Prefab は、各スクリプトの
> ProgramAsset がプロジェクトごとにインポート時生成される GUID に依存するため、
> Unity 外で正しい .prefab を手作りすることはできない。現状のパッケージは
> スクリプト一式を提供し、Cat Prefab は `docs/Phase1.md` の手順で組む方式。
> Prefab をパッケージに同梱したい場合は、エディタメニューから Cat Prefab を
> 自動生成するツールを別途用意できる(要相談)。
