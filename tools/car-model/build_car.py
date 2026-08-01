"""Builds the car used by the 3D part picker, from a real supplied mesh plus generated hitboxes.

Run:
    blender --background --python tools/car-model/build_car.py

Outputs (both committed, so CI never needs Blender):
    wwwroot/models/car.glb          the model
    wwwroot/models/car-parts.json   the mesh names in it

THE SOURCE MESH
tools/car-model/source/car_mesh.glb is a real sedan mesh (supplied, not authored by this
script) — two meshes, body and glass, with generic node names ("Object_2", "Object_3") and no
per-panel structure at all. That absence is exactly the problem the whole car-picker design is
built around: you cannot click "left front door" on a mesh that has no left front door as a
separate object, only "the car".

The fix is the same one used for the primitive car in build_car_primitive.py (kept for
reference — this script supersedes it as the default): generate 33 invisible boxes, one per
Part enum member, positioned over the visible body and used ONLY for raycasting. The visible
mesh is never touched or clicked directly; picking, highlighting and the coverage map all work
against the boxes. Swapping the visible mesh again later (a better scan, a different car) needs
no code change elsewhere as long as this script is re-run — the hitbox layout is written as
FRACTIONS of the mesh's own bounding box, not hardcoded metres, so it adapts to whatever shape
is supplied.

NAMING IS A CONTRACT
Every hitbox name must match a member of Models/Part.cs exactly. CarModelManifestTests reads
car-parts.json and fails if the two drift.

LICENSING NOTE: car_mesh.glb was supplied directly by the project owner for this purpose. If it
originates from an asset marketplace (its material names — "CarBase1Mtl", "Carglass1Mtl" — and
the "e.obj.cleaner.materialmerger.gles" node name suggest an OBJ-to-glTF conversion pipeline
typical of sites like Sketchfab), check its license permits redistribution inside this
application before shipping it further than internal use.
"""

import bpy
import json
import math
import os
import mathutils

SOURCE_GLB = "car_mesh.glb"
TARGET_LENGTH = 5.0  # arbitrary scene units; only relative proportions matter for the camera


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_and_normalize(repo_root):
    """Loads the source mesh, recentres it on the origin, scales it to TARGET_LENGTH, and
    rotates it so +X is the front — matching the axis convention build_car_primitive.py used,
    so the fractional layout below reads the same way regardless of which script produced the
    model. Determined by rendering the source from multiple angles and looking at it: the
    source's own +Y end has the windscreen, mirrors and a numberplate-shaped bumper recess, so
    +Y is front in its native frame; rotating -90 degrees about Z maps that to +X."""
    path = os.path.join(repo_root, "tools", "car-model", "source", SOURCE_GLB)
    bpy.ops.import_scene.gltf(filepath=path)

    mesh_objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]

    # Unparent (keeping each mesh's current world transform as its new local one) so every
    # later step works with plain matrices and never has to reason about a parent hierarchy.
    # The two failed attempts before this one BOTH came from that hierarchy: first, setting a
    # parent empty's .location before accounting for its .scale shifted by the wrong amount
    # (scale multiplies a child's local coordinates before the parent's translation is added);
    # then, setting that empty's .rotation_euler had NO effect at all, silently, because
    # glTF-imported objects default to rotation_mode='QUATERNION' and .rotation_euler is only
    # read when 'XYZ' mode is active. Composing one matrix and assigning it straight to each
    # mesh's matrix_world sidesteps both traps — no .location/.rotation_euler/.rotation_mode
    # ever gets touched.
    bpy.ops.object.select_all(action="DESELECT")
    for o in mesh_objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = mesh_objs[0]
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.context.view_layer.update()

    all_corners = [o.matrix_world @ mathutils.Vector(c) for o in mesh_objs for c in o.bound_box]
    center = mathutils.Vector((
        sum(p.x for p in all_corners) / len(all_corners),
        sum(p.y for p in all_corners) / len(all_corners),
        sum(p.z for p in all_corners) / len(all_corners),
    ))
    ext = max(
        max(p.x for p in all_corners) - min(p.x for p in all_corners),
        max(p.y for p in all_corners) - min(p.y for p in all_corners),
        max(p.z for p in all_corners) - min(p.z for p in all_corners),
    )
    scale = TARGET_LENGTH / ext

    # Determined by rendering the source from several angles and looking at it (top, both
    # sides, front-on): its own +Y end has the windscreen, mirrors and a numberplate-shaped
    # bumper recess, so +Y is front in its native frame. Rotating -90 degrees about Z maps that
    # onto +X, matching the front-is-+X convention build_car_primitive.py used, so the
    # fractional hitbox layout below reads the same way regardless of which script produced
    # the model.
    rot = mathutils.Matrix.Rotation(-math.pi / 2, 4, "Z")
    scale_m = mathutils.Matrix.Scale(scale, 4)
    recenter = mathutils.Matrix.Translation(-(rot @ scale_m @ center))
    transform = recenter @ rot @ scale_m

    for o in mesh_objs:
        o.matrix_world = transform @ o.matrix_world
    bpy.context.view_layer.update()

    # Bake into the mesh data so every downstream calculation works in plain final coordinates.
    bpy.context.view_layer.objects.active = mesh_objs[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    return mesh_objs


def recolor(mesh_objs):
    """The source materials carry no base color (baseColorFactor is unset, so they render
    flat grey-white) — recoloring is the other half of what was asked for alongside the
    hitboxes. Kept close to the primitive car's palette so the two builds don't look like
    different products if someone compares them."""
    for obj in mesh_objs:
        for slot in obj.material_slots:
            mat = slot.material
            if mat is None or not mat.use_nodes:
                continue
            bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
            if bsdf is None:
                continue
            name = mat.name.lower()
            if "glass" in name:
                bsdf.inputs["Base Color"].default_value = (0.30, 0.40, 0.46, 1.0)
                bsdf.inputs["Roughness"].default_value = 0.12
                if "Alpha" in bsdf.inputs:
                    bsdf.inputs["Alpha"].default_value = 0.55
                    mat.blend_method = "BLEND"
            else:
                # A near-white/grey body technically has colour but doesn't read as coloured
                # against this app's white page background — it just disappears. A clear mid
                # blue is visibly a colour choice rather than "the default", while staying a
                # plausible car paint rather than a garish placeholder.
                bsdf.inputs["Base Color"].default_value = (0.16, 0.35, 0.62, 1.0)
                bsdf.inputs["Roughness"].default_value = 0.35
                bsdf.inputs["Metallic"].default_value = 0.25


def hitbox_material():
    mat = bpy.data.materials.new("Hitbox")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    # Alpha 0: invisible in the rendered view, but raycasting tests geometry, not material, so
    # clicks still land on it. Selection is shown by car3d.js bumping this opacity up at
    # runtime, not by anything baked into the export.
    bsdf.inputs["Base Color"].default_value = (0.09, 0.37, 0.65, 1.0)
    if "Alpha" in bsdf.inputs:
        bsdf.inputs["Alpha"].default_value = 0.0
    mat.blend_method = "BLEND"
    return mat


def add_hitbox(name, xf, yside, ywidth_frac, zf, bbox, mat):
    """xf/zf are (from, to) FRACTIONS of the bbox along that axis (0 = min, 1 = max).
    yside is -1 (right) or +1 (left, matching build_car_primitive.py's convention); ywidth_frac
    is how far the box extends from that side toward the centreline, as a fraction of the half
    width — generously wide on purpose. An organic scanned/modelled surface curves in and out
    of the bounding box in ways a script has no simple analytic handle on, so a slim hitbox
    tuned to look tidy would miss real clicks on the curved surface; a generous one costs
    nothing since these boxes are never seen."""
    xmin, xmax, ymin, ymax, zmin, zmax = bbox
    x0, x1 = xmin + xf[0] * (xmax - xmin), xmin + xf[1] * (xmax - xmin)
    z0, z1 = zmin + zf[0] * (zmax - zmin), zmin + zf[1] * (zmax - zmin)
    half_w = max(ymax, -ymin)
    y_edge = yside * half_w
    y_center = yside * half_w * (1.0 - ywidth_frac)
    y0, y1 = sorted((y_center, y_edge * 1.08))  # 8% past the edge so it isn't clipped by the hull

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = ((x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2)
    obj.data.materials.append(mat)
    return obj


def add_center_hitbox(name, xf, zf, ywidth_frac, bbox, mat):
    """A hitbox spanning the car's centreline (roof, bonnet, boot, bumpers, windscreens) —
    same fractional-X/Z addressing as add_hitbox, but centred in Y rather than pinned to a side."""
    xmin, xmax, ymin, ymax, zmin, zmax = bbox
    x0, x1 = xmin + xf[0] * (xmax - xmin), xmin + xf[1] * (xmax - xmin)
    z0, z1 = zmin + zf[0] * (zmax - zmin), zmin + zf[1] * (zmax - zmin)
    half_w = max(ymax, -ymin) * ywidth_frac
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=((x0 + x1) / 2, 0, (z0 + z1) / 2))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = ((x1 - x0) / 2, half_w, (z1 - z0) / 2)
    obj.data.materials.append(mat)
    return obj


def build_hitboxes(mesh_objs):
    corners = [o.matrix_world @ mathutils.Vector(c) for o in mesh_objs for c in o.bound_box]
    xmin = min(p.x for p in corners); xmax = max(p.x for p in corners)
    ymin = min(p.y for p in corners); ymax = max(p.y for p in corners)
    zmin = min(p.z for p in corners); zmax = max(p.z for p in corners)
    bbox = (xmin, xmax, ymin, ymax, zmin, zmax)

    mat = hitbox_material()
    parts = {}

    # X fractions: 0 = rear, 1 = front (matches the +X-front convention this mesh was rotated
    # into above). Z fractions: 0 = ground, 1 = roofline.
    center_defs = [
        ("RearBumper", (0.00, 0.07), (0.10, 0.42)),
        ("BootTailgate", (0.07, 0.30), (0.42, 0.75)),
        ("RearWindscreen", (0.24, 0.36), (0.55, 0.85)),
        ("Roof", (0.30, 0.62), (0.85, 1.00)),
        ("Windscreen", (0.58, 0.70), (0.55, 0.85)),
        ("Bonnet", (0.66, 0.96), (0.42, 0.75)),
        ("FrontBumper", (0.93, 1.00), (0.10, 0.42)),
    ]
    for name, xf, zf in center_defs:
        parts[name] = add_center_hitbox(name, xf, zf, 0.94, bbox, mat)

    for yside, suffix in ((1, "Left"), (-1, "Right")):
        side_defs = [
            ("QuarterRear" + suffix, (0.06, 0.24), (0.30, 0.78)),
            ("DoorRear" + suffix, (0.24, 0.44), (0.30, 0.78)),
            ("DoorFront" + suffix, (0.44, 0.64), (0.30, 0.78)),
            ("WingFront" + suffix, (0.64, 0.86), (0.30, 0.78)),
            ("Sill" + suffix, (0.22, 0.66), (0.00, 0.14)),
            ("Headlight" + suffix, (0.94, 1.00), (0.35, 0.58)),
            ("Taillight" + suffix, (0.00, 0.06), (0.35, 0.58)),
        ]
        for name, xf, zf in side_defs:
            parts[name] = add_hitbox(name, xf, yside, 0.55, zf, bbox, mat)

        mirror = add_hitbox("Mirror" + suffix, (0.54, 0.62), yside, 1.15, (0.62, 0.78), bbox, mat)
        parts["Mirror" + suffix] = mirror

    # Wheels: rear axle near x-fraction 0.17, front axle near 0.82 — typical sedan proportions;
    # not derivable from the mesh without per-wheel geometry isolation, so approximated and
    # verified by rendering (see docs/LOCAL_DEV.md).
    for axle_frac, fr in ((0.17, "Rear"), (0.82, "Front")):
        for yside, suffix in ((1, "Left"), (-1, "Right")):
            name = "Wheel" + fr + suffix
            parts[name] = add_hitbox(name, (axle_frac - 0.09, axle_frac + 0.09), yside, 0.5,
                                      (0.0, 0.30), bbox, mat)

    # No honest position on the exterior — present so every Part exists, offered as buttons.
    hidden = {
        "Interior": ((0.35, 0.55), (0.30, 0.65)),
        "EngineBay": ((0.66, 0.90), (0.20, 0.55)),
        "LoadArea": ((0.10, 0.28), (0.20, 0.55)),
        "Odometer": ((0.50, 0.58), (0.45, 0.60)),
        "Undercarriage": ((0.15, 0.85), (0.00, 0.06)),
        "Other": ((0.48, 0.52), (0.48, 0.52)),
    }
    for name, (xf, zf) in hidden.items():
        obj = add_center_hitbox(name, xf, zf, 0.5, bbox, mat)
        obj.hide_render = True
        parts[name] = obj

    return parts


def main():
    reset_scene()
    repo_root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))

    mesh_objs = import_and_normalize(repo_root)
    recolor(mesh_objs)
    parts = build_hitboxes(mesh_objs)

    out_dir = os.path.join(repo_root, "wwwroot", "models")
    os.makedirs(out_dir, exist_ok=True)
    glb_path = os.path.join(out_dir, "car.glb")

    bpy.ops.export_scene.gltf(
        filepath=glb_path,
        export_format="GLB",
        export_apply=True,
        export_materials="EXPORT",
        export_yup=True,
    )

    part_names = sorted(parts.keys())
    with open(os.path.join(out_dir, "car-parts.json"), "w", encoding="utf-8") as f:
        json.dump({"meshes": part_names}, f, indent=2)

    size_kb = os.path.getsize(glb_path) / 1024.0
    print(f"[build_car] exported {glb_path} ({size_kb:.0f} KB), {len(part_names)} named parts")


if __name__ == "__main__":
    main()
