"""Builds the licensed, textured log truck (tractor + stake trailer + logs) for TexturedTruckModel.

The output goes to ForesTycoon/Assets/Licensed/log-truck-textured.glb, which is git-ignored: the purchased models
must never be committed (their licence forbids redistribution). Without it the game falls back to the
repository's Assets/Vehicles/log-truck.glb.

Frame: +X forward, +Y left, +Z up; 3.3 units long; ground at the bottom of the wheels. Wheels are separate nodes
placed at their centres (so the game can spin and steer them; steering axle names start with "wheel_front"),
logs are separate "cargo_" nodes ordered bottom-up. The tractor hangs on "body"; the semi-trailer is articulated:
its "trailer" node sits at the kingpin (the hinge it turns about), its wheels are "trailer_wheel_" nodes, and the
logs ride on it. The original UVs
and texture atlases are kept.

Usage (repo root): python tools/convert_licensed_truck.py <Logging_Facility_glb/Separate_assets_glb>
Needs numpy.
"""
import json, struct, sys
from pathlib import Path
import numpy as np

if len(sys.argv) != 2:
    raise SystemExit(__doc__)
source = Path(sys.argv[1])
textures = []      # PNG bytes, one material each
texture_keys = {}  # (file, image index) -> texture slot


def load(path):
    data = path.read_bytes()
    n = struct.unpack_from('<I', data, 12)[0]
    return json.loads(data[20:20 + n]), data[28 + n:]


def accessor(doc, binary, index):
    a = doc['accessors'][index]; v = doc['bufferViews'][a['bufferView']]
    width = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[a['type']]
    dtype = {5126: '<f4', 5125: '<u4', 5123: '<u2', 5121: 'u1'}[a['componentType']]
    item = np.dtype(dtype).itemsize
    out = np.ndarray((a['count'], width), dtype=dtype, buffer=binary, offset=v.get('byteOffset', 0) + a.get('byteOffset', 0),
                     strides=(v.get('byteStride', item * width), item)).copy()
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


def texture_slot(doc, binary, path, material):
    pbr = doc['materials'][material].get('pbrMetallicRoughness', {})
    if 'baseColorTexture' not in pbr:
        return None
    image = doc['textures'][pbr['baseColorTexture']['index']]['source']
    key = (str(path), image)
    if key not in texture_keys:
        img = doc['images'][image]; v = doc['bufferViews'][img['bufferView']]
        raw = binary[v.get('byteOffset', 0):v.get('byteOffset', 0) + v['byteLength']]
        if raw[:8] != b'\x89PNG\r\n\x1a\n':
            raise SystemExit(f'{path.name}: texture {image} is not PNG')
        texture_keys[key] = len(textures); textures.append(bytes(raw))
    return texture_keys[key]


def parts(path):
    """(name, positions, normals, indices, uv, texture slot) per primitive, skinned in the rest pose, glTF frame."""
    doc, binary = load(path)
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
            idx = accessor(doc, binary, prim['indices']).reshape(-1) if 'indices' in prim else np.arange(len(p))
            uv = accessor(doc, binary, att['TEXCOORD_0']).astype('f4') if 'TEXCOORD_0' in att else np.zeros((len(p), 2), 'f4')
            slot = texture_slot(doc, binary, path, prim['material']) if 'material' in prim else None
            result.append((node.get('name', 'part'), p, n, idx, uv, slot))
    return result


def to_game(points):
    """glTF (x forward, y up, z right) to the game frame (+X forward, +Y left, +Z up); a proper rotation."""
    return np.c_[points[:, 0], -points[:, 2], points[:, 1]]


truck = [(nm, to_game(p), to_game(n), i, uv, t) for nm, p, n, i, uv, t in parts(source / 'truck_001.glb')]
trailer = [(nm, to_game(p), to_game(n), i, uv, t) for nm, p, n, i, uv, t in parts(source / 'truck_trailer_004.glb')]

# Fifth wheel: the midpoint of the tractor's rear axles; the trailer's front end sits a little ahead of it.
centres = sorted(float(p[:, 0].mean()) for nm, p, *_ in truck if 'wheel' in nm)
rear_axles = [x for x in centres if x < 0] or centres[:2]
fifth_wheel = float(np.mean(rear_axles))
trailer_front = max(float(p[:, 0].max()) for _, p, *_ in trailer)
shift = np.array([fifth_wheel + 1.2 - trailer_front, 0, 0])
trailer = [(nm, p + shift, n, i, uv, t) for nm, p, n, i, uv, t in trailer]

# Deck height: the largest upward-facing surface of the trailer body below 2 m.
body = [(p, n) for nm, p, n, *_ in trailer if 'wheel' not in nm]
up = np.concatenate([p[n[:, 2] > 0.9][:, 2] for p, n in body]); up = up[up < 2.0]
deck = float(np.percentile(up, 90)) if len(up) else 1.4
body_all = np.concatenate([p for p, _ in body])
bed_front, bed_rear = float(body_all[:, 0].max()) - 0.4, float(body_all[:, 0].min()) + 0.3
bed_width = float(body_all[:, 1].max() - body_all[:, 1].min()) - 0.5

# Logs: two lengths along the bed, a 4-3-2 pyramid across it.
log_parts = parts(source / 'log_002.glb')
lp = np.concatenate([p for _, p, *_ in log_parts])
log_len, log_dia = float(lp[:, 1].max() - lp[:, 1].min()), float(lp[:, 0].max() - lp[:, 0].min())
segment = (bed_front - bed_rear) / 2
rows = [4, 3, 2]
radius = min(bed_width / (2 * rows[0]), 0.36)
cargo = []
for s in range(2):
    centre_x = bed_rear + segment * (s + 0.5)
    for layer, count in enumerate(rows):
        for k in range(count):
            y = (k - (count - 1) / 2) * 2 * radius
            z = deck + radius + layer * radius * 1.73
            prims = []
            for _, p, n, i, uv, t in log_parts:
                # The log stands along glTF y; the cyclic swap (y, z, x) lays it along game X (a proper rotation).
                q = p - lp.mean(0)
                q = np.c_[q[:, 1] * (segment * 0.96 / log_len), q[:, 2] * (2 * radius / log_dia), q[:, 0] * (2 * radius / log_dia)]
                prims.append((q + [centre_x, y, z], np.c_[n[:, 1], n[:, 2], n[:, 0]], i, uv, t))
            cargo.append((f'cargo_{s}_{layer}_{k}', z, prims))
cargo.sort(key=lambda c: c[1])

# Nodes: tractor body, trailer body (hinged at the kingpin), one node per wheel, one per log.
front_x = max(centres)
groups = [('body', [(p, n, i, uv, t) for nm, p, n, i, uv, t in truck if 'wheel' not in nm.lower()]),
          ('trailer', [(p, n, i, uv, t) for nm, p, n, i, uv, t in trailer if 'wheel' not in nm.lower()])]
for nm, p, n, i, uv, t in truck:
    if 'wheel' in nm.lower():
        front = abs(float(p[:, 0].mean()) - front_x) < 0.3
        groups.append((('wheel_front_' if front else 'wheel_rear_') + nm, [(p, n, i, uv, t)]))
for k, (nm, p, n, i, uv, t) in enumerate(trailer):
    if 'wheel' in nm.lower():
        groups.append((f'trailer_wheel_{k}_' + nm, [(p, n, i, uv, t)]))
groups += [(name, prims) for name, _, prims in cargo]

allp = np.concatenate([p for _, prims in groups for p, *_ in prims])
mn, mx = allp.min(0), allp.max(0)
scale = 3.3 / (mx[0] - mn[0])
origin = np.array([(mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, mn[2]])

payload = bytearray(); views = []; accessors = []; meshes = []; nodes = []
def view(raw):
    while len(payload) % 4: payload.append(0)
    views.append({'buffer': 0, 'byteOffset': len(payload), 'byteLength': len(raw)}); payload.extend(raw)
    return len(views) - 1
def append(array, kind, component):
    a = {'bufferView': view(array.tobytes()), 'componentType': component, 'count': len(array), 'type': kind}
    if kind == 'VEC3': a.update(min=array.min(0).tolist(), max=array.max(0).tolist())
    accessors.append(a); return len(accessors) - 1
for name, prims in groups:
    pts = np.concatenate([(p - origin) * scale for p, *_ in prims])
    # Wheels hang at their centres so the game can rotate them in place.
    pivot = ((pts.min(0) + pts.max(0)) / 2 if 'wheel' in name
             else (np.array([fifth_wheel, 0, 0]) - origin) * scale * [1, 0, 0] if name == 'trailer' else np.zeros(3))
    primitives = []
    for p, n, i, uv, t in prims:
        pos = ((p - origin) * scale - pivot).astype('<f4')
        prim = {'attributes': {'POSITION': append(pos, 'VEC3', 5126), 'NORMAL': append(n.astype('<f4'), 'VEC3', 5126),
                               'TEXCOORD_0': append(uv.astype('<f4'), 'VEC2', 5126)},
                'indices': append(i.astype('<u4').reshape(-1, 1), 'SCALAR', 5125), 'mode': 4}
        if t is not None: prim['material'] = t
        primitives.append(prim)
    meshes.append({'name': name, 'primitives': primitives})
    nodes.append({'name': name, 'mesh': len(meshes) - 1, 'translation': pivot.tolist()})
images = [{'bufferView': view(png), 'mimeType': 'image/png'} for png in textures]
out = {'asset': {'version': '2.0', 'generator': 'ForesTycoon licensed log truck (not for redistribution)'}, 'scene': 0,
       'scenes': [{'nodes': list(range(len(nodes)))}], 'nodes': nodes, 'meshes': meshes,
       'materials': [{'name': f'atlas_{k}', 'pbrMetallicRoughness': {'baseColorTexture': {'index': k}, 'metallicFactor': 0, 'roughnessFactor': 1}}
                     for k in range(len(textures))],
       'textures': [{'source': k} for k in range(len(textures))], 'images': images,
       'buffers': [{'byteLength': len(payload)}], 'bufferViews': views, 'accessors': accessors}
j = json.dumps(out, separators=(',', ':')).encode(); j += b' ' * ((-len(j)) % 4); payload += b'\0' * ((-len(payload)) % 4)
glb = struct.pack('<III', 0x46546c67, 2, 12 + 8 + len(j) + 8 + len(payload)) + struct.pack('<II', len(j), 0x4e4f534a) + j + struct.pack('<II', len(payload), 0x004e4942) + payload
dest = Path('ForesTycoon/Assets/Licensed/log-truck-textured.glb'); dest.parent.mkdir(parents=True, exist_ok=True); dest.write_bytes(glb)
print(f'Licensed textured log truck: {len(nodes)} nodes ({len(cargo)} logs, {len(textures)} textures), '
      f'{sum(len(i) // 3 for _, prims in groups for _, _, i, _, _ in prims)} triangles, {len(glb)} bytes -> {dest}')
