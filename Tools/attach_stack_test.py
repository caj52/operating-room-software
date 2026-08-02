"""Unity-faithful attach stack measurement (Blender + prefab numbers)."""
from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy
from mathutils import Euler, Vector

REPO = Path(sys.argv[sys.argv.index("--") + 1]) if "--" in sys.argv else Path(".").resolve()
OUT = REPO / "Logs" / "attach_stack"
REPORT = REPO / "Logs" / "attach_stack_report.txt"
OUT.mkdir(parents=True, exist_ok=True)

PLATE = REPO / "Assets/Models/CeilingMount_Single/CeilingMount_Single.blend"
TUBE = REPO / "Assets/Models/BoomDropTube/BoomDropTube.blend"

# From CeilingMount_Double.prefab / BoomDropTube.prefab
PLATE_SCALE_Z = 0.7
HANG_LOCAL = Vector((0.1524, 0.0, 0.122))
TIP_AP_Z = 0.6754
SIZES = [0.1, 0.3, 0.5]


def clear():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for col in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for b in list(col):
            col.remove(b)


def append(blend: Path, prefix: str):
    with bpy.data.libraries.load(str(blend), link=False) as (src, dst):
        dst.objects = list(src.objects)
    out = []
    for o in dst.objects:
        if o is None:
            continue
        bpy.context.collection.objects.link(o)
        o.name = f"{prefix}_{o.name}"
        out.append(o)
    return out


def apply_xf(objs):
    for o in objs:
        if o.type != "MESH":
            continue
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        o.select_set(False)


def aabb(objs):
    mins = Vector((1e9, 1e9, 1e9))
    maxs = Vector((-1e9, -1e9, -1e9))
    n = 0
    for o in objs:
        if o.type != "MESH":
            continue
        n += 1
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            for i in range(3):
                mins[i] = min(mins[i], w[i])
                maxs[i] = max(maxs[i], w[i])
    if not n:
        raise RuntimeError("no mesh")
    return mins, maxs


def empty(name, parent=None, local=None, world=None):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.04
    bpy.context.collection.objects.link(e)
    if parent is not None:
        e.parent = parent
        e.location = local if local is not None else Vector((0, 0, 0))
    elif world is not None:
        e.location = world
    return e


def render_side(center, extent, path: Path):
    cam_d = bpy.data.cameras.new("cam")
    cam_d.type = "ORTHO"
    cam_d.ortho_scale = max(extent.y, extent.z, 0.25) * 1.4
    cam = bpy.data.objects.new("cam", cam_d)
    bpy.context.collection.objects.link(cam)
    cam.location = center + Vector((max(extent.x, 0.4) * 3.0, 0, 0))
    cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    bpy.context.collection.objects.link(sun)
    sun.location = center + Vector((2, 2, 3))
    sc = bpy.context.scene
    sc.camera = cam
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    sc.render.filepath = str(path)
    sc.render.image_settings.file_format = "PNG"
    bpy.ops.render.render(write_still=True)


def main():
    lines = []

    def log(s=""):
        print(s)
        lines.append(s)

    clear()
    plate = append(PLATE, "P")
    apply_xf(plate)
    pmin, pmax = aabb(plate)
    thick = (pmax - pmin).z
    log(f"PLATE thickness (applied)={thick:.6f}m AABB_z=[{pmin.z:.6f},{pmax.z:.6f}]")

    clear()
    tube = append(TUBE, "T")
    # Identity authored length = tip AP / shaft mesh (Unity Size owner is .001)
    shaft = [o for o in tube if o.type == "MESH" and o.name.endswith("BoomDropTube.001")]
    flange = [o for o in tube if o.type == "MESH" and o.name.endswith("BoomDropTube") and not o.name.endswith(".001")]
    smin, smax = aabb(shaft if shaft else tube)
    shaft_len = smax.z - smin.z
    log(f"SHAFT .001 worldAABB_z=[{smin.z:.6f},{smax.z:.6f}] len={shaft_len:.6f}")
    for o in tube:
        if o.type == "MESH":
            log(f"  {o.name} loc.z={o.location.z:.6f} scale={tuple(round(x,4) for x in o.scale)}")
    authored = shaft_len if shaft_len > 0.05 else TIP_AP_Z
    log(f"authored_len (ScaleZ denom)={authored:.6f} tipAP={TIP_AP_Z}")
    log(f"UNITY hang local z={HANG_LOCAL.z} plate scale.z={PLATE_SCALE_Z}")
    log(f"UNITY hang world depth if parented under scaled plate={HANG_LOCAL.z * PLATE_SCALE_Z:.6f}")
    log(f"UNITY underside depth if plate thick*{PLATE_SCALE_Z}={thick * PLATE_SCALE_Z:.6f}")
    log(f"DELTA hang-underside (Unity math)={HANG_LOCAL.z * PLATE_SCALE_Z - thick * PLATE_SCALE_Z:.6f}")
    log()

    for size in SIZES:
        clear()
        plate = append(PLATE, "P")
        apply_xf(plate)
        root = empty("PlateRoot")
        for o in plate:
            o.parent = root
            # keep current world (identity)
            o.matrix_parent_inverse = root.matrix_world.inverted()

        # Plate after apply: z in [-thick,0]. Flip so +Z into room, ceiling at 0, underside +thick
        root.rotation_euler = Euler((math.radians(180), 0, 0), "XYZ")
        bpy.context.view_layer.update()
        pmin, pmax = aabb(plate)
        root.location.z -= pmin.z
        bpy.context.view_layer.update()
        root.scale = Vector((1, 1, PLATE_SCALE_Z))
        bpy.context.view_layer.update()
        pmin, pmax = aabb(plate)
        underside = pmax.z
        ceiling = pmin.z

        # Unity: AP localPosition under scaled parent
        hang = empty("Hang", parent=root, local=HANG_LOCAL.copy())
        bpy.context.view_layer.update()
        hang_w = hang.matrix_world.translation.copy()

        tube = append(TUBE, "T")
        # Size owner only (.001) — shell disabled in Unity
        for o in list(tube):
            if o.type == "MESH" and not o.name.endswith("BoomDropTube.001"):
                o.hide_render = True
                o.hide_viewport = True

        tube_root = empty("TubeRoot")
        for o in tube:
            o.parent = tube_root
            o.matrix_parent_inverse = tube_root.matrix_world.inverted()

        # Zero shaft object offset into tube_root so proximal face is at tube origin
        # (report BOTH with blend offset and with offset zeroed)
        scale_z = size / authored
        tube_root.scale = Vector((1, 1, scale_z))
        tube_root.location = hang_w
        bpy.context.view_layer.update()

        visible = [o for o in tube if o.type == "MESH" and not o.hide_viewport]
        tmin, tmax = aabb(visible)
        log(f"=== Size={size}m ({int(size*1000)}mm) ScaleZ={scale_z:.6f} ===")
        log(f"  plate ceiling={ceiling:.6f} underside={underside:.6f} thick={underside-ceiling:.6f}")
        log(f"  hang_world.z={hang_w.z:.6f}  hang-underside={hang_w.z-underside:.6f} (0=AP on underside)")
        log(f"  shaft AABB z=[{tmin.z:.6f},{tmax.z:.6f}] len={tmax.z-tmin.z:.6f}")
        log(f"  mesh_len - Size = {(tmax.z-tmin.z)-size:.6f}")
        log(f"  shaft_min - hang = {tmin.z-hang_w.z:.6f} (0=mesh starts at AP)")
        log(f"  into_plate = {max(0.0, underside-tmin.z):.6f} m  (mesh above underside toward ceiling)")
        log(f"  free_below_plate = {max(0.0, tmax.z-underside):.6f} m")

        # Second measurement: clear .001 object local z offset (proximal flush at origin)
        for o in tube:
            if o.name.endswith("BoomDropTube.001"):
                o.location.z = 0.0
        bpy.context.view_layer.update()
        tmin2, tmax2 = aabb(visible)
        log(f"  AFTER zeroing .001 loc.z: AABB z=[{tmin2.z:.6f},{tmax2.z:.6f}] "
            f"min-hang={tmin2.z-hang_w.z:.6f} into_plate={max(0.0, underside-tmin2.z):.6f}")

        amin, amax = aabb([o for o in plate + visible if o.type == "MESH"])
        try:
            render_side((amin + amax) * 0.5, amax - amin, OUT / f"stack_{int(size*1000)}mm.png")
            log(f"  png={OUT / f'stack_{int(size*1000)}mm.png'}")
        except Exception as ex:
            log(f"  render err: {ex}")
        log()

    REPORT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"WROTE {REPORT}")


if __name__ == "__main__":
    main()
