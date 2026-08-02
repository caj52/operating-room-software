from pathlib import Path
import re

text = Path("Assets/Prefabs/Selectables/CeilingMount_Double.prefab").read_text(encoding="utf-8")

print("=== PrefabInstances ===")
for part in text.split("--- !u!1001 &")[1:]:
    pid, _, body = part.partition("\n")
    pid = pid.strip()
    parent = re.search(r"m_TransformParent: \{fileID: ([^}]+)\}", body)
    name = re.search(r"propertyPath: m_Name\n\s+value: ([^\n]+)", body)
    scale_z = re.search(r"propertyPath: m_LocalScale\.z\n\s+value: ([^\n]+)", body)
    zs = re.findall(r"propertyPath: m_LocalPosition\.z\n\s+value: ([^\n]+)", body)
    nm = name.group(1).strip() if name else "-"
    par = parent.group(1) if parent else None
    sz = scale_z.group(1) if scale_z else "-"
    print(f"PI {pid}: name={nm} parent={par} scale.z={sz} pos.zs={zs[:6]}")

print("\n=== Transforms ===")
for m in re.finditer(r"--- !u!4 &(\d+)(?: stripped)?\nTransform:\n([\s\S]*?)(?=\n--- )", text):
    tid = m.group(1)
    body = m.group(2)
    father = re.search(r"m_Father: \{fileID: ([^}]+)\}", body)
    children = re.findall(r"m_Children:\n((?:  - \{fileID: \d+\}\n)+)", body)
    kids = re.findall(r"fileID: (\d+)", children[0]) if children else []
    print(f"T {tid}: father={father.group(1) if father else None} kids={kids}")

# Resolve: which object has scale 0.7
print("\n=== scale.z=0.7 target fileID ===")
for m in re.finditer(
    r"target: \{fileID: (\d+), guid: ([a-f0-9]+).*?\n\s+propertyPath: m_LocalScale\.z\n\s+value: 0\.7",
    text,
    re.S,
):
    print("target", m.group(1), "guid", m.group(2))
