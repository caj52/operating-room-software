"""Patch CdnMirror ceiling-mount hang APs to hub seat (mesh-measured)."""
from __future__ import annotations

import re
from pathlib import Path

import UnityPy
from UnityPy import config

config.FALLBACK_UNITY_VERSION = "6000.3.13f1"

ROOT = Path(__file__).resolve().parents[1]
MIRROR = ROOT / "TestData" / "CdnMirror"
CATALOG = ROOT / "Assets" / "SelectableAssetBundles.asset"

HUB_SEAT_SCALED = 0.082  # Double/Single (mesh scale 0.25)
HUB_SEAT_RAW = 0.265  # Circle (mesh scale 1)

TARGETS = {
    "CeilingMount_Double": HUB_SEAT_SCALED,
    "CeilingMount_Single": HUB_SEAT_SCALED,
    "CeilingMount_Circle": HUB_SEAT_RAW,
    "CeilingMount_Circle_Slim": HUB_SEAT_RAW,
    # Cover keeps authored cover thickness unless hub differs — leave alone for now.
}


def catalog_bundles() -> dict[str, str]:
    text = CATALOG.read_text(encoding="utf-8", errors="ignore")
    out = {}
    for block in re.split(r"- <PrefabName>k__BackingField: ", text)[1:]:
        name = block.split("\n", 1)[0].strip()
        m = re.search(r"<AssetBundleName>k__BackingField: ([^\n]+)", block)
        if m:
            out[name] = m.group(1).strip()
    return out


def patch_bundle(path: Path, hang_z: float) -> list[str]:
    env = UnityPy.load(str(path))
    go_names: dict[int, str] = {}
    for obj in env.objects:
        if obj.type.name != "GameObject":
            continue
        try:
            data = obj.read()
        except Exception:
            continue
        go_names[obj.path_id] = data.m_Name

    changed = []
    for obj in env.objects:
        if obj.type.name != "Transform":
            continue
        try:
            data = obj.read()
        except Exception:
            continue
        go_id = data.m_GameObject.path_id if data.m_GameObject else None
        name = go_names.get(go_id, "")
        if not name.startswith("AttachPoint"):
            continue
        old = float(data.m_LocalPosition.z)
        if abs(old - hang_z) < 1e-6:
            changed.append(f"{name}: already {old:.6f}")
            continue
        data.m_LocalPosition.z = hang_z
        data.save()
        changed.append(f"{name}: {old:.6f} -> {hang_z:.6f}")

    if any("->" in c for c in changed):
        path.write_bytes(env.file.save())
    return changed


def main():
    bundles = catalog_bundles()
    for name, hang_z in TARGETS.items():
        bname = bundles.get(name)
        if not bname:
            print(f"SKIP {name}: not in catalog")
            continue
        path = MIRROR / bname
        if not path.exists():
            print(f"SKIP {name}: missing {path.name}")
            continue
        print(f"=== {name} ({path.name}) hangZ={hang_z} ===")
        for line in patch_bundle(path, hang_z):
            print(" ", line)


if __name__ == "__main__":
    main()
