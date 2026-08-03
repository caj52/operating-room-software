"""Patch CdnMirror SH_Shelf bundles: length-only scale (X), shared Y/Z=1."""
from __future__ import annotations

from pathlib import Path

import UnityPy
from UnityPy import config

config.FALLBACK_UNITY_VERSION = "6000.3.13f1"

ROOT = Path(__file__).resolve().parents[1]
MIRROR = ROOT / "TestData" / "CdnMirror"

# claim_m / mesh_X_m (LowerShelf / MiddleShelfPull X ≈ 0.840605 m at root scale 1)
SCALE_500 = (0.59481, 1.0, 1.0)
SCALE_750 = (0.89221, 1.0, 1.0)

TARGETS = {
    "gameobject_22c75ba1c3542434eab8e0c5866819f8": SCALE_500,  # SH_Shelf_1_500mm
    "gameobject_475e1e26d7e92a049935f2ee4d414515": SCALE_500,  # SH_Shelf_2_500mm
    "gameobject_53aa01dc8e396914b80e891824927678": SCALE_750,  # SH_Shelf_1
    "gameobject_6578fb191fa8907439062b3a3ddc08dc": SCALE_750,  # SH_Shelf_2
}


def patch_bundle(path: Path, scale: tuple[float, float, float]) -> list[str]:
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

    changed: list[str] = []
    sx, sy, sz = scale
    for obj in env.objects:
        if obj.type.name != "Transform":
            continue
        try:
            data = obj.read()
        except Exception:
            continue
        go_id = data.m_GameObject.path_id if data.m_GameObject else None
        name = go_names.get(go_id, "")
        # Root selectable only — mesh child stays at authored ~0.001 file scale.
        if not name.startswith("SH_Shelf"):
            continue
        old = (float(data.m_LocalScale.x), float(data.m_LocalScale.y), float(data.m_LocalScale.z))
        data.m_LocalScale.x = sx
        data.m_LocalScale.y = sy
        data.m_LocalScale.z = sz
        data.save()
        changed.append(f"{name}: {old} -> {(sx, sy, sz)}")

    if changed:
        path.write_bytes(env.file.save())
    return changed


def main() -> None:
    roots = [
        MIRROR,
        ROOT / "AssetBundles" / "StandaloneWindows64",
        ROOT / "AssetBundles" / "StandaloneOSX",
    ]
    for root in roots:
        for bundle, scale in TARGETS.items():
            path = root / bundle
            if not path.exists():
                print(f"MISSING {root.name}/{bundle}")
                continue
            for line in patch_bundle(path, scale):
                print(f"{root.name}/{bundle}: {line}")


if __name__ == "__main__":
    main()
