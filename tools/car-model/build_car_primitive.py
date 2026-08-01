"""Builds the car used by the 3D part picker, and exports it as glTF.

Run:
    blender --background --python tools/car-model/build_car.py

Outputs (both committed, so CI never needs Blender):
    wwwroot/models/car.glb          the model
    wwwroot/models/car-parts.json   the mesh names in it

WHY GENERATE RATHER THAN DOWNLOAD A MODEL
Most car models are welded into a single mesh, or grouped by material (paint / glass / chrome).
Either way you cannot click "left front door" — you click "car". Building it here means every
panel is a separate named object by construction, which is the whole requirement. It also
sidesteps the licensing question entirely for a commercial workshop app, and makes the car
editable source rather than an opaque binary.

NAMING IS A CONTRACT
Every object name must match a member of Models/Part.cs exactly. CarModelManifestTests reads
car-parts.json and fails if the two drift — otherwise renaming an enum member silently produces
a panel that can never be selected, and nobody notices until someone tries to tag that part.

Dimensions are a real sedan: 4.7m long, 1.8m wide, 1.45m tall, wheelbase 2.7m.
"""

import bpy
import bmesh
import json
import os
import sys
from mathutils import Vector

# ---------------------------------------------------------------- scene setup

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


# ---------------------------------------------------------------- helpers

def add_box(name, center, size, bevel=0.02):
    """A named box. Bevelled because sharp CG edges are the main thing that makes a
    procedurally built car look like a stack of crates rather than a vehicle."""
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=center)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = Vector(size) * 0.5 * 2.0  # cube is 1 unit; scale is half-extent * 2
    obj.scale = Vector((size[0], size[1], size[2]))
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 3
        mod.limit_method = "ANGLE"
        mod.angle_limit = 0.6
    return obj


def add_cylinder(name, center, radius, depth, axis="X"):
    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=depth, location=center, vertices=32)
    obj = bpy.context.active_object
    obj.name = name
    if axis == "X":
        obj.rotation_euler[1] = 1.5707963
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return obj


def add_profile_solid(name, profile_xz, width, y_center=0.0, bevel=0.015):
    """Extrudes a 2D side profile (list of (x, z)) sideways into a solid.

    This is what gives the body its car shape — a raked windscreen and a real roofline —
    instead of the boxy silhouette you get from primitives alone."""
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)

    bm = bmesh.new()
    half = width / 2.0
    verts = [bm.verts.new((x, y_center - half, z)) for (x, z) in profile_xz]
    face = bm.faces.new(verts)

    # extrude_face_region + an explicit translate, NOT solidify: solidify pushes along the face
    # normal, whose direction depends on the winding order of the profile points, so a profile
    # authored clockwise silently extrudes the wrong way and leaves the panel detached from the
    # car. Extruding and translating by a known vector is direction-independent.
    ret = bmesh.ops.extrude_face_region(bm, geom=[face])
    new_verts = [e for e in ret["geom"] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=new_verts, vec=(0.0, width, 0.0))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    bm.to_mesh(mesh)
    bm.free()

    if bevel > 0:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
        mod.limit_method = "ANGLE"
        mod.angle_limit = 0.7
    return obj


def shade_smooth(obj):
    for poly in obj.data.polygons:
        poly.use_smooth = True


# ---------------------------------------------------------------- materials

def make_materials():
    def mat(name, rgba, rough, metal=0.0):
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        bsdf = m.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = rgba
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
        return m

    return {
        "paint": mat("Paint", (0.82, 0.81, 0.78, 1.0), 0.45, 0.10),
        "glass": mat("Glass", (0.42, 0.52, 0.58, 1.0), 0.12, 0.30),
        "trim": mat("Trim", (0.18, 0.18, 0.17, 1.0), 0.65),
        "tyre": mat("Tyre", (0.09, 0.09, 0.08, 1.0), 0.95),
        "rim": mat("Rim", (0.72, 0.72, 0.70, 1.0), 0.30, 0.70),
        "lamp": mat("Lamp", (0.95, 0.93, 0.85, 1.0), 0.15),
        "lampred": mat("LampRed", (0.70, 0.16, 0.14, 1.0), 0.25),
    }


def assign(obj, material):
    obj.data.materials.clear()
    obj.data.materials.append(material)


# ---------------------------------------------------------------- the car
# Origin at the centre of the car, +X toward the front, +Y to the left, +Z up.

LENGTH, WIDTH, HEIGHT = 4.70, 1.80, 1.45
HALF_W = WIDTH / 2.0
WHEEL_R = 0.33
AXLE_F, AXLE_R = 1.35, -1.35
SILL_Z = 0.42
BELT_Z = 0.98
ROOF_Z = 1.45


def build(mats):
    parts = {}

    # --- main body: a side profile extruded across the full width -------------
    # More points than strictly needed: each one lets the bevel round a corner, and rounded
    # corners are most of what separates "car" from "stack of crates" in a procedural build.
    body_profile = [
        (2.35, 0.58), (2.36, 0.78), (2.30, 0.90), (2.10, 0.96),
        (1.60, 0.99), (1.05, 1.01), (0.45, 1.02),
        (-1.05, 1.02), (-1.60, 1.00), (-1.95, 0.97), (-2.18, 0.92),
        (-2.32, 0.80), (-2.34, 0.58), (-2.32, 0.42), (2.32, 0.42),
    ]
    lower = add_profile_solid("BodyLower", body_profile, WIDTH * 0.98)
    assign(lower, mats["paint"])
    shade_smooth(lower)
    parts["_bodyLower"] = lower

    # --- greenhouse: narrower than the body, which is the "tumblehome" that stops
    #     a procedural car looking like a slab ---------------------------------
    # Peaks at 1.41, below the roof plate at 1.44 — at 1.45 the glass poked through the roof
    # and rendered as loose shards floating above the car.
    green_profile = [
        (0.98, 1.00), (0.54, 1.40), (-0.70, 1.41), (-1.30, 1.00),
    ]
    green = add_profile_solid("Greenhouse", green_profile, WIDTH * 0.84)
    assign(green, mats["glass"])
    shade_smooth(green)
    parts["_greenhouse"] = green

    # --- panels: thin plates sitting proud of the body. These are the clickable
    #     meshes; the body beneath is scenery. -------------------------------
    t = 0.035  # plate thickness

    def side_panel(name, x_center, x_len, z_center, z_len, left):
        y = (HALF_W * 0.97) if left else -(HALF_W * 0.97)
        o = add_box(name, (x_center, y, z_center), (x_len, t, z_len))
        assign(o, mats["paint"])
        return o

    for left, suffix in ((True, "Left"), (False, "Right")):
        parts["WingFront" + suffix] = side_panel("WingFront" + suffix, 1.62, 0.85, 0.75, 0.52, left)
        parts["DoorFront" + suffix] = side_panel("DoorFront" + suffix, 0.72, 0.90, 0.72, 0.56, left)
        parts["DoorRear" + suffix] = side_panel("DoorRear" + suffix, -0.22, 0.90, 0.72, 0.56, left)
        parts["QuarterRear" + suffix] = side_panel("QuarterRear" + suffix, -1.35, 1.20, 0.75, 0.52, left)
        sill = side_panel("Sill" + suffix, 0.25, 1.90, 0.47, 0.14, left)
        assign(sill, mats["trim"])
        parts["Sill" + suffix] = sill

        y_m = (HALF_W + 0.08) if left else -(HALF_W + 0.08)
        mirror = add_box("Mirror" + suffix, (1.02, y_m, 1.02), (0.16, 0.20, 0.10))
        assign(mirror, mats["trim"])
        parts["Mirror" + suffix] = mirror

    # --- horizontal panels ---------------------------------------------------
    bonnet = add_box("Bonnet", (1.62, 0.0, 1.00), (1.30, WIDTH * 0.88, t))
    assign(bonnet, mats["paint"])
    parts["Bonnet"] = bonnet

    boot = add_box("BootTailgate", (-1.80, 0.0, 1.00), (0.95, WIDTH * 0.88, t))
    assign(boot, mats["paint"])
    parts["BootTailgate"] = boot

    # Sits on top of the greenhouse and spans its full width, so the cabin reads as a closed
    # roof rather than a plate hovering over glass.
    roof = add_box("Roof", (-0.08, 0.0, 1.435), (1.22, WIDTH * 0.845, t * 1.6))
    assign(roof, mats["paint"])
    parts["Roof"] = roof

    # --- glass ---------------------------------------------------------------
    # Centres sit at 1.18, not 1.24. Rotating a box grows its vertical extent by
    # length/2 * sin(angle), which took the earlier 1.24 centres up to z=1.45 — through the
    # roof at 1.407, so the glass rendered as shards floating above the car. These end at
    # ~1.38, just under the roof.
    ws = add_box("Windscreen", (0.80, 0.0, 1.18), (0.56, WIDTH * 0.80, t))
    ws.rotation_euler[1] = -0.72
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    assign(ws, mats["glass"])
    parts["Windscreen"] = ws

    rws = add_box("RearWindscreen", (-1.00, 0.0, 1.18), (0.54, WIDTH * 0.80, t))
    rws.rotation_euler[1] = 0.75
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    assign(rws, mats["glass"])
    parts["RearWindscreen"] = rws

    # --- bumpers and lamps ---------------------------------------------------
    fb = add_box("FrontBumper", (2.30, 0.0, 0.62), (0.14, WIDTH * 0.99, 0.42))
    assign(fb, mats["trim"])
    parts["FrontBumper"] = fb

    rb = add_box("RearBumper", (-2.32, 0.0, 0.62), (0.14, WIDTH * 0.99, 0.42))
    assign(rb, mats["trim"])
    parts["RearBumper"] = rb

    for left, suffix in ((True, "Left"), (False, "Right")):
        y = 0.60 if left else -0.60
        hl = add_box("Headlight" + suffix, (2.28, y, 0.92), (0.10, 0.44, 0.16))
        assign(hl, mats["lamp"])
        parts["Headlight" + suffix] = hl

        tl = add_box("Taillight" + suffix, (-2.30, y, 0.94), (0.10, 0.42, 0.17))
        assign(tl, mats["lampred"])
        parts["Taillight" + suffix] = tl

    # --- wheels --------------------------------------------------------------
    for x, fr in ((AXLE_F, "Front"), (AXLE_R, "Rear")):
        for left, suffix in ((True, "Left"), (False, "Right")):
            y = (HALF_W - 0.10) if left else -(HALF_W - 0.10)
            name = "Wheel" + fr + suffix
            tyre = add_cylinder(name, (x, y, WHEEL_R), WHEEL_R, 0.22)
            assign(tyre, mats["tyre"])
            shade_smooth(tyre)
            parts[name] = tyre

            rim = add_cylinder(name + "_rim", (x, y * 1.02, WHEEL_R), WHEEL_R * 0.55, 0.23)
            assign(rim, mats["rim"])
            shade_smooth(rim)
            parts["_rim" + name] = rim

    # --- parts with no outside surface. Present so every Part value exists in the
    #     model, but placed inside the car where they are not clickable clutter;
    #     the UI offers them as buttons instead. -------------------------------
    hidden = {
        "Interior": (0.0, 0.0, 0.95, (1.9, 1.5, 0.5)),
        "EngineBay": (1.65, 0.0, 0.80, (1.1, 1.4, 0.35)),
        "LoadArea": (-1.80, 0.0, 0.80, (0.85, 1.4, 0.30)),
        "Odometer": (0.62, 0.35, 1.02, (0.22, 0.30, 0.12)),
        "Undercarriage": (0.0, 0.0, 0.30, (3.9, 1.5, 0.06)),
        "Other": (0.0, 0.0, 0.62, (0.12, 0.12, 0.12)),
    }
    for name, (x, y, z, size) in hidden.items():
        o = add_box(name, (x, y, z), size, bevel=0.0)
        assign(o, mats["trim"])
        o.hide_render = True
        parts[name] = o

    return parts


# ---------------------------------------------------------------- export

def main():
    reset_scene()
    mats = make_materials()
    parts = build(mats)

    repo_root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
    out_dir = os.path.join(repo_root, "wwwroot", "models")
    os.makedirs(out_dir, exist_ok=True)

    glb_path = os.path.join(out_dir, "car.glb")
    bpy.ops.export_scene.gltf(
        filepath=glb_path,
        export_format="GLB",
        export_apply=True,          # bake the bevel modifiers
        export_materials="EXPORT",
        export_yup=True,            # glTF is Y-up; three.js expects it
    )

    # Only names that are real Part values go in the manifest. Scenery objects are
    # prefixed with "_" precisely so they are excluded here without a second list to
    # keep in sync.
    part_names = sorted(n for n in parts if not n.startswith("_"))
    with open(os.path.join(out_dir, "car-parts.json"), "w", encoding="utf-8") as f:
        json.dump({"meshes": part_names}, f, indent=2)

    size_kb = os.path.getsize(glb_path) / 1024.0
    print(f"[build_car] exported {glb_path} ({size_kb:.0f} KB), {len(part_names)} named parts")


if __name__ == "__main__":
    main()
