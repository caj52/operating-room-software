"""Resolve remaining unmapped AssetBundle script fileIDs."""
from __future__ import annotations

import re
import subprocess
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ZERO = "0" * 32
BLOCK_RE = re.compile(r"--- !u!114 &\d+\nMonoBehaviour:\n(.*?)(?=\n--- |\Z)", re.S)
SCRIPT_RE = re.compile(r"m_Script: \{fileID: (-?\d+), guid: ([a-f0-9]+)")


def top_keys(body: str) -> tuple[str, ...]:
    keys: list[str] = []
    for line in body.splitlines():
        if not line.startswith("  ") or line.startswith("   ") or line.startswith("  -"):
            continue
        if ":" not in line:
            continue
        key = line.strip().split(":", 1)[0]
        if key.startswith("m_"):
            continue
        keys.append(key)
    return tuple(keys)


def m_fields(body: str) -> tuple[str, ...]:
    """Include m_* custom-looking fields (UI Graphic etc.)."""
    keys: list[str] = []
    for line in body.splitlines():
        if not line.startswith("  ") or line.startswith("   "):
            continue
        if ":" not in line:
            continue
        key = line.strip().split(":", 1)[0]
        if key in (
            "m_ObjectHideFlags",
            "m_CorrespondingSourceObject",
            "m_PrefabInstance",
            "m_PrefabAsset",
            "m_GameObject",
            "m_Enabled",
            "m_EditorHideFlags",
            "m_Script",
            "m_Name",
            "m_EditorClassIdentifier",
        ):
            continue
        keys.append(key)
    return tuple(keys[:40])


def load_meta() -> dict[str, str]:
    out: dict[str, str] = {}
    for base in (ROOT / "Assets", ROOT / "Packages"):
        if not base.exists():
            continue
        for meta in base.rglob("*.cs.meta"):
            t = meta.read_text(encoding="utf-8", errors="replace")
            m = re.search(r"^guid: ([a-f0-9]+)", t, re.M)
            if m:
                out.setdefault(m.group(1), meta.with_suffix("").name)
    return out


def git_show(rel: str) -> str | None:
    r = subprocess.run(["git", "show", f"HEAD:{rel}"], cwd=ROOT, capture_output=True)
    if r.returncode != 0:
        return None
    return r.stdout.decode("utf-8", errors="replace")


def main() -> None:
    meta = load_meta()

    # Manual overrides by distinctive keys / known types
    manual: dict[str, str] = {
        # AttachmentPoint
        "3051475361219219507": "79f4f1f4b77332e4582ec64224c3ac69",
        # ClearanceLinesRenderer
        "-6462157728776104445": "f68425babe0141945b7bb62bfa707421",
        # WallCutter
        "5824319919438786352": "15c3726bdcd96974dad23c97181d8876",
        # PulseDataLineRenderer
        "5369255589366598180": "95be0a7f3ee49f7438177e97c70f28b8",
        # BoomConfigurationManager
        "2889719667090061084": "f2081d293ee97fb4a8a1af2cf45ed9f1",
        # MatchScale
        "-477400162067130098": "086f805cfbf23de4d80dd5e05fd488d9",
    }

    # Discover PulseDataField / similar for dataFieldIndex
    for cs in (ROOT / "Assets").rglob("*.cs"):
        text = cs.read_text(encoding="utf-8", errors="replace")
        meta_path = cs.with_suffix(".cs.meta")
        if not meta_path.exists():
            continue
        guid = re.search(r"^guid: ([a-f0-9]+)", meta_path.read_text(), re.M).group(1)
        if "dataFieldIndex" in text and "multiplier" in text and "decimals" in text:
            print("dataField script:", cs.name, guid)
            manual.setdefault("-3512311433012165083", guid)

    # Empty scripts: compare HEAD BoomSegment empty MonoBehaviours by co-occurrence
    # Also scan all HEAD selectables for empty-key scripts and their guids
    empty_guid_counts: Counter[str] = Counter()
    mfield_to_guid: dict[tuple[str, ...], str] = {}
    key_to_guid: dict[tuple[str, ...], str] = {}

    prefabs = sorted((ROOT / "Assets/Prefabs/Selectables").rglob("*.prefab"))
    for p in prefabs:
        rel = p.relative_to(ROOT).as_posix()
        head = git_show(rel)
        if not head:
            continue
        for body in BLOCK_RE.findall(head):
            sm = SCRIPT_RE.search(body)
            if not sm:
                continue
            guid = sm.group(2)
            if guid == ZERO:
                continue
            tk = top_keys(body)
            mf = m_fields(body)
            if tk:
                key_to_guid.setdefault(tk, guid)
            else:
                empty_guid_counts[guid] += 1
            if mf:
                mfield_to_guid.setdefault(mf, guid)

    print("\nEmpty-key scripts in HEAD (count, guid, name):")
    for g, c in empty_guid_counts.most_common(20):
        print(f"  {c:4d} {g} {meta.get(g, '?')}")

    # For each unmapped current fileID, try m_fields fingerprint
    cur_fid_mf: dict[str, tuple[str, ...]] = {}
    cur_fid_tk: dict[str, tuple[str, ...]] = {}
    cur_counts: Counter[str] = Counter()
    for p in prefabs:
        text = p.read_text(encoding="utf-8", errors="replace")
        for body in BLOCK_RE.findall(text):
            sm = SCRIPT_RE.search(body)
            if not sm or sm.group(2) != ZERO:
                continue
            fid = sm.group(1)
            cur_counts[fid] += 1
            cur_fid_mf[fid] = m_fields(body)
            cur_fid_tk[fid] = top_keys(body)

    print("\nResolve unmapped via m_fields / manual:")
    resolved = dict(manual)
    for fid, c in cur_counts.most_common():
        if fid in resolved:
            continue
        tk = cur_fid_tk[fid]
        mf = cur_fid_mf[fid]
        if tk and tk in key_to_guid:
            resolved[fid] = key_to_guid[tk]
            print(f"  keys {fid} -> {meta.get(resolved[fid], '?')}")
        elif mf and mf in mfield_to_guid:
            resolved[fid] = mfield_to_guid[mf]
            print(f"  mfields {fid} -> {meta.get(resolved[fid], '?')} mf={mf[:12]}")
        else:
            print(f"  STILL {c:4d} fid={fid} tk={tk[:10]} mf={mf[:15]}")

    # Dump a sample body for remaining empties from current
    print("\nSample bodies for stubborn fids:")
    samples_needed = [f for f in cur_counts if f not in resolved]
    got = set()
    for p in prefabs:
        if len(got) >= len(samples_needed):
            break
        text = p.read_text(encoding="utf-8", errors="replace")
        for body in BLOCK_RE.findall(text):
            sm = SCRIPT_RE.search(body)
            if not sm or sm.group(2) != ZERO:
                continue
            fid = sm.group(1)
            if fid not in samples_needed or fid in got:
                continue
            got.add(fid)
            print(f"==== {fid} in {p.name}")
            print(body[:600])
            print()


if __name__ == "__main__":
    main()
