#!/usr/bin/env python3
"""
Unity なしで .meta 生成 + .unitypackage を構築するビルドスクリプト。

- GUID は「アセットの相対パスの md5」で決定的に生成する(再ビルドしても不変)。
  → SDK としてチーム間で GUID を安定させられる。
- .cs には MonoImporter、フォルダには folderAsset の .meta を生成する。
- .meta はリポジトリにも書き出し(Unity プロジェクト構成として直接取り込めるように)、
  同じ内容を .unitypackage にも詰める。
"""
import hashlib, os, io, tarfile, time

REPO = "/home/user/creature_ai_spec3"
ASSETS_ROOT = os.path.join(REPO, "Assets")
OUT_DIR = os.path.join(REPO, "dist")
PKG_NAME = "CreatureAI_Phase1.unitypackage"

def guid_for(rel_path):
    # 前方スラッシュ正規化 → md5 32hex
    return hashlib.md5(rel_path.replace("\\", "/").encode("utf-8")).hexdigest()

def script_meta(guid):
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "MonoImporter:\n"
        "  externalObjects: {}\n"
        "  serializedVersion: 2\n"
        "  defaultReferences: []\n"
        "  executionOrder: 0\n"
        "  icon: {instanceID: 0}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )

def folder_meta(guid):
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "folderAsset: yes\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )

# --- 収集: Assets 配下のフォルダと .cs ---
folder_assets = []  # (rel_path, abs_path)
file_assets = []    # (rel_path, abs_path)

for dirpath, dirnames, filenames in os.walk(ASSETS_ROOT):
    dirnames.sort(); filenames.sort()
    rel_dir = os.path.relpath(dirpath, REPO).replace("\\", "/")
    if rel_dir != "Assets":  # "Assets" 自体は package に含めない(プロジェクトのルート印)
        folder_assets.append((rel_dir, dirpath))
    for fn in filenames:
        if fn.endswith(".meta"):
            continue
        if fn.endswith(".cs"):
            rel = (rel_dir + "/" + fn)
            file_assets.append((rel, os.path.join(dirpath, fn)))

# --- .meta をリポジトリに書き出す ---
def write_meta_repo(asset_abs, content):
    meta_path = asset_abs + ".meta"
    with open(meta_path, "w", newline="\n") as f:
        f.write(content)

meta_map = {}  # rel_path -> (guid, meta_content, is_folder)

for rel, ab in folder_assets:
    g = guid_for(rel)
    m = folder_meta(g)
    write_meta_repo(ab, m)
    meta_map[rel] = (g, m, True)

for rel, ab in file_assets:
    g = guid_for(rel)
    m = script_meta(g)
    write_meta_repo(ab, m)
    meta_map[rel] = (g, m, False)

# --- .unitypackage(gzip tar)を構築 ---
os.makedirs(OUT_DIR, exist_ok=True)
pkg_path = os.path.join(OUT_DIR, PKG_NAME)

def ti(name, data_bytes, mtime):
    t = tarfile.TarInfo(name)
    t.size = len(data_bytes)
    t.mtime = mtime
    t.mode = 0o644
    t.uid = 0; t.gid = 0
    return t

mtime = int(time.time())
count = 0
# Unity の .unitypackage インポータは PAX 形式の tar を展開できないことがあるため、
# 必ず GNU 形式で書き出す(エントリ名・サイズとも GNU の範囲内なので拡張ヘッダは出ない)。
with tarfile.open(pkg_path, "w:gz", format=tarfile.GNU_FORMAT) as tar:
    for rel, (g, m, is_folder) in sorted(meta_map.items()):
        # pathname
        pn = (rel + "\n").encode("utf-8")
        tar.addfile(ti(f"{g}/pathname", pn, mtime), io.BytesIO(pn))
        # asset.meta
        mb = m.encode("utf-8")
        tar.addfile(ti(f"{g}/asset.meta", mb, mtime), io.BytesIO(mb))
        # asset 本体(フォルダには入れない)
        if not is_folder:
            ab = os.path.join(REPO, rel)
            with open(ab, "rb") as f:
                data = f.read()
            tar.addfile(ti(f"{g}/asset", data, mtime), io.BytesIO(data))
        count += 1

print(f"folders={len(folder_assets)} scripts={len(file_assets)} assets_in_pkg={count}")
print(f"package: {pkg_path}")
print(f"size: {os.path.getsize(pkg_path)} bytes")
