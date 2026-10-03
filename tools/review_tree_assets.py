"""Read-only GLB inventory and orthographic asset previews (not an FPS benchmark).

Usage: python tools/review_tree_assets.py D:/Personal/Downloads
Requires numpy and Pillow. Previews use base colour, flat lighting and alpha
cutouts; they intentionally do not reproduce the game's renderer or shadows.
"""
import io
import json
from pathlib import Path
import struct
import sys

import numpy as np
from PIL import Image, ImageDraw


def load(path):
    data = path.read_bytes()
    document = None
    binary = None
    offset = 12
    while offset < len(data):
        size, kind = struct.unpack_from('<II', data, offset)
        chunk = data[offset + 8:offset + 8 + size]
        if kind == 0x4E4F534A:
            document = json.loads(chunk)
        elif kind == 0x004E4942:
            binary = chunk
        offset += size + 8
    if document is None or binary is None:
        raise ValueError('Expected embedded GLB JSON and binary chunks')
    return document, binary


def accessor(g, binary, index):
    a = g['accessors'][index]
    if 'sparse' in a:
        raise ValueError('Sparse accessors are not supported by this review tool')
    view = g['bufferViews'][a['bufferView']]
    width = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
    dtype = np.dtype({5120: 'i1', 5121: 'u1', 5122: '<i2', 5123: '<u2', 5125: '<u4', 5126: '<f4'}[a['componentType']])
    stride = view.get('byteStride', width * dtype.itemsize)
    return np.ndarray((a['count'], width), dtype=dtype, buffer=binary,
                      offset=view.get('byteOffset', 0) + a.get('byteOffset', 0),
                      strides=(stride, dtype.itemsize)).copy()


def transform(node):
    if 'matrix' in node:
        return np.array(node['matrix']).reshape((4, 4), order='F')
    x, y, z, w = node.get('rotation', [0, 0, 0, 1])
    rotation = np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                         [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                         [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])
    result = np.eye(4)
    result[:3, :3] = rotation @ np.diag(node.get('scale', [1, 1, 1]))
    result[:3, 3] = node.get('translation', [0, 0, 0])
    return result


def collect(g, binary, selected=None):
    result = []
    def walk(index, parent, included):
        node = g['nodes'][index]
        world = parent @ transform(node)
        included = included or index == selected
        if included and 'mesh' in node:
            for primitive in g['meshes'][node['mesh']]['primitives']:
                if primitive.get('mode', 4) != 4:
                    continue
                positions = accessor(g, binary, primitive['attributes']['POSITION'])
                positions = np.c_[positions, np.ones(len(positions))] @ world.T
                indices = accessor(g, binary, primitive['indices']).reshape(-1, 3)
                uv_index = primitive['attributes'].get('TEXCOORD_0')
                uv = accessor(g, binary, uv_index) if uv_index is not None else np.zeros((len(positions), 2))
                result.append((positions[:, :3], indices, uv, primitive.get('material')))
        for child in node.get('children', []):
            walk(child, world, included)
    for root in g['scenes'][g.get('scene', 0)]['nodes']:
        walk(root, np.eye(4), selected is None)
    return result


def preview(g, binary, primitives, title, destination):
    size = 640
    all_points = np.concatenate([p[0] for p in primitives])
    centre = (all_points.min(axis=0) + all_points.max(axis=0)) / 2
    yaw, elevation = .55, .12
    right = np.array([np.cos(yaw), 0, -np.sin(yaw)])
    forward = np.array([np.sin(yaw)*np.cos(elevation), -np.sin(elevation), np.cos(yaw)*np.cos(elevation)])
    up = np.cross(forward, right)
    camera = np.array([right, up, forward]).T
    projected = (all_points - centre) @ camera
    scale = (size - 90) / max(np.ptp(projected[:, 0]), np.ptp(projected[:, 1]))
    output = np.full((size, size, 3), [231, 235, 238], dtype=np.uint8)
    depth = np.full((size, size), -np.inf)
    textures = {}
    for i, image in enumerate(g.get('images', [])):
        view = g['bufferViews'][image['bufferView']]
        start = view.get('byteOffset', 0)
        textures[i] = np.asarray(Image.open(io.BytesIO(binary[start:start+view['byteLength']])).convert('RGBA')) / 255.
    light = np.array([-.4, .8, .5]); light /= np.linalg.norm(light)
    for positions, indices, uv, material_index in primitives:
        material = g.get('materials', [])[material_index] if material_index is not None else {}
        pbr = material.get('pbrMetallicRoughness', {})
        colour = np.array(pbr.get('baseColorFactor', [1, 1, 1, 1]), dtype=float)
        texture = None
        if 'baseColorTexture' in pbr:
            texture = textures[g['textures'][pbr['baseColorTexture']['index']]['source']]
        points = (positions - centre) @ camera
        points[:, 0] = points[:, 0] * scale + size/2
        points[:, 1] = size/2 - points[:, 1] * scale + 12
        for face in indices:
            a, b, c = points[face]
            xmin, xmax = max(0, int(np.floor(min(a[0], b[0], c[0])))), min(size-1, int(np.ceil(max(a[0], b[0], c[0]))))
            ymin, ymax = max(32, int(np.floor(min(a[1], b[1], c[1])))), min(size-1, int(np.ceil(max(a[1], b[1], c[1]))))
            denominator = (b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
            if abs(denominator) < 1e-8 or xmin > xmax or ymin > ymax:
                continue
            yy, xx = np.mgrid[ymin:ymax+1, xmin:xmax+1] + .5
            u = ((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1])) / denominator
            v = ((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1])) / denominator
            w = 1-u-v
            z = u*a[2]+v*b[2]+w*c[2]
            target_depth = depth[ymin:ymax+1, xmin:xmax+1]
            mask = (u >= 0) & (v >= 0) & (w >= 0) & (z > target_depth)
            if not np.any(mask):
                continue
            rgba = np.broadcast_to(colour, (*u.shape, 4)).copy()
            if texture is not None:
                coords = u[..., None]*uv[face[0]] + v[..., None]*uv[face[1]] + w[..., None]*uv[face[2]]
                tx = (np.mod(coords[..., 0], 1)*texture.shape[1]).astype(int)
                ty = (np.mod(coords[..., 1], 1)*texture.shape[0]).astype(int)
                rgba *= texture[ty, tx]
            if material.get('alphaMode', 'OPAQUE') != 'OPAQUE':
                mask &= rgba[..., 3] >= material.get('alphaCutoff', .5)
            normal = np.cross(positions[face[1]]-positions[face[0]], positions[face[2]]-positions[face[0]])
            normal /= max(np.linalg.norm(normal), 1e-10)
            shading = .65 + .35*abs(np.dot(normal, light))
            # Texture base colour is sRGB; factors are linear in glTF.
            rgb = rgba[..., :3] if texture is not None else np.power(np.maximum(rgba[..., :3], 0), 1/2.2)
            output[ymin:ymax+1, xmin:xmax+1][mask] = np.clip(rgb[mask]*shading*255, 0, 255).astype(np.uint8)
            target_depth[mask] = z[mask]
    image = Image.fromarray(output)
    ImageDraw.Draw(image).text((12, 10), title, fill=(30, 35, 40))
    image.save(destination)


def main():
    source = Path(sys.argv[1])
    destination = Path('artifacts/tree-asset-review')
    destination.mkdir(parents=True, exist_ok=True)
    selections = {'low_poly_forest_tree_pack': [('pack', None)],
                  'trees_low_poly': [('tree4', 3), ('tree6', 6)],
                  'oak_trees': [('small', 3), ('medium', 53), ('large', 171)],
                  'oak_tree': [('tree', None)],
                  'birch_tree_low_poly': [('living', 2), ('bare', 9)]}
    inventory = []
    previews = []
    for name, groups in selections.items():
        path = source / (name + '.glb')
        g, binary = load(path)
        entry = {'file': str(path), 'bytes': path.stat().st_size, 'asset': g['asset'], 'groups': []}
        for label, node in groups:
            primitives = collect(g, binary, node)
            triangles = sum(len(p[1]) for p in primitives)
            entry['groups'].append({'name': label, 'triangles': triangles, 'primitives': len(primitives)})
            output = destination / f'{name}-{label}.png'
            preview(g, binary, primitives, f'{name} / {label} / {triangles:,} triangles', output)
            previews.append(output)
            print(name, label, triangles, len(primitives), flush=True)
        inventory.append(entry)
    (destination / 'inventory.json').write_text(json.dumps(inventory, indent=2), encoding='utf-8')
    sheet = Image.new('RGB', (640*3, 640*3), (231, 235, 238))
    for i, path in enumerate(previews):
        with Image.open(path) as image:
            sheet.paste(image, ((i % 3)*640, (i // 3)*640))
    sheet.save(destination / 'comparison.png')


if __name__ == '__main__':
    main()
