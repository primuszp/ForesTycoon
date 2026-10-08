"""Builds the licensed log truck (tractor + stake trailer + logs) in the flattened vertex-colour format of
GlbTruckModel. The output goes to ForesTycoon/Assets/Licensed/ (git-ignored: the purchased models must never be
committed). Without it the game falls back to Assets/Vehicles/log-truck.glb.

Usage (repo root): python tools/convert_licensed_truck.py <Logging_Facility_glb/Separate_assets_glb>
Needs numpy and Pillow.
"""
import io, json, struct, sys
from pathlib import Path
import numpy as np
from PIL import Image

if len(sys.argv) != 2:
    raise SystemExit(__doc__)
source = Path(sys.argv[1])


def load(path):
    data = path.read_bytes()
    n = struct.unpack_from('<I', data, 12)[0]
    doc = json.loads(data[20:20 + n])
    return doc, data[28 + n:]


def accessor(doc, binary, index):
    a = doc['accessors'][index]; v = doc['bufferViews'][a['bufferView']]
    width = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[a['type']]
    dtype = {5126: '<f4', 5125: '<u4', 5123: '<u2', 5121: 'u1'}[a['componentType']]
    item = np.dtype(dtype).itemsize
    stride = v.get('byteStride', item * width)
    out = np.ndarray((a['count'], width), dtype=dtype, buffer=binary,
                     offset=v.get('byteOffset', 0) + a.get('byteOffset', 0), strides=(stride, item)).copy()
    if a.get('normalized') and dtype != '<f4':
        out = out.astype('f4') / (255 if dtype == 'u1' else 65535)
    return out


def trs(node):
    if 'matrix' in node:
        return np.array(node['matrix'], 'f8').reshape(4, 4).T
    t = node.get('translation', [0, 0, 0]); x, y, z, w = node.get('rotation', [0, 0, 0, 1]); s = node.get('scale', [1, 1, 1])
    r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m = np.eye(4); m[:3, :3] = r * np.array(s); m[:3, 3] = t
    return m


def images(doc, binary):
    out = []
    for image in doc.get('images', []):
        v = doc['bufferViews'][image['bufferView']]
        raw = binary[v.get('byteOffset', 0):v.get('byteOffset', 0) + v['byteLength']]
        out.append(np.asarray(Image.open(io.BytesIO(raw)).convert('RGB'), 'f4') / 255)
    return out


def parts(path):
    """(name, positions, normals, triangle indices, linear rgb) per mesh node, skinned in the rest pose, glTF frame."""
    doc, binary = load(path)
    atlas = images(doc, binary)
    world = [None] * len(doc['nodes'])
    def walk(i, parent):
        world[i] = parent @ trs(doc['nodes'][i])
        for c in doc['nodes'][i].get('children', []): walk(c, world[i])
    for root in doc['scenes'][doc.get('scene', 0)]['nodes']: walk(root, np.eye(4))
    result = []
    for i, node in enumerate(doc['nodes']):
        if 'mesh' not in node: continue
        for prim in doc['meshes'][node['mesh']]['primitives']:
            att = prim['attributes']
            pos = accessor(doc, binary, att['POSITION']).astype('f8'); nrm = accessor(doc, binary, att['NORMAL']).astype('f8')
            if 'skin' in node and 'JOINTS_0' in att:
                skin = doc['skins'][node['skin']]
                inverse = accessor(doc, binary, skin['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
                joint = np.array([world[j] for j in skin['joints']]) @ inverse
                joints = accessor(doc, binary, att['JOINTS_0']).astype(int); weights = accessor(doc, binary, att['WEIGHTS_0']).astype('f8')
                weights /= np.maximum(weights.sum(1, keepdims=True), 1e-9)
                m = np.einsum('vk,vkij->vij', weights, joint[joints])
            else:
                m = np.repeat(world[i][None], len(pos), 0)
            p = np.einsum('vij,vj->vi', m, np.c_[pos, np.ones(len(pos))])[:, :3]
            n = np.einsum('vij,vj->vi', m[:, :3, :3], nrm); n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
            material = doc['materials'][prim['material']] if 'material' in prim else {}
            pbr = material.get('pbrMetallicRoughness', {}); factor = np.array(pbr.get('baseColorFactor', [1, 1, 1, 1])[:3])
            if 'baseColorTexture' in pbr and 'TEXCOORD_0' in att:
                img = atlas[doc['textures'][pbr['baseColorTexture']['index']]['source']]
                uv = accessor(doc, binary, att['TEXCOORD_0']) % 1.0
                xy = np.clip((uv * [img.shape[1], img.shape[0]]).astype(int), 0, [img.shape[1] - 1, img.shape[0] - 1])
                colour = img[xy[:, 1], xy[:, 0]] * factor
            else:
                colour = np.tile(factor, (len(p), 1))
            idx = accessor(doc, binary, prim['indices']).reshape(-1) if 'indices' in prim else np.arange(len(p))
            result.append((node.get('name', 'part'), p, n, idx, np.power(np.clip(colour, 0, 1), 2.2)))
    return result


def to_game(points):
    """glTF (x forward, y up, z right) to the game frame (+X forward, +Y left, +Z up); a proper rotation."""
    return np.c_[points[:, 0], -points[:, 2], points[:, 1]]


truck = [(name, to_game(p), to_game(n), i, c) for name, p, n, i, c in parts(source / 'truck_001.glb')]
trailer = [(name, to_game(p), to_game(n), i, c) for name, p, n, i, c in parts(source / 'truck_trailer_004.glb')]

# Fifth wheel: the midpoint of the tractor's rear axles; the trailer's front end sits a little ahead of it.
truck_wheels = [p for name, p, *_ in truck if 'wheel' in name]
centres = sorted(float(w[:, 0].mean()) for w in truck_wheels)
rear_axles = [x for x in centres if x < 0] or centres[:2]
fifth_wheel = float(np.mean(rear_axles))
trailer_front = max(float(p[:, 0].max()) for _, p, *_ in trailer)
shift = np.array([fifth_wheel + 1.2 - trailer_front, 0, 0])
trailer = [(name, p + shift, n, i, c) for name, p, n, i, c in trailer]

# Deck height: the largest upward-facing surface of the trailer body below 2 m.
body = [p for name, p, n, *_ in trailer if 'wheel' not in name][0]
body_n = [n for name, p, n, *_ in trailer if 'wheel' not in name][0]
up = body[body_n[:, 2] > 0.9][:, 2]
up = up[up < 2.0]
deck = float(np.percentile(up, 90)) if len(up) else 1.4
body_all = np.concatenate([p for name, p, *_ in trailer if 'wheel' not in name])
bed_front, bed_rear = float(body_all[:, 0].max()) - 0.4, float(body_all[:, 0].min()) + 0.3
bed_width = float(body_all[:, 1].max() - body_all[:, 1].min()) - 0.5

# Logs: two lengths along the bed, a 4-3-2 pyramid across it.
log_parts = parts(source / 'log_002.glb')
lp = np.concatenate([p for _, p, *_ in log_parts])
log_len, log_dia = float(lp[:, 1].max() - lp[:, 1].min()), float(lp[:, 0].max() - lp[:, 0].min())
segment = (bed_front - bed_rear) / 2
cargo = []
rows = [4, 3, 2]
radius = min(bed_width / (2 * rows[0]), 0.36)
for s in range(2):
    centre_x = bed_rear + segment * (s + 0.5)
    for layer, count in enumerate(rows):
        for k in range(count):
            y = (k - (count - 1) / 2) * 2 * radius
            z = deck + radius + layer * radius * 1.73
            for name, p, n, i, c in log_parts:
                # The log stands along glTF y; the cyclic swap (y, z, x) lays it along game X and is a
                # proper rotation, so the triangle winding stays outward.
                q = p - lp.mean(0)
                q = np.c_[q[:, 1] * (segment * 0.96 / log_len), q[:, 2] * (2 * radius / log_dia), q[:, 0] * (2 * radius / log_dia)]
                m = np.c_[n[:, 1], n[:, 2], n[:, 0]]
                cargo.append((f'log_{s}_{layer}_{k}', q + [centre_x, y, z], m, i, c))

# Wheel naming for GlbTruckModel: the steering axle carries "Front".
front_x = max(centres)
named = []
for name, p, n, i, c in truck + trailer:
    if 'wheel' in name.lower():
        cat = 'wheel'; name = ('Wheel_Front_' if abs(float(p[:, 0].mean()) - front_x) < 0.3 else 'Wheel_Rear_') + name
    else:
        cat = 'body'
    named.append((name, p, n, i, c, cat))
# Cargo appears from the bottom up as the load grows.
named += [(name, p, n, i, c, 'cargo') for name, p, n, i, c in sorted(cargo, key=lambda t: float(t[1][:, 2].mean()))]

allp = np.concatenate([t[1] for t in named])
mn, mx = allp.min(0), allp.max(0)
scale = 3.3 / (mx[0] - mn[0])
origin = np.array([(mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, mn[2]])

payload = bytearray(); views = []; accessors = []; meshes = []; nodes = []
def append(array, kind, component):
    while len(payload) % 4: payload.append(0)
    raw = array.tobytes(); views.append({'buffer': 0, 'byteOffset': len(payload), 'byteLength': len(raw)}); payload.extend(raw)
    a = {'bufferView': len(views) - 1, 'componentType': component, 'count': len(array), 'type': kind}
    if kind == 'VEC3': a.update(min=array.min(0).tolist(), max=array.max(0).tolist())
    accessors.append(a); return len(accessors) - 1
for name, p, n, i, c, cat in named:
    pos = ((p - origin) * scale).astype('<f4'); nrm = n.astype('<f4')
    colour = np.c_[c, np.ones(len(c))].astype('<f4')
    prim = {'attributes': {'POSITION': append(pos, 'VEC3', 5126), 'NORMAL': append(nrm, 'VEC3', 5126), 'COLOR_0': append(colour, 'VEC4', 5126)},
            'indices': append(i.astype('<u4').reshape(-1, 1), 'SCALAR', 5125), 'mode': 4}
    meshes.append({'name': name, 'primitives': [prim]})
    nodes.append({'name': name, 'mesh': len(meshes) - 1, 'extras': {'category': cat, 'pivot': ((pos.min(0) + pos.max(0)) / 2).tolist()}})
out = {'asset': {'version': '2.0', 'generator': 'ForesTycoon licensed log truck (not for redistribution)'}, 'scene': 0,
       'scenes': [{'nodes': list(range(len(nodes)))}], 'nodes': nodes, 'meshes': meshes, 'buffers': [{'byteLength': len(payload)}],
       'bufferViews': views, 'accessors': accessors}
j = json.dumps(out, separators=(',', ':')).encode(); j += b' ' * ((-len(j)) % 4); payload += b'\0' * ((-len(payload)) % 4)
glb = struct.pack('<III', 0x46546c67, 2, 12 + 8 + len(j) + 8 + len(payload)) + struct.pack('<II', len(j), 0x4e4f534a) + j + struct.pack('<II', len(payload), 0x004e4942) + payload
dest = Path('ForesTycoon/Assets/Licensed/log-truck.glb'); dest.parent.mkdir(parents=True, exist_ok=True); dest.write_bytes(glb)
print(f'Licensed log truck: {len(nodes)} parts ({sum(t[5] == "cargo" for t in named)} logs), '
      f'{sum(len(t[3]) // 3 for t in named)} triangles, {len(glb)} bytes -> {dest}')
