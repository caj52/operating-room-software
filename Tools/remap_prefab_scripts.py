"""Remap AssetBundle zero-GUID MonoBehaviour scripts on selectable prefabs."""
from __future__ import annotations

import re
import subprocess
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PREFAB_ROOT = ROOT / "Assets/Prefabs/Selectables"

BLOCK_RE = re.compile(r"(--- !u!114 &\d+\nMonoBehaviour:\n)(.*?)(?=\n--- |\Z)", re.S)
SCRIPT_RE = re.compile(r"m_Script: \{fileID: (-?\d+), guid: ([a-f0-9]+), type: \d+\}")
ZERO = "0" * 32

# AssetBundle fileID hash -> MonoScript GUID (fileID 11500000)
MANUAL: dict[str, str] = {
    # Core placeable stack
    "8469785147841022874": "2ccc1c21eb92b2d43903fee16c414d2c",  # Selectable
    "-3303820236459684073": "533802ce717278149876c674f9f556b7",  # Measurable
    "3051475361219219507": "79f4f1f4b77332e4582ec64224c3ac69",  # AttachmentPoint
    "-4120392538539844322": "4f03a8b09e5a0824ba0691c9ff093a08",  # GizmoHandler
    "5453424689975723468": "888380afc233049ce9e618f9f36c8ba8",  # HighlightEffect
    "456658178838955562": "4d13a952dd356a94492b01c9bd024acc",  # UnityEventSender
    "-2417219683783370097": "9d00a4aafe7c6054ca3fe80415597755",  # KeepRelativePosition
    "6122883071229454512": "96ecab8b3abe5c342960b84000c38872",  # TrackedObject
    "-5688678510405291135": "1ef5841ced6c3e34d9b91129db08fec7",  # RemoveTrackedObject
    "-6462157728776104445": "f68425babe0141945b7bb62bfa707421",  # ClearanceLinesRenderer
    "5824319919438786352": "15c3726bdcd96974dad23c97181d8876",  # WallCutter
    "-7106381183227115105": "74177392d521ee142be8ab52906d5c53",  # Door
    # Walls / mats
    "956812599699933629": "e5662d52efa20354b832f9a57294778c",  # MeshInstanceManager
    "-1550010699204651066": "8ebc892f4e4cd49418d88e47fd8f903d",  # MatchHeightToWalls
    "3024235202123035348": "148e656885a46f44ea091d462bcc5eb4",  # MoveToRootOnStart
    "-7529176214363252309": "269211f3d4b866a45859274e0c84f825",  # ObjectMenuIgnore
    "4786653894530862594": "c38b63aaa725930478fd461cf4506ce9",  # ExtraWall
    "4617392488531430109": "6c0af82fbc0767e489021023046ab54c",  # Cuttable
    "-6678476169782941445": "adfab55af70bbd64fa03a19142f44275",  # SetUVToWorld
    "2488845500225340852": "ee7bd3faa4e32a34db16a4c0117799c1",  # MaterialPalette
    "-477400162067130098": "086f805cfbf23de4d80dd5e05fd488d9",  # MatchScale
    "7035999336347276993": "3b8f192b5b0f2cd4d9f8f24d57aab031",  # MatchTransform
    "-6281872239112519943": "7f7341218db4454408a6eeaf67a70a4d",  # IgnoreInverseScaling
    # Boom / arms
    "2889719667090061084": "f2081d293ee97fb4a8a1af2cf45ed9f1",  # BoomConfigurationManager
    "1901629392826871533": "885a83fb755aad741887e3d9bd994d94",  # BoomHeadScaleHandler
    "-165846969436809778": "9b57c75aceaff5d4089bdd94359454c1",  # BoomOutletValidator
    "8938049273912440754": "10746b49712551c478517b88e8db846a",  # BoomHeadBottomPostEnabler
    "2130470172778902416": "ecb1220789f419c49842e0fe46579e83",  # KeepRotation
    "-2030730451438378703": "b9bf7dd551281da499388ffd303038ff",  # TandomRestrictions
    "8777578913783034705": "5b8cd685223618243ae50d83fd282fa1",  # CCDIK
    "7367248881463035319": "0740ded2b828aff428daeb1e9fb7426a",  # GetAttachedObjects
    "-6084736375102819585": "0740ded2b828aff428daeb1e9fb7426a",  # GetAttachedObjects
    "-4300716117595148339": "cb0d9ef43e6b256429b38e2b04f58586",  # ClearanceLineColorToggle
    "-152501608441096867": "d4bfa600169491047becb9b94db72e97",  # LightFactory
    "-3527023516195290542": "9acfc92d8f8c4f14cb8ba2ab265805b0",  # GEAlliaHeightAdjuster
    "6742647238394890708": "a3b69c8953e3f09438454d8679833f63",  # NitrogenRegulatotPositionSetter
    "4359078407876153511": "1fa0514e6a297e648aa40ab165c205aa",  # RestorePositionOnLoad
    # Monitors / vitals
    "8068354591029660335": "f174b250d079eb346a970a3ff90e93f9",  # WallMonitor
    "5369255589366598180": "95be0a7f3ee49f7438177e97c70f28b8",  # PulseDataLineRenderer
    "-3512311433012165083": "PLACEHOLDER_PULSE_NUMBER",  # filled below
    "-7978266203517415465": "5f7201a12d95ffc409449d95f23cf332",  # UI Text
    "-7030229213176759517": "fe87c0e1cc204ed48ad3b37840f39efc",  # UI Image
    # Lights / cameras (URP + project)
    "6867360251947594959": "474bcb49853aa07438625e644c072ee6",  # UniversalAdditionalLightData
    "1206983720893999492": "a79441f348de89743a2939f4d699eac1",  # UniversalAdditionalCameraData
    "-9133348833776305037": "9794f62fb94912c40ace4692ba27cccf",  # InGameLight
}


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
    return tuple(keys[:40])


def git_show(rel: str) -> str | None:
    r = subprocess.run(["git", "show", f"HEAD:{rel}"], cwd=ROOT, capture_output=True)
    if r.returncode != 0:
        return None
    return r.stdout.decode("utf-8", errors="replace")


def fill_pulse_number_guid() -> None:
    meta = ROOT / "Assets/PulsePhysiologyEngine/Scripts/PulseDataNumberRenderer.cs.meta"
    guid = re.search(r"^guid: ([a-f0-9]+)", meta.read_text(), re.M).group(1)
    MANUAL["-3512311433012165083"] = guid


def main() -> None:
    fill_pulse_number_guid()

    # Also merge fingerprint map from HEAD for anything MANUAL missed
    fp_to_guid: dict[tuple[str, ...], str] = {}
    prefabs = sorted(PREFAB_ROOT.rglob("*.prefab"))
    for p in prefabs:
        rel = p.relative_to(ROOT).as_posix()
        head = git_show(rel)
        if not head:
            continue
        for m in BLOCK_RE.finditer(head):
            body = m.group(2)
            sm = SCRIPT_RE.search(body)
            if not sm or sm.group(2) == ZERO:
                continue
            fp = top_keys(body)
            if fp:
                fp_to_guid.setdefault(fp, sm.group(2))

    fid_to_guid = dict(MANUAL)
    unmapped: Counter[str] = Counter()
    for p in prefabs:
        text = p.read_text(encoding="utf-8", errors="replace")
        for m in BLOCK_RE.finditer(text):
            body = m.group(2)
            sm = SCRIPT_RE.search(body)
            if not sm or sm.group(2) != ZERO:
                continue
            fid = sm.group(1)
            if fid in fid_to_guid:
                continue
            fp = top_keys(body)
            if fp in fp_to_guid:
                fid_to_guid[fid] = fp_to_guid[fp]
            else:
                unmapped[fid] += 1

    print(f"mapped fileIDs: {len(fid_to_guid)}")
    if unmapped:
        print("UNMAPPED:")
        for fid, c in unmapped.most_common():
            print(f"  {c:4d} {fid}")
        print("Aborting.")
        return

    fixed_files = 0
    fixed_refs = 0
    for p in prefabs:
        text = p.read_text(encoding="utf-8", errors="replace")
        if ZERO not in text:
            continue

        def repl(m: re.Match) -> str:
            nonlocal fixed_refs
            fid, guid = m.group(1), m.group(2)
            if guid != ZERO or fid not in fid_to_guid:
                return m.group(0)
            fixed_refs += 1
            return f"m_Script: {{fileID: 11500000, guid: {fid_to_guid[fid]}, type: 3}}"

        new_text = SCRIPT_RE.sub(repl, text)
        if new_text != text:
            p.write_text(new_text, encoding="utf-8")
            fixed_files += 1

    left = 0
    for p in prefabs:
        for m in BLOCK_RE.finditer(p.read_text(encoding="utf-8", errors="replace")):
            sm = SCRIPT_RE.search(m.group(2))
            if sm and sm.group(2) == ZERO:
                left += 1

    print(f"fixed_files={fixed_files} rewrites={fixed_refs} remaining_zero_scripts={left}")

    # Spot-check BoomSegment Selectable
    sample = (PREFAB_ROOT / "BoomSegment_1.prefab").read_text(encoding="utf-8")
    if "guid: 2ccc1c21eb92b2d43903fee16c414d2c" in sample:
        print("BoomSegment_1: Selectable GUID OK")
    else:
        print("BoomSegment_1: Selectable GUID MISSING")


if __name__ == "__main__":
    main()
