"""Full CDN mirror vs Assets audit for hang/tube length contract."""
from __future__ import annotations

import re
from pathlib import Path

import UnityPy
from UnityPy import config

config.FALLBACK_UNITY_VERSION = "6000.3.13f1"

ROOT = Path(__file__).resolve().parents[1]
MIRROR = ROOT / "TestData" / "CdnMirror"
ASSETS = ROOT / "Assets" / "Prefabs" / "Selectables"
CATALOG = ROOT / "Assets" / "SelectableAssetBundles.asset"
OUT = ROOT / "Logs" / "cdn_attach_audit.txt"

TARGETS = [
    "BoomDropTube",
    "BoomCielingFlange",
    "CeilingMount_Double",
    "CeilingMount_Double_Cover",
    "CeilingMount_Single",
    "CeilingMount_Circle",
    "CeilingMount_Circle_Slim",
    "ArmDropTube",
    "SimFlexTube",
]

PLATE_THICK = 0.122727  # Blender applied AABB
COVER_THICK = 0.1524


def catalog_bundles() -> dict[str, str]:
    text = CATALOG.read_text(encoding="utf-8", errors="ignore")
    out = {}
    for block in re.split(r"- <PrefabName>k__BackingField: ", text)[1:]:
        name = block.split("\n", 1)[0].strip()
        m = re.search(r"<AssetBundleName>k__BackingField: ([^\n]+)", block)
        if m:
            out[name] = m.group(1).strip()
    return out


def assets_ap_locals(prefab: Path) -> list[dict]:
    if not prefab.exists():
        return []
    text = prefab.read_text(encoding="utf-8", errors="ignore")
    root_sz = None
    for m in re.finditer(r"propertyPath: m_LocalScale\.z\n\s+value: ([^\n]+)", text):
        v = float(m.group(1))
        if abs(v - 0.7) < 1e-6:
            root_sz = v
            break
    if root_sz is None:
        root_sz = 1.0
    out = []
    for m in re.finditer(
        r"value: (AttachPoint[^\n]*)\n([\s\S]{0,900}?)propertyPath: m_LocalPosition\.z\n\s+value: ([^\n]+)",
        text,
    ):
        chunk = m.group(0)
        pos = {}
        for a in "xyz":
            mm = re.search(
                rf"propertyPath: m_LocalPosition\.{a}\n\s+value: ([^\n]+)", chunk
            )
            if mm:
                pos[a] = float(mm.group(1))
        out.append({"name": m.group(1).strip(), "pos": pos, "rootScaleZ": root_sz})
    return out


def assets_sizes(prefab: Path) -> list[float]:
    if not prefab.exists():
        return []
    return [
        float(x)
        for x in re.findall(
            r"<Size>k__BackingField: ([0-9.]+)",
            prefab.read_text(encoding="utf-8", errors="ignore"),
        )
        if float(x) > 0
    ]


def load_bundle(path: Path) -> dict:
    env = UnityPy.load(str(path))
    # path_id -> transform data
    transforms = {}
    gameobjects = {}
    monos = []
    meshes = []

    for obj in env.objects:
        typ = obj.type.name
        try:
            data = obj.read()
        except Exception:
            continue

        if typ == "GameObject":
            comps = []
            for c in getattr(data, "m_Component", []) or []:
                try:
                    # UnityPy ComponentPair: .component is PPtr
                    ptr = getattr(c, "component", None) or getattr(c, "component", c)
                    if hasattr(ptr, "path_id"):
                        comps.append(ptr.path_id)
                except Exception:
                    pass
            gameobjects[obj.path_id] = {
                "name": data.m_Name,
                "components": comps,
            }

        if typ == "Transform":
            lp = data.m_LocalPosition
            ls = data.m_LocalScale
            go_id = data.m_GameObject.path_id if data.m_GameObject else None
            father = data.m_Father.path_id if data.m_Father else None
            transforms[obj.path_id] = {
                "go_id": go_id,
                "pos": (float(lp.x), float(lp.y), float(lp.z)),
                "scale": (float(ls.x), float(ls.y), float(ls.z)),
                "father": father,
            }

        if typ == "MonoBehaviour":
            try:
                tree = obj.read_typetree()
            except Exception:
                continue
            monos.append(tree)

        if typ == "Mesh":
            name = getattr(data, "m_Name", "") or ""
            entry = {"name": name}
            try:
                aabb = data.m_LocalAABB
                c, e = aabb.m_Center, aabb.m_Extent
                entry["center"] = (float(c.x), float(c.y), float(c.z))
                entry["extent"] = (float(e.x), float(e.y), float(e.z))
                entry["size"] = (float(e.x) * 2, float(e.y) * 2, float(e.z) * 2)
            except Exception:
                pass
            meshes.append(entry)

    # resolve attach points
    aps = []
    roots = []
    for tid, tr in transforms.items():
        go = gameobjects.get(tr["go_id"], {})
        name = go.get("name", "")
        if "AttachPoint" in name or "AttachmentPoint" in name:
            aps.append({"name": name, **tr})
        if name in (
            "CeilingMount_Double",
            "CeilingMount_Double_Cover",
            "CeilingMount_Single",
            "CeilingMount_Circle",
            "CeilingMount_Circle_Slim",
            "BoomDropTube",
            "BoomCielingFlange",
            "ArmDropTube",
            "SimFlexTube",
        ):
            roots.append({"name": name, **tr})

    # ScaleLevels from typetree
    sizes = []
    for tree in monos:
        # Selectable ScaleLevels
        levels = tree.get("ScaleLevels") or tree.get("<ScaleLevels>k__BackingField")
        if not levels:
            continue
        for lv in levels:
            if not isinstance(lv, dict):
                continue
            sz = lv.get("Size") or lv.get("<Size>k__BackingField")
            if sz is not None and float(sz) > 0:
                sizes.append(float(sz))

    return {
        "aps": aps,
        "roots": roots,
        "sizes": sorted(set(round(s, 6) for s in sizes)),
        "meshes": meshes,
        "go_names": sorted({g["name"] for g in gameobjects.values()}),
    }


def main():
    lines = []

    def log(s=""):
        print(s)
        lines.append(s)

    bundles = catalog_bundles()
    log("CDN MIRROR ATTACH AUDIT")
    log(f"Unity fallback={config.FALLBACK_UNITY_VERSION}")
    log(f"mirror files={sum(1 for _ in MIRROR.iterdir())}")
    log()

    mismatches = []
    for name in TARGETS:
        ab = bundles[name]
        path = MIRROR / ab
        prefab = ASSETS / f"{name}.prefab"
        log(f"=== {name} ({ab}) ===")
        log(f"mirror bytes={path.stat().st_size if path.is_file() else 'MISSING'}")

        a_aps = assets_ap_locals(prefab)
        a_sizes = assets_sizes(prefab)
        log(f"Assets APs: {a_aps}")
        log(f"Assets sizes: {a_sizes}")

        if not path.is_file():
            mismatches.append(f"{name}: mirror missing")
            log()
            continue

        try:
            data = load_bundle(path)
        except Exception as ex:
            mismatches.append(f"{name}: UnityPy load failed: {ex}")
            log(f"  UnityPy FAIL: {ex}")
            log()
            continue

        log(f"CDN GOs: {data['go_names']}")
        log(f"CDN roots: {data['roots']}")
        log(f"CDN APs: {data['aps']}")
        log(f"CDN sizes: {data['sizes']}")
        log(f"CDN meshes: {data['meshes']}")

        # Compare hang AP Z for ceiling mounts
        if name.startswith("CeilingMount"):
            thick = COVER_THICK if "Cover" in name else PLATE_THICK
            # CDN root scale z
            root_sz = 1.0
            for r in data["roots"]:
                if abs(r["scale"][2] - 0.7) < 1e-4:
                    root_sz = r["scale"][2]
                elif name in r["name"] or r["name"].startswith("Ceiling"):
                    root_sz = r["scale"][2]
            # Prefer explicit 0.7 if any transform has it
            for r in data["roots"]:
                if abs(r["scale"][2] - 0.7) < 1e-4:
                    root_sz = 0.7

            # Also scan all transforms via aps' fathers - get scale from root GO
            for r in data["roots"]:
                if "CeilingMount" in r["name"] or r["name"] == name:
                    root_sz = r["scale"][2]

            for ap in data["aps"]:
                hz = ap["pos"][2] * root_sz
                under = thick * root_sz
                dmm = (hz - under) * 1000
                log(
                    f"  CDN hang check {ap['name']}: localZ={ap['pos'][2]:.6f} "
                    f"rootScaleZ={root_sz:.4f} hangDepth={hz:.6f} underside={under:.6f} "
                    f"delta_mm={dmm:.2f}"
                )
                if abs(dmm) > 2.0:
                    mismatches.append(
                        f"{name} {ap['name']}: hang delta {dmm:.2f}mm (CDN)"
                    )

            for ap in a_aps:
                hz = ap["pos"]["z"] * ap["rootScaleZ"]
                under = thick * ap["rootScaleZ"]
                dmm = (hz - under) * 1000
                log(
                    f"  Assets hang check {ap['name']}: localZ={ap['pos']['z']:.6f} "
                    f"rootScaleZ={ap['rootScaleZ']:.4f} hangDepth={hz:.6f} "
                    f"underside={under:.6f} delta_mm={dmm:.2f}"
                )
                if abs(dmm) > 2.0:
                    mismatches.append(
                        f"{name} {ap['name']}: hang delta {dmm:.2f}mm (Assets)"
                    )

            # Assets vs CDN AP z match
            cdn_zs = sorted(ap["pos"][2] for ap in data["aps"])
            asset_zs = sorted(ap["pos"]["z"] for ap in a_aps)
            log(f"  AP z Assets={asset_zs} CDN={cdn_zs}")
            if len(cdn_zs) == len(asset_zs):
                for a, c in zip(asset_zs, cdn_zs):
                    if abs(a - c) > 0.001:
                        mismatches.append(
                            f"{name}: Assets AP z {a} != CDN AP z {c}"
                        )
            elif data["aps"]:
                mismatches.append(
                    f"{name}: AP count Assets={len(asset_zs)} CDN={len(cdn_zs)}"
                )

        if name in ("BoomDropTube", "BoomCielingFlange", "ArmDropTube", "SimFlexTube"):
            # tip AP should be ~mesh length
            tip_zs = [ap["pos"][2] for ap in data["aps"]]
            asset_tips = [ap["pos"]["z"] for ap in a_aps]
            log(f"  tip AP z Assets={asset_tips} CDN={tip_zs}")
            for mesh in data["meshes"]:
                if "size" in mesh:
                    log(
                        f"  mesh {mesh['name']} size={mesh['size']} "
                        f"center={mesh.get('center')}"
                    )
            if data["sizes"] and a_sizes:
                if [round(x, 3) for x in data["sizes"]] != [
                    round(x, 3) for x in sorted(set(a_sizes))
                ]:
                    # allow subset
                    if set(round(x, 3) for x in data["sizes"]) != set(
                        round(x, 3) for x in a_sizes
                    ):
                        mismatches.append(
                            f"{name}: sizes Assets={a_sizes} CDN={data['sizes']}"
                        )
            if tip_zs and asset_tips:
                if abs(tip_zs[0] - asset_tips[0]) > 0.002:
                    mismatches.append(
                        f"{name}: tip AP Assets={asset_tips[0]} CDN={tip_zs[0]}"
                    )
        log()

    # Code state
    sel = (ROOT / "Assets/Scripts/Selectable.cs").read_text(encoding="utf-8", errors="ignore")
    meas = (ROOT / "Assets/Scripts/Measurable.cs").read_text(encoding="utf-8", errors="ignore")
    log("=== CODE STATE ===")
    for n in [
        "EnsureVerticalLengthFlushToHangFace",
        "EnsureCeilingHangPointsOnUnderside",
        "FlushToHang",
        "hangInset",
        "TryBuildVerticalCatalogYTicks",
    ]:
        log(f"  {n}: Sel={sel.count(n)} Meas={meas.count(n)}")

    log()
    log("=== MISMATCHES ===")
    if not mismatches:
        log("NONE — Assets hang APs match Blender underside (±2mm); CDN parsed where available.")
    else:
        for m in mismatches:
            log(f"  FAIL: {m}")

    OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"WROTE {OUT}")


if __name__ == "__main__":
    main()
