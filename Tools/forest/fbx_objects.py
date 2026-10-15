#!/usr/bin/env python3
"""Measure the mesh objects of a binary FBX file and compare them with the forest asset manifest.

usage: python3 -I fbx_objects.py <file.fbx> <manifest.json> <asset id> [--write]

The forest kit is built from sizes recorded in the manifest (bounds, triangle counts, triangles per
material). For five models those numbers were first taken from the glTF, and the FBX is not always
the glTF: two rock faces turned out 1.4 and 1.8 times larger. This tool reads the FBX that was
actually downloaded, before Unity sees it, and says whether the manifest describes it.

Exit status
  0  every object matches the manifest. If the entry said objectsSource "gltf" it now says "fbx".
     With --write: the measured values were stored and the flag set.
  1  the file could not be measured (unreadable, not a binary FBX, a transform this tool does not
     understand, unknown asset id). Nothing was changed.
  2  the file was measured and differs from the manifest. Nothing was changed. Look at the printed
     values, check that the layout still holds with them, then run again with --write.

What is compared, per object: the triangle count, the triangles per material, and the bounding box
within 2 % of the object's largest dimension (at least 1 cm).

Bounds are in the manifest's "source-rh-yup" space: metres, right-handed, Y up, relative to the
object's pivot, with the node's rotation and scale applied and its translation ignored. Poly Haven's
Blender exports give every mesh node Lcl Rotation (-90, 0, 0), which turns a stored point (x, y, z)
into (x, z, -y). Any other rotation, a parent node that rotates or scales, a pivot or a geometric
transform is reported as an error instead of being guessed.

--write also fills the two fields the build does not rely on, from the FBX instead of the glTF:
"vertices" becomes the number of control points the material's polygons use (the glTF number
counted vertices split at UV and normal seams, so it was larger), and "hasUv0" becomes true only
when the first UV layer spans more than 0.01 on both axes over that material's polygons.

Python standard library only. The FBX is treated as data: nothing in it is executed.
"""

import argparse
import array
import itertools
import json
import os
import struct
import sys
import zlib

EXIT_MATCH = 0
EXIT_ERROR = 1
EXIT_MISMATCH = 2

MAGIC = b"Kaydara FBX Binary  \x00"
HEADER_LENGTH = 27

# FBX array property type -> (array module type code, bytes per item).
ARRAY_TYPES = {"f": ("f", 4), "d": ("d", 8), "l": ("q", 8), "i": ("i", 4), "b": ("b", 1)}

BOUNDS_RELATIVE_TOLERANCE = 0.02
BOUNDS_MINIMUM_TOLERANCE = 0.01
ROTATION_TOLERANCE_DEGREES = 0.001
USABLE_UV_SPAN = 0.01
EXPECTED_ROTATION = (-90.0, 0.0, 0.0)

# Transform properties that would move or turn the geometry in ways this tool does not model.
# Each must be absent or hold its neutral value.
NEUTRAL_TRANSFORMS = {
    "PreRotation": 0.0, "PostRotation": 0.0, "RotationOffset": 0.0, "RotationPivot": 0.0,
    "ScalingOffset": 0.0, "ScalingPivot": 0.0, "GeometricTranslation": 0.0,
    "GeometricRotation": 0.0, "GeometricScaling": 1.0,
}

# The axis system every measured Poly Haven file declares: right-handed, Y up.
EXPECTED_AXES = {
    "UpAxis": 1, "UpAxisSign": 1, "FrontAxis": 2, "FrontAxisSign": 1, "CoordAxis": 0, "CoordAxisSign": 1,
}


class FbxError(Exception):
    """The file cannot be measured. Reported with exit status 1."""


class ArrayRef:
    """An array property left where it is in the file until somebody needs it."""

    __slots__ = ("kind", "count", "encoding", "offset", "length")

    def __init__(self, kind, count, encoding, offset, length):
        self.kind = kind
        self.count = count
        self.encoding = encoding
        self.offset = offset
        self.length = length


class Node:
    __slots__ = ("name", "props", "children")

    def __init__(self, name, props, children):
        self.name = name
        self.props = props
        self.children = children

    def first(self, name):
        for child in self.children:
            if child.name == name:
                return child
        return None

    def all(self, name):
        return [child for child in self.children if child.name == name]


class FbxFile:
    """The node tree of a binary FBX. Arrays are decoded on demand, one at a time."""

    def __init__(self, path):
        try:
            with open(path, "rb") as handle:
                self.data = handle.read()
        except OSError as error:
            raise FbxError("cannot read %s: %s" % (path, error))
        if len(self.data) < HEADER_LENGTH or self.data[:len(MAGIC)] != MAGIC:
            raise FbxError("%s is not a binary FBX file" % path)
        self.view = memoryview(self.data)
        self.version = struct.unpack_from("<I", self.data, 23)[0]
        # Offsets are 32-bit below version 7500 and 64-bit from 7500 on.
        self.record = struct.Struct("<QQQ" if self.version >= 7500 else "<III")
        self.nodes = []
        offset = HEADER_LENGTH
        try:
            while offset + self.record.size + 1 <= len(self.data):
                node, offset = self._read_node(offset)
                if node is None:
                    break
                self.nodes.append(node)
        except (struct.error, IndexError):
            raise FbxError("%s is truncated or damaged" % path)

    def first(self, name):
        for node in self.nodes:
            if node.name == name:
                return node
        return None

    def _read_node(self, offset):
        end, property_count, property_bytes = self.record.unpack_from(self.data, offset)
        offset += self.record.size
        name_length = self.data[offset]
        offset += 1
        if end == 0:
            return None, offset  # the all-zero record that closes a list of nodes
        name = self.data[offset:offset + name_length].decode("ascii", "replace")
        offset += name_length
        if end > len(self.data) or end < offset + property_bytes:
            raise FbxError("node %r has an end offset outside the file" % name)

        props = []
        cursor = offset
        for _ in range(property_count):
            kind = chr(self.data[cursor])
            cursor += 1
            if kind == "Y":
                props.append(struct.unpack_from("<h", self.data, cursor)[0])
                cursor += 2
            elif kind == "C":
                props.append(self.data[cursor] != 0)
                cursor += 1
            elif kind == "I":
                props.append(struct.unpack_from("<i", self.data, cursor)[0])
                cursor += 4
            elif kind == "F":
                props.append(struct.unpack_from("<f", self.data, cursor)[0])
                cursor += 4
            elif kind == "D":
                props.append(struct.unpack_from("<d", self.data, cursor)[0])
                cursor += 8
            elif kind == "L":
                props.append(struct.unpack_from("<q", self.data, cursor)[0])
                cursor += 8
            elif kind in "SR":
                length = struct.unpack_from("<I", self.data, cursor)[0]
                cursor += 4
                props.append(self.data[cursor:cursor + length])
                cursor += length
            elif kind in ARRAY_TYPES:
                count, encoding, length = struct.unpack_from("<III", self.data, cursor)
                cursor += 12
                props.append(ArrayRef(kind, count, encoding, cursor, length))
                cursor += length
            else:
                raise FbxError("node %r has a property of unknown type %r" % (name, kind))
        if cursor != offset + property_bytes:
            raise FbxError("node %r: its properties do not fill the length the record states" % name)

        children = []
        offset = cursor
        while offset < end:
            child, offset = self._read_node(offset)
            if child is None:
                break
            children.append(child)
        return Node(name, props, children), end

    def decode(self, node, name):
        """The array held by child <name> of <node>, or None when there is no such child."""
        child = node.first(name) if node is not None else None
        if child is None or not child.props or not isinstance(child.props[0], ArrayRef):
            return None
        ref = child.props[0]
        code, width = ARRAY_TYPES[ref.kind]
        raw = self.view[ref.offset:ref.offset + ref.length]
        if ref.encoding == 1:
            try:
                raw = zlib.decompress(raw)
            except zlib.error as error:
                raise FbxError("array %s cannot be decompressed: %s" % (name, error))
        elif ref.encoding != 0:
            raise FbxError("array %s uses unknown encoding %d" % (name, ref.encoding))
        if len(raw) != ref.count * width:
            raise FbxError("array %s holds %d bytes, expected %d" % (name, len(raw), ref.count * width))
        values = array.array(code)
        if values.itemsize != width:
            raise FbxError("this Python stores type %r in %d bytes, not %d" % (code, values.itemsize, width))
        values.frombytes(raw)
        if sys.byteorder == "big":
            values.byteswap()
        return values


def text(value):
    """An FBX string property. Object names look like b'name\\x00\\x01Class'."""
    if isinstance(value, (bytes, bytearray)):
        return bytes(value).split(b"\x00\x01")[0].decode("utf-8", "replace")
    return str(value)


def properties70(node):
    """The Properties70 block as {name: [values]}."""
    result = {}
    block = node.first("Properties70") if node is not None else None
    if block is not None:
        for entry in block.children:
            if entry.name == "P" and entry.props:
                result[text(entry.props[0])] = list(entry.props[4:])
    return result


class Measured:
    """What one mesh object of the FBX holds."""

    def __init__(self, name):
        self.name = name
        self.rotation = (0.0, 0.0, 0.0)
        self.scaling = (1.0, 1.0, 1.0)
        self.translation = (0.0, 0.0, 0.0)
        self.control_points = 0
        self.unused_points = 0
        self.polygons = 0
        self.degenerate_polygons = 0
        self.triangles = 0
        self.uv_layers = 0
        self.uv_note = ""
        self.bounds_min = [0.0, 0.0, 0.0]
        self.bounds_max = [0.0, 0.0, 0.0]
        self.materials = []  # dicts: material, triangles, vertices, uvSpan (None when not measured)

    def size(self):
        return [high - low for low, high in zip(self.bounds_min, self.bounds_max)]

    def triangles_by_material(self):
        result = {}
        for item in self.materials:
            if item["triangles"] > 0:
                result[item["material"]] = result.get(item["material"], 0) + item["triangles"]
        return result

    def sub_meshes(self):
        """The object's sub-meshes in the manifest's shape."""
        result = []
        for item in self.materials:
            if item["triangles"] <= 0:
                continue
            span = item["uvSpan"]
            usable = (span[0] > USABLE_UV_SPAN and span[1] > USABLE_UV_SPAN) if span is not None else self.uv_layers > 0
            result.append({
                "material": item["material"], "triangles": item["triangles"],
                "vertices": item["vertices"], "hasUv0": bool(usable),
            })
        return result


def union(marks):
    """Byte-wise OR of equally long bytearrays of 0 and 1, without a Python-level loop."""
    if len(marks) == 1:
        return marks[0]
    combined = 0
    for item in marks:
        combined |= int.from_bytes(item, "little")
    return bytearray(combined.to_bytes(len(marks[0]), "little"))


def low_high(values, marks):
    """Smallest and largest of the values whose mark is set (all of them when marks is None)."""
    if marks is None:
        return min(values), max(values)
    return min(itertools.compress(values, marks)), max(itertools.compress(values, marks))


def check_transform(name, props):
    """Raises unless the node's transform is the one this tool can apply exactly."""
    rotation = tuple(float(v) for v in props.get("Lcl Rotation", [0.0, 0.0, 0.0])[:3])
    scaling = tuple(float(v) for v in props.get("Lcl Scaling", [1.0, 1.0, 1.0])[:3])
    translation = tuple(float(v) for v in props.get("Lcl Translation", [0.0, 0.0, 0.0])[:3])
    if len(rotation) != 3 or len(scaling) != 3 or len(translation) != 3:
        raise FbxError("object %s: Lcl transform properties are incomplete" % name)
    if any(abs(a - b) > ROTATION_TOLERANCE_DEGREES for a, b in zip(rotation, EXPECTED_ROTATION)):
        raise FbxError("object %s has Lcl Rotation %s. Only (-90, 0, 0) is understood: look at the file, "
                       "do not guess" % (name, tuple(round(v, 4) for v in rotation)))
    if any(v <= 0.0 for v in scaling):
        raise FbxError("object %s has a non-positive Lcl Scaling %s" % (name, scaling))
    for key, neutral in NEUTRAL_TRANSFORMS.items():
        values = [float(v) for v in props.get(key, []) if isinstance(v, (int, float))]
        if any(abs(v - neutral) > 1e-6 for v in values):
            raise FbxError("object %s has %s %s, which this tool does not apply" % (name, key, values))
    return rotation, scaling, translation


def check_ancestors(name, model_id, parent_of, models):
    """
    A parent node that only moves its children changes nothing that is measured here (translations are
    ignored anyway). A parent that turns or scales them does, and is refused.
    """
    seen = set()
    ancestor = parent_of.get(model_id)
    while ancestor is not None and ancestor not in seen:
        seen.add(ancestor)
        ancestor_name, _, node = models[ancestor]
        props = properties70(node)
        rotation = [float(v) for v in props.get("Lcl Rotation", [0.0, 0.0, 0.0])[:3]]
        scaling = [float(v) for v in props.get("Lcl Scaling", [1.0, 1.0, 1.0])[:3]]
        turned = any(abs(v) > ROTATION_TOLERANCE_DEGREES for v in rotation) or any(abs(v - 1.0) > 1e-6 for v in scaling)
        for key, neutral in NEUTRAL_TRANSFORMS.items():
            if key in ("RotationOffset", "RotationPivot", "ScalingOffset", "ScalingPivot", "GeometricTranslation"):
                continue  # these only move things once rotation and scale are neutral
            values = [float(v) for v in props.get(key, []) if isinstance(v, (int, float))]
            turned = turned or any(abs(v - neutral) > 1e-6 for v in values)
        if turned:
            raise FbxError("object %s is a child of node %s, which rotates or scales it (Lcl Rotation %s, Lcl "
                           "Scaling %s). Nested transforms are not understood: look at the file, do not guess"
                           % (name, ancestor_name, tuple(rotation), tuple(scaling)))
        ancestor = parent_of.get(ancestor)


def measure(fbx, name, model_props, geometry, material_names, unit):
    result = Measured(name)
    result.rotation, result.scaling, result.translation = check_transform(name, model_props)

    points = fbx.decode(geometry, "Vertices")
    corners = fbx.decode(geometry, "PolygonVertexIndex")
    if points is None or corners is None or len(points) == 0 or len(points) % 3 != 0 or len(corners) == 0:
        raise FbxError("object %s: the geometry has no usable Vertices or PolygonVertexIndex array" % name)
    point_count = len(points) // 3
    result.control_points = point_count

    # Material per polygon. "AllSame" stores one index for the whole mesh.
    polygon_material = None
    single_material = 0
    layer = geometry.first("LayerElementMaterial")
    if layer is not None:
        mapping = text(layer.first("MappingInformationType").props[0]) if layer.first("MappingInformationType") else ""
        indices = fbx.decode(layer, "Materials")
        if indices is None or len(indices) == 0:
            raise FbxError("object %s: LayerElementMaterial has no Materials array" % name)
        if mapping == "ByPolygon":
            polygon_material = indices
        elif mapping == "AllSame":
            single_material = indices[0]
        else:
            raise FbxError("object %s: material mapping %r is not understood" % (name, mapping))
    highest = max(polygon_material) if polygon_material is not None else single_material
    lowest = min(polygon_material) if polygon_material is not None else single_material
    if lowest < 0:
        raise FbxError("object %s: a polygon has a negative material index" % name)
    slot_count = max(highest + 1, len(material_names), 1)
    slot_names = [material_names[i] if i < len(material_names) else "(material slot %d)" % i for i in range(slot_count)]

    # First UV layer: which UV entry each polygon corner uses.
    uv_layers = geometry.all("LayerElementUV")
    result.uv_layers = len(uv_layers)
    uv_values = None
    uv_of_corner = None
    if uv_layers:
        first_layer = uv_layers[0]
        mapping = text(first_layer.first("MappingInformationType").props[0]) if first_layer.first("MappingInformationType") else ""
        reference = text(first_layer.first("ReferenceInformationType").props[0]) if first_layer.first("ReferenceInformationType") else ""
        uv_values = fbx.decode(first_layer, "UV")
        if uv_values is None or len(uv_values) < 2 or len(uv_values) % 2 != 0:
            uv_values = None
            result.uv_note = "first UV layer holds no values"
        elif mapping == "ByPolygonVertex" and reference == "IndexToDirect":
            uv_of_corner = fbx.decode(first_layer, "UVIndex")
        elif mapping == "ByPolygonVertex" and reference == "Direct":
            uv_of_corner = range(len(uv_values) // 2)
        if uv_values is not None and (uv_of_corner is None or len(uv_of_corner) != len(corners)):
            uv_values = None
            uv_of_corner = None
            result.uv_note = "UV mapping %s/%s not measured" % (mapping, reference)
    uv_count = len(uv_values) // 2 if uv_values is not None else 0

    # One pass over the polygon corners. A polygon ends at a negative index, which is the bitwise
    # NOT of the real one. An n-sided polygon makes n - 2 triangles.
    triangles = [0] * slot_count
    used_points = [bytearray(point_count) for _ in range(slot_count)]
    used_uvs = [bytearray(uv_count) for _ in range(slot_count)] if uv_values is not None else None
    polygon = 0
    sides = 0
    degenerate = 0
    try:
        slot = polygon_material[0] if polygon_material is not None else single_material
        for position, index in enumerate(corners):
            sides += 1
            last = index < 0
            if last:
                index = ~index
            if index >= point_count:
                raise IndexError(index)
            used_points[slot][index] = 1
            if used_uvs is not None:
                uv = uv_of_corner[position]
                if uv >= 0:
                    used_uvs[slot][uv] = 1
            if last:
                if sides >= 3:
                    triangles[slot] += sides - 2
                else:
                    degenerate += 1
                sides = 0
                polygon += 1
                if polygon_material is not None and position + 1 < len(corners):
                    slot = polygon_material[polygon]
    except IndexError:
        raise FbxError("object %s: an index points outside its array (damaged geometry)" % name)
    if sides != 0:
        raise FbxError("object %s: the last polygon is not closed" % name)
    if polygon_material is not None and polygon != len(polygon_material):
        raise FbxError("object %s: %d polygons but %d material indices" % (name, polygon, len(polygon_material)))
    result.polygons = polygon
    result.degenerate_polygons = degenerate
    result.triangles = sum(triangles)

    # Bounds over the control points that polygons use: a loose point is in the file but in no mesh.
    used = union(used_points)
    used_count = used.count(1)
    result.unused_points = point_count - used_count
    if used_count == 0:
        raise FbxError("object %s has no polygons" % name)
    marks = None if used_count == point_count else used
    factor = [axis / 100.0 * unit for axis in result.scaling]
    x_low, x_high = low_high(points[0::3], marks)
    y_low, y_high = low_high(points[1::3], marks)
    z_low, z_high = low_high(points[2::3], marks)
    # Rotation (-90, 0, 0) after the scale: (x, y, z) -> (x * sx, z * sz, -y * sy).
    result.bounds_min = [x_low * factor[0], z_low * factor[2], -y_high * factor[1]]
    result.bounds_max = [x_high * factor[0], z_high * factor[2], -y_low * factor[1]]

    for slot in range(slot_count):
        span = None
        if used_uvs is not None and used_uvs[slot].count(1) > 0:
            u_low, u_high = low_high(uv_values[0::2], used_uvs[slot])
            v_low, v_high = low_high(uv_values[1::2], used_uvs[slot])
            span = (u_high - u_low, v_high - v_low)
        result.materials.append({
            "material": slot_names[slot], "triangles": triangles[slot],
            "vertices": used_points[slot].count(1), "uvSpan": span,
        })
    return result


def read_objects(fbx):
    """Every mesh object of the file, measured. Returns (unit scale factor, [Measured], file order)."""
    settings = properties70(fbx.first("GlobalSettings"))
    unit = float(settings.get("UnitScaleFactor", [1.0])[0])
    for key, expected in EXPECTED_AXES.items():
        actual = settings.get(key, [expected])[0]
        if actual != expected:
            raise FbxError("the file declares %s = %s, expected %s (right-handed, Y up). Look at it, do not guess"
                           % (key, actual, expected))

    objects = fbx.first("Objects")
    connections = fbx.first("Connections")
    if objects is None or connections is None:
        raise FbxError("the file has no Objects or Connections section")

    models = {}       # id -> (name, sub-type, node)
    geometries = {}   # id -> node
    materials = {}    # id -> name
    for node in objects.children:
        if len(node.props) < 2:
            continue
        if node.name == "Model":
            models[node.props[0]] = (text(node.props[1]), text(node.props[2]) if len(node.props) > 2 else "", node)
        elif node.name == "Geometry":
            geometries[node.props[0]] = node
        elif node.name == "Material":
            materials[node.props[0]] = text(node.props[1])

    geometry_of = {}
    materials_of = {}
    parent_of = {}
    for link in connections.children:
        if link.name != "C" or len(link.props) < 3 or text(link.props[0]) != "OO":
            continue
        child, parent = link.props[1], link.props[2]
        if parent in models:
            if child in geometries:
                geometry_of.setdefault(parent, []).append(child)
            elif child in materials:
                materials_of.setdefault(parent, []).append(materials[child])  # connection order is the slot order
            elif child in models:
                parent_of[child] = parent

    measured = []
    seen = set()
    for model_id, (name, kind, node) in models.items():
        if kind != "Mesh":
            continue
        linked = geometry_of.get(model_id, [])
        if not linked:
            continue  # an empty mesh node: nothing to import, nothing to measure
        if len(linked) > 1:
            raise FbxError("object %s is connected to %d geometries" % (name, len(linked)))
        check_ancestors(name, model_id, parent_of, models)
        if name in seen:
            raise FbxError("the file holds two mesh objects named %s" % name)
        seen.add(name)
        measured.append(measure(fbx, name, properties70(node), geometries[linked[0]],
                                materials_of.get(model_id, []), unit))
    if not measured:
        raise FbxError("the file holds no mesh object")
    return unit, measured


def manifest_triangles_by_material(entry_object):
    result = {}
    for sub in entry_object.get("subMeshes", []):
        result[sub["material"]] = result.get(sub["material"], 0) + sub["triangles"]
    return result


def compare(measured, entry_object):
    """Differences between a measured object and its manifest object, as text. Empty when they match."""
    problems = []
    if measured.triangles != entry_object["triangles"]:
        problems.append("triangles: FBX %d, manifest %d" % (measured.triangles, entry_object["triangles"]))
    in_file = measured.triangles_by_material()
    in_manifest = manifest_triangles_by_material(entry_object)
    if in_file != in_manifest:
        problems.append("triangles per material: FBX %s, manifest %s" % (in_file, in_manifest))
    expected = list(entry_object["boundsMin"]) + list(entry_object["boundsMax"])
    actual = measured.bounds_min + measured.bounds_max
    largest = max(high - low for low, high in zip(entry_object["boundsMin"], entry_object["boundsMax"]))
    tolerance = max(BOUNDS_RELATIVE_TOLERANCE * largest, BOUNDS_MINIMUM_TOLERANCE)
    difference = max(abs(a - b) for a, b in zip(actual, expected))
    if difference > tolerance:
        problems.append("bounds differ by up to %.4f m (tolerance %.4f m): manifest min %s max %s"
                        % (difference, tolerance, entry_object["boundsMin"], entry_object["boundsMax"]))
    return problems, difference, tolerance


def vector(values, digits=4):
    # "+ 0.0" keeps a rounded -0.0 from printing as "-0.0".
    return "(" + ", ".join("%.*f" % (digits, round(v, digits) + 0.0) for v in values) + ")"


def describe(measured):
    lines = ["  %s" % measured.name]
    lines.append("    node: Lcl Rotation %s  Lcl Scaling %s  Lcl Translation %s"
                 % (vector(measured.rotation, 1), vector(measured.scaling, 3), vector(measured.translation, 1)))
    extra = ""
    if measured.unused_points:
        extra += "  (%d loose points ignored)" % measured.unused_points
    if measured.degenerate_polygons:
        extra += "  (%d polygons with fewer than 3 corners)" % measured.degenerate_polygons
    lines.append("    triangles %d  polygons %d  control points %d  UV layers %d%s"
                 % (measured.triangles, measured.polygons, measured.control_points, measured.uv_layers, extra))
    for item in measured.materials:
        if item["triangles"] <= 0:
            lines.append("    material %s: no polygons" % item["material"])
            continue
        if item["uvSpan"] is not None:
            uv = "UV span %.3f x %.3f" % item["uvSpan"]
        else:
            uv = measured.uv_note or ("no UV layer" if measured.uv_layers == 0 else "UV span not measured")
        lines.append("    material %s: triangles %d  control points %d  %s"
                     % (item["material"], item["triangles"], item["vertices"], uv))
    lines.append("    bounds: min %s  max %s  size %s"
                 % (vector(measured.bounds_min), vector(measured.bounds_max), vector(measured.size())))
    return lines


def rounded(values):
    return [round(v, 4) + 0.0 for v in values]  # "+ 0.0" turns -0.0 into 0.0


def load_manifest(path):
    try:
        with open(path, "r", encoding="utf-8") as handle:
            manifest = json.load(handle)
    except (OSError, ValueError) as error:
        raise FbxError("cannot read the manifest %s: %s" % (path, error))
    if not isinstance(manifest, list):
        raise FbxError("the manifest %s is not a JSON array" % path)
    return manifest


def save_manifest(path, manifest):
    """Same layout as the file was written with (indent 1, trailing newline), replaced in one step."""
    temporary = path + ".tmp"
    with open(temporary, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(json.dumps(manifest, indent=1) + "\n")
    os.replace(temporary, path)


class Arguments(argparse.ArgumentParser):
    def error(self, message):
        # argparse would exit with 2, which this tool reserves for "measured and different".
        self.print_usage(sys.stderr)
        sys.stderr.write("error: %s\n" % message)
        sys.exit(EXIT_ERROR)


def run(arguments):
    manifest = load_manifest(arguments.manifest)
    entry = next((item for item in manifest if isinstance(item, dict) and item.get("id") == arguments.asset_id), None)
    if entry is None:
        raise FbxError("the manifest has no asset with id %s" % arguments.asset_id)
    if entry.get("kind") != "model" or not entry.get("objects"):
        raise FbxError("asset %s is not a model with objects" % arguments.asset_id)

    fbx = FbxFile(arguments.fbx)
    unit, measured = read_objects(fbx)
    by_name = {item.name: item for item in measured}
    source = entry.get("objectsSource", "")
    lod_nodes = list(entry.get("fbxNodes", []))

    print("%s: %s" % (arguments.asset_id, arguments.fbx))
    print("  FBX version %d, UnitScaleFactor %g, %d mesh objects, manifest objectsSource \"%s\""
          % (fbx.version, unit, len(measured), source))

    mismatches = []   # text, for the summary
    missing = []      # manifest objects with no node in the file: --write cannot repair these
    matched = {}      # FBX object name -> manifest object
    for entry_object in entry["objects"]:
        name = entry_object["name"]
        # The glTF names the log "dead_tree_trunk_02"; its FBX node is "dead_tree_trunk_02_LOD0".
        node_name = name if name in by_name else name + "_LOD0"
        if node_name not in by_name or node_name in matched:
            missing.append(name)
            mismatches.append("%s: the manifest object has no mesh object in the FBX" % name)
            continue
        matched[node_name] = entry_object

    for item in measured:
        for line in describe(item):
            print(line)
        entry_object = matched.get(item.name)
        if entry_object is not None:
            problems, difference, tolerance = compare(item, entry_object)
            label = "" if entry_object["name"] == item.name else " (as %s)" % entry_object["name"]
            if problems:
                for problem in problems:
                    print("    MISMATCH%s: %s" % (label, problem))
                    mismatches.append("%s: %s" % (item.name, problem))
            else:
                print("    manifest%s: triangles OK, materials OK, bounds OK (largest difference %.4f m, tolerance %.4f m)"
                      % (label, difference, tolerance))
        elif item.name in lod_nodes:
            print("    listed in fbxNodes: measured for information, the manifest holds no numbers for it")
        else:
            print("    MISMATCH: the manifest does not list this object")
            mismatches.append("%s: in the FBX but not in the manifest" % item.name)
    for node_name in lod_nodes:
        if node_name not in by_name:
            mismatches.append("%s: listed in fbxNodes but not in the FBX" % node_name)
            missing.append(node_name)

    if not mismatches:
        if source != "fbx":
            entry["objectsSource"] = "fbx"
            save_manifest(arguments.manifest, manifest)
            print("MATCH: %d objects agree with the manifest. objectsSource set to \"fbx\" in %s"
                  % (len(matched), arguments.manifest))
        else:
            print("MATCH: %d objects agree with the manifest (objectsSource is already \"fbx\")" % len(matched))
        return EXIT_MATCH

    print("MISMATCH: %d differences between the FBX and the manifest:" % len(mismatches))
    for line in mismatches:
        print("  - %s" % line)
    if not arguments.write:
        print("Nothing was changed. Check that the layout still holds with the measured sizes, then run again "
              "with --write to store them.")
        return EXIT_MISMATCH
    if missing:
        print("--write refused: %s cannot be measured because the FBX has no such object. Correct the manifest "
              "by hand." % ", ".join(missing))
        return EXIT_MISMATCH

    stored = 0
    for item in measured:
        entry_object = matched.get(item.name)
        if entry_object is None:
            if item.name in lod_nodes:
                continue  # an authored lower level: the manifest keeps no numbers for it
            entry_object = {"name": item.name}
            entry["objects"].append(entry_object)
        entry_object["boundsMin"] = rounded(item.bounds_min)
        entry_object["boundsMax"] = rounded(item.bounds_max)
        entry_object["triangles"] = item.triangles
        entry_object["subMeshes"] = item.sub_meshes()
        stored += 1
    entry["objectsSource"] = "fbx"
    remark = ("objects[] was measured from the FBX and stored by fbx_objects.py --write: vertices are FBX control "
              "points per material, hasUv0 means the first UV layer spans more than 0.01 on both axes.")
    previous = entry.get("note", "")
    if remark not in previous:
        entry["note"] = (previous + " " + remark).strip()
    save_manifest(arguments.manifest, manifest)
    print("WROTE: the measured values of %d objects are now in %s and objectsSource is \"fbx\""
          % (stored, arguments.manifest))
    return EXIT_MATCH


def main():
    parser = Arguments(description="Measure the mesh objects of a binary FBX and compare them with the forest "
                                   "asset manifest. Exit status: 0 match, 1 cannot measure, 2 mismatch.")
    parser.add_argument("fbx", help="the downloaded .fbx file")
    parser.add_argument("manifest", help="forest-asset-manifest.json (updated in place)")
    parser.add_argument("asset_id", metavar="asset-id", help="the manifest id of the model")
    parser.add_argument("--write", action="store_true",
                        help="after a mismatch: store the measured values in the manifest and set objectsSource")
    arguments = parser.parse_args()
    try:
        return run(arguments)
    except FbxError as error:
        sys.stderr.write("error: %s\n" % error)
        return EXIT_ERROR


if __name__ == "__main__":
    sys.exit(main())
