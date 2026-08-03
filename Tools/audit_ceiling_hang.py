"""Audit all ceiling-mount hang APs vs visible mesh hub seats."""
from __future__ import annotations

import math
import re
from pathlib import Path

import ufbx
import UnityPy
from UnityPy import config

config.FALLBACK_UNITY_VERSION = "6000.3.13f1"

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Assets/Prefabs/Selectables"
MIRROR = ROOT / "TestData/CdnMirror"
CATALOG = ROOT / "Assets/SelectableAssetBundles.asset"
TOL_MM = 3.0  # fail if |delta| > this

# Expected hang local Z (root space) from prior mesh-measured hub seats.
# Double_Cover: Tandem Cover inactive → same plate hub as Double.
EXPECTED = {
    "CeilingMount_Double": 0.082,
    "CeilingMount_Single": 0.082,
    "CeilingMount_Double_Cover": 0.082,
    "CeilingMount_Circle": 0.1524,  # BoomHeadCover underside
    "CeilingMount_Circle_Slim": 0.0758,  # SlimBoomHeadCover underside
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


def assets_aps(name: str) -> list[tuple[str, float, float, float]]:
    path = ASSETS / f"{name}.prefab"
    if not path.exists():
        return []
    text = path.read_text(encoding="utf-8")
    out = []
    for m in re.finditer(
        r"value: (AttachPoint[^\n]*)\n([\s\S]{0,1200}?)propertyPath: m_LocalPosition\.z\n\s+value: ([^\n]+)",
        text,
    ):
        chunk = m.group(0)

        def g(a: str) -> float:
            mm = re.search(rf"propertyPath: m_LocalPosition\.{a}\n\s+value: ([^\n]+)", chunk)
            return float(mm.group(1)) if mm else float("nan")

        out.append((m.group(1).strip(), g("x"), g("y"), float(m.group(3))))
    return out


def cdn_aps(bundle: str) -> list[tuple[str, float, float, float]]:
    path = MIRROR / bundle
    if not path.exists():
        return []
    env = UnityPy.load(str(path))
    go = {}
    for o in env.objects:
        if o.type.name == "GameObject":
            try:
                go[o.path_id] = o.read().m_Name
            except Exception:
                pass
    out = []
    for o in env.objects:
        if o.type.name != "Transform":
            continue
        try:
            d = o.read()
        except Exception:
            continue
        gid = d.m_GameObject.path_id if d.m_GameObject else None
        name = go.get(gid, "")
        if not name.startswith("AttachPoint"):
            continue
        lp = d.m_LocalPosition
        out.append((name, float(lp.x), float(lp.y), float(lp.z)))
    return out


def cdn_active(bundle: str, names: set[str]) -> dict[str, bool]:
    path = MIRROR / bundle
    env = UnityPy.load(str(path))
    out = {}
    for o in env.objects:
        if o.type.name != "GameObject":
            continue
        try:
            d = o.read()
        except Exception:
            continue
        if d.m_Name in names:
            out[d.m_Name] = bool(d.m_IsActive)
    return out


def cover_underside(fbx: Path, scale_y_is_thickness: bool = True) -> float:
    sc = ufbx.load_file(str(fbx))
    best = 0.0
    for node in sc.nodes:
        if node.is_root or not node.mesh:
            continue
        t = node.local_transform
        sx, sy, sz = t.scale.x, t.scale.y, t.scale.z
        pts = [(v.x * sx, v.y * sy, v.z * sz) for v in node.mesh.vertices]
        if scale_y_is_thickness:
            # Unity hang Z ≈ FBX scaled Y for these covers
            ys = [p[1] for p in pts]
            # hub: near lateral origin in XZ
            near = [y for x, y, z in pts if x * x + z * z < 0.12**2]
            best = max(best, max(near) if near else max(ys))
        else:
            zs = [p[2] for p in pts]
            best = max(best, max(zs))
    return best


def main():
    bundles = catalog_bundles()
    fails = []
    print("=== Ceiling mount hang AP audit ===\n")

    # Mesh reference seats
    boom = cover_underside(ROOT / "Assets/Models/CeilingMountCovers/BoomHeadCover.fbx")
    slim = cover_underside(ROOT / "Assets/Models/CeilingMountCovers/SlimBoomHeadCover.fbx")
    tandem = cover_underside(ROOT / "Assets/Models/Tandem Cover/Tandem Cover.fbx")
    print(f"Mesh underside (scaled Y): BoomHeadCover={boom:.4f} Slim={slim:.4f} Tandem={tandem:.4f}")
    print(f"EXPECTED table: {EXPECTED}\n")

    for name, exp in EXPECTED.items():
        print(f"--- {name} (expect z={exp}) ---")
        bname = bundles.get(name)
        if bname:
            act = cdn_active(
                bname,
                {
                    "Tandem Cover",
                    "SlimBoomHeadCover",
                    "BoomHeadCover",
                    "CeilingMount_Single",
                    "CeilingMount_Single (1)",
                },
            )
            if act:
                print(f"  CDN active: {act}")

        a_aps = assets_aps(name)
        c_aps = cdn_aps(bname) if bname else []
        if not a_aps:
            print("  FAIL: no Assets APs")
            fails.append(f"{name}: no Assets APs")
        for n, x, y, z in a_aps:
            dmm = (z - exp) * 1000
            ok = abs(dmm) <= TOL_MM
            mark = "OK" if ok else "FAIL"
            print(f"  Assets {n}: xyz=({x:.4f},{y:.4f},{z:.4f}) delta={dmm:+.2f}mm [{mark}]")
            if not ok:
                fails.append(f"{name} Assets {n}: z={z} expect={exp} ({dmm:+.1f}mm)")

        if not c_aps:
            print("  FAIL: no CDN APs")
            fails.append(f"{name}: no CDN APs")
        for n, x, y, z in c_aps:
            dmm = (z - exp) * 1000
            ok = abs(dmm) <= TOL_MM
            mark = "OK" if ok else "FAIL"
            print(f"  CDN    {n}: xyz=({x:.4f},{y:.4f},{z:.4f}) delta={dmm:+.2f}mm [{mark}]")
            if not ok:
                fails.append(f"{name} CDN {n}: z={z} expect={exp} ({dmm:+.1f}mm)")

        # Cross-check Assets vs CDN per AP name
        cmap = {n: z for n, _, _, z in c_aps}
        for n, _, _, z in a_aps:
            if n in cmap and abs(z - cmap[n]) * 1000 > TOL_MM:
                msg = f"{name} {n}: Assets/CDN mismatch {z} vs {cmap[n]}"
                print(f"  FAIL: {msg}")
                fails.append(msg)
        print()

    # Sanity: Circle hang should be near BoomHeadCover underside OR intentional hub protrusion
    print("--- mesh sanity ---")
    print(f"  Circle expect {EXPECTED['CeilingMount_Circle']} vs BoomHeadCover underside {boom:.4f} "
          f"(delta={(EXPECTED['CeilingMount_Circle']-boom)*1000:+.1f}mm)")
    print(f"  Slim expect {EXPECTED['CeilingMount_Circle_Slim']} vs Slim underside {slim:.4f} "
          f"(delta={(EXPECTED['CeilingMount_Circle_Slim']-slim)*1000:+.1f}mm)")
    print(f"  Double_Cover uses plate hub (Tandem Cover inactive); Tandem underside {tandem:.4f} unused")

    print("\n=== VERDICT ===")
    if fails:
        print(f"FAIL ({len(fails)}):")
        for f in fails:
            print(f"  - {f}")
    else:
        print("PASS — all hang APs within ±3mm of expected hub seats; Assets==CDN.")


if __name__ == "__main__":
    main()
