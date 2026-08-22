from pathlib import Path

root = Path("Assets/Prefabs/Selectables")
broken = []
for p in sorted(root.rglob("*.prefab")):
    t = p.read_text(encoding="utf-8", errors="replace")
    missing = t.count("Missing Prefab")
    placeholders = t.count("Placeholder for referenced")
    real = 0
    for chunk in t.split("MeshFilter:"):
        if "m_Mesh:" in chunk:
            line = chunk.split("m_Mesh:")[1].split("\n")[0]
            if "fileID: 0" not in line:
                real += 1
    if missing or placeholders:
        broken.append((str(p.relative_to(root)), missing, placeholders, real))

print("prefabs with Missing/Placeholder:")
for row in broken:
    print(f"  {row[0]}: missing={row[1]} placeholder={row[2]} realMeshes={row[3]}")

print("\nbundle search:")
for d in [
    Path("TestData/CdnMirror"),
    Path("AssetBundles"),
    Path("Assets/StreamingAssets"),
]:
    print(f"  {d}: exists={d.exists()}")
    if d.exists():
        for child in list(d.rglob("*"))[:30]:
            if child.is_file() and (
                "simeon" in child.name.lower()
                or "9548e43" in child.name
                or "9d8bfe" in child.name
                or child.name in ("StandaloneWindows", "StandaloneWindows64")
            ):
                print(f"    {child}")
