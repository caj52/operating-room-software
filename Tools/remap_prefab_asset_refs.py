"""Remap remaining zero-GUID asset refs (Highlight profiles, materials)."""
from __future__ import annotations

import re
import subprocess
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PREFAB_ROOT = ROOT / "Assets/Prefabs/Selectables"
ZERO = "0" * 32
REF_RE = re.compile(
    r"\{fileID: (-?\d+), guid: 00000000000000000000000000000000, type: (\d)\}"
)

# Known AssetBundle fileID → project GUID (+ default fileID for the asset type)
FID_MAP: dict[str, tuple[str, int]] = {
    # HighlightPlus profiles
    "6420316029212642594": ("e33bb565c9ab7f445a0646199c7f0fb3", 11400000),  # SelectableSelected
    "-8655132121257920240": ("2f3438361f210fe4e8213e380c359122", 11400000),  # AttachPointSelected
    # NewBoomHead outlet mats (from HEAD)
    "-4094569761158740963": ("7bec4e898352a774b8711af6af5013d2", 2100000),  # blackMat
    "-3629556551298203753": ("70440ee40a1ccf5489fb8f7ac0a93446", 2100000),  # whiteMat
}


def mat_lists(text: str) -> list[tuple[str, str]]:
    out: list[tuple[str, str]] = []
    for m in re.finditer(r"materials:\n((?:    - \{fileID: [^}]+\}\n)+)", text):
        for mm in re.finditer(r"\{fileID: (-?\d+), guid: ([a-f0-9]+)", m.group(1)):
            out.append((mm.group(1), mm.group(2)))
    return out


def git_show(rel: str) -> str | None:
    r = subprocess.run(["git", "show", f"HEAD:{rel}"], cwd=ROOT, capture_output=True)
    if r.returncode != 0:
        return None
    return r.stdout.decode("utf-8", errors="replace")


def main() -> None:
    fid_map = dict(FID_MAP)

    for p in PREFAB_ROOT.rglob("*.prefab"):
        rel = p.relative_to(ROOT).as_posix()
        head = git_show(rel)
        if not head:
            continue
        cur = p.read_text(encoding="utf-8", errors="replace")
        hmats = mat_lists(head)
        cmats = mat_lists(cur)
        if len(hmats) != len(cmats) or not hmats:
            continue
        for (cf, cg), (_hf, hg) in zip(cmats, hmats):
            if cg == ZERO and hg != ZERO:
                fid_map.setdefault(cf, (hg, 2100000))

    print(f"fid map size: {len(fid_map)}")

    fixed_files = 0
    fixed = 0
    for p in PREFAB_ROOT.rglob("*.prefab"):
        text = p.read_text(encoding="utf-8", errors="replace")
        if ZERO not in text:
            continue

        def repl(m: re.Match) -> str:
            nonlocal fixed
            fid = m.group(1)
            if fid not in fid_map:
                return m.group(0)
            guid, file_id = fid_map[fid]
            fixed += 1
            return f"{{fileID: {file_id}, guid: {guid}, type: 2}}"

        new = REF_RE.sub(repl, text)
        if new != text:
            p.write_text(new, encoding="utf-8")
            fixed_files += 1

    left: Counter[str] = Counter()
    for p in PREFAB_ROOT.rglob("*.prefab"):
        text = p.read_text(encoding="utf-8", errors="replace")
        for m in REF_RE.finditer(text):
            left[m.group(1)] += 1

    print(f"fixed_files={fixed_files} rewrites={fixed}")
    print(f"remaining zero refs: {sum(left.values())} unique={len(left)}")
    for fid, c in left.most_common(10):
        print(f"  {c:4d} {fid}")


if __name__ == "__main__":
    main()
