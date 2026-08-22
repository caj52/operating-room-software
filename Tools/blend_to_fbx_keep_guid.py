"""Export .blend → .fbx next to source, then swap so Unity keeps the same GUID."""
from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

BLENDER = Path(r"C:\Users\conno\AppData\Local\Programs\Blender\blender-4.4.3-windows-x64\blender.exe")
ROOT = Path(__file__).resolve().parents[1]

BLENDS = [
    ROOT / "Assets/Models/CeilingMount_Single/CeilingMount_Single.blend",
    ROOT / "Assets/Models/CeilingMountCovers/BoomHeadCover.blend",
    ROOT / "Assets/Models/CeilingMountCovers/SlimBoomHeadCover.blend",
    ROOT / "Assets/Models/Tandem Cover/Tandem Cover.blend",
    ROOT / "Assets/Models/BoomDropTube/BoomDropTube.blend",
    ROOT / "Assets/Models/BoomSegment_1/BoomSegment_1.blend",
]

EXPORT_PY = r"""
import bpy, sys
from pathlib import Path
out = Path(sys.argv[sys.argv.index('--')+1])
# export selected-all meshes
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(
    filepath=str(out),
    use_selection=False,
    apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z',
    axis_up='Y',
    bake_space_transform=True,
)
print('EXPORTED', out)
"""


def main():
    if not BLENDER.is_file():
        raise SystemExit(f"Blender not found: {BLENDER}")

    script = ROOT / "Tools" / "_export_fbx_tmp.py"
    script.write_text(EXPORT_PY, encoding="utf-8")

    for blend in BLENDS:
        if not blend.is_file():
            print("MISSING", blend)
            continue
        fbx = blend.with_suffix(".fbx")
        meta_blend = Path(str(blend) + ".meta")
        meta_fbx = Path(str(fbx) + ".meta")

        print("Exporting", blend.name)
        subprocess.check_call(
            [str(BLENDER), "--background", str(blend), "--python", str(script), "--", str(fbx)],
            cwd=str(ROOT),
        )
        if not fbx.is_file():
            raise SystemExit(f"FBX not written: {fbx}")

        if not meta_blend.is_file():
            print("  WARN no meta for", blend)
            continue

        # Keep GUID: move meta blend→fbx. Park .blend OUTSIDE Assets so Unity won't import it.
        bak_dir = ROOT / "Tools" / "_blend_bak" / blend.parent.relative_to(ROOT / "Assets")
        bak_dir.mkdir(parents=True, exist_ok=True)
        bak_blend = bak_dir / blend.name
        bak_meta = bak_dir / (blend.name + ".meta")

        if bak_blend.exists():
            bak_blend.unlink()
        if bak_meta.exists():
            bak_meta.unlink()

        shutil.move(str(blend), str(bak_blend))
        if meta_fbx.exists():
            meta_fbx.unlink()
        shutil.move(str(meta_blend), str(meta_fbx))
        shutil.copy2(str(meta_fbx), str(bak_meta))

        print("  OK", fbx.name, "guid preserved; blend parked in", bak_dir)

    script.unlink(missing_ok=True)
    print("DONE — reopen Unity / refresh Assets so FBX imports.")


if __name__ == "__main__":
    main()
