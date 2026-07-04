import re
from datetime import datetime
from collections import defaultdict

log = open("TestData/AssetPipelineDiagnostics.log", encoding="utf-8", errors="replace").readlines()

def parse_ts(line):
    m = re.match(r"\[(\d{2}:\d{2}:\d{2}\.\d{3})\]", line)
    return datetime.strptime(m.group(1), "%H:%M:%S.%f") if m else None

def count_bundles_between(lines, start_ts, end_ts):
    local = 0
    for line in lines:
        ts = parse_ts(line)
        if not ts or ts < start_ts or ts > end_ts:
            continue
        if "LOCAL CdnMirror" in line and "GetAssetBundle" in line:
            local += 1
    return local

# Find all placement sequences in 19:17+ session
placements = []
i = 0
while i < len(log):
    line = log[i]
    if "[19:17:" not in line and "[19:18:" not in line:
        i += 1
        continue
    if "ObjectMenu.Click" in line and "menuLabel=" in line:
        m = re.search(r"menuLabel='([^']*)'", line, re.DOTALL)
        label = (m.group(1).strip() if m else "?")[:40]
        click_ts = parse_ts(line)
        req_ts = loaded_ts = before_ts = after_ts = None
        mesh_filters = verts_hint = None
        bundle_name = None
        j = i + 1
        while j < len(log) and j < i + 300:
            l = log[j]
            ts = parse_ts(l)
            if ts and click_ts and (ts - click_ts).total_seconds() > 120:
                break
            if "GetPrefab" in l and "requesting prefab" in l:
                req_ts = ts
                bm = re.search(r"AssetBundleName=(\S+)", l)
                bundle_name = bm.group(1) if bm else None
            if "ABM.GetAsset] LOADED" in l and bundle_name and bundle_name in l:
                loaded_ts = ts
            if "prefab before Instantiate" in l:
                before_ts = ts
                mf = re.search(r"MeshFilter=(\d+)", l)
                mesh_filters = int(mf.group(1)) if mf else 0
            if "instance after Instantiate" in l:
                after_ts = ts
                break
            if j > i + 5 and "ObjectMenu.Click" in l and "menuLabel=" in l:
                break
            j += 1
        if req_ts and loaded_ts:
            dep_count = count_bundles_between(log, req_ts, loaded_ts) if loaded_ts else 0
            placements.append({
                "label": label,
                "req": req_ts,
                "loaded": loaded_ts,
                "before": before_ts,
                "after": after_ts,
                "mf": mesh_filters,
                "deps": dep_count,
            })
        i = j if after_ts else i + 1
    else:
        i += 1

print("=" * 90)
print(f"{'Label':<40} {'BundleLoad':>10} {'Instantiate':>11} {'ClickReady':>11} {'Deps':>5} MF")
print("=" * 90)
for p in placements:
    load_ms = (p["loaded"] - p["req"]).total_seconds() * 1000
    inst_ms = 0
    if p["before"] and p["after"]:
        inst_ms = (p["after"] - p["before"]).total_seconds() * 1000
    ready_ms = load_ms
    if p["after"] and p["req"]:
        ready_ms = (p["after"] - p["req"]).total_seconds() * 1000
    elif p["before"] and p["req"]:
        ready_ms = (p["before"] - p["req"]).total_seconds() * 1000
    print(f"{p['label']:<40} {load_ms:9.0f}ms {inst_ms:10.0f}ms {ready_ms:10.0f}ms {p['deps']:5} {p['mf'] or '?'}")

print()
print("Notes:")
print("- BundleLoad = GetPrefab request to ABM.GetAsset LOADED (deps + deserialize)")
print("- Instantiate = Stopwatch around Instantiate() only (excludes snapshot logging)")
print("- DaVinci: 1 MF but huge single mesh; GE Allia: 16 MF, moderate load")
