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

`Assets/CreatureAI` を、UdonSharp 導入済みの VRChat World プロジェクトの
`Assets/` 配下にコピーする。`.meta` は Unity が初回インポート時に生成する。
