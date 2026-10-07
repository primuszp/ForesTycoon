#!/usr/bin/env python3
"""Software-render tree mesh dumps (see ForesTycoon.Tests/TreePreviewDump.cs) to PNG.

usage: render_tree_dump.py out.png dump1.json [dump2.json ...] [--view side|top|iso] [--rows]
Each dump is one row; trees inside a dump are laid out left to right.
"""
import json, struct, sys, zlib
import numpy as np

W_PER_TREE, H_ROW = 260, 360

def png(path, img):
    h, w, _ = img.shape
    raw = b''.join(b'\x00' + img[y].tobytes() for y in range(h))
    def chunk(t, d): return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    open(path, 'wb').write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
                           + chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b''))

def project(p, view):
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    if view == 'side':   # look along +y
        return np.stack([x, z, y], -1)
    if view == 'top':
        return np.stack([x, y, z], -1)
    # iso: rotate 35 deg about x, 30 deg about z
    c, s = np.cos(np.radians(30)), np.sin(np.radians(30))
    x2, y2 = x * c - y * s, x * s + y * c
    t = np.radians(55); ct, st = np.cos(t), np.sin(t)
    return np.stack([x2, z * ct + y2 * st, -(y2 * ct - z * st)], -1)

def raster(img, zbuf, tris, view, scale, ox, oy, dx, shade_light=(0.4, -0.5, 0.75)):
    L = np.array(shade_light); L = L / np.linalg.norm(L)
    h, w, _ = img.shape
    for t in tris:
        a = np.array(t[0:9]).reshape(3, 3); col = np.array(t[9:12], float) / 255
        a = a + np.array([dx, 0, 0])
        n = np.cross(a[1] - a[0], a[2] - a[0]); ln = np.linalg.norm(n)
        if ln < 1e-12: continue
        n /= ln
        shade = 0.45 + 0.55 * max(0.0, float(n @ L)) if view != 'side' else 0.45 + 0.55 * max(0.0, float(n @ L))
        c = np.clip(col * shade * 255, 0, 255).astype(np.uint8)
        q = project(a, view)
        px = q[:, 0] * scale + ox; py = oy - q[:, 1] * scale; pz = q[:, 2]
        x0, x1 = int(max(0, np.floor(px.min()))), int(min(w - 1, np.ceil(px.max())))
        y0, y1 = int(max(0, np.floor(py.min()))), int(min(h - 1, np.ceil(py.max())))
        if x1 < x0 or y1 < y0: continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (py[1] - py[2]) * (px[0] - px[2]) + (px[2] - px[1]) * (py[0] - py[2])
        if abs(d) < 1e-9:
            # sub-pixel sliver: still draw its centre pixel so thin twigs stay visible
            cx, cy = int(px.mean()), int(py.mean())
            if 0 <= cx < w and 0 <= cy < h and pz.mean() < zbuf[cy, cx]:
                zbuf[cy, cx] = pz.mean(); img[cy, cx] = c
            continue
        l1 = ((py[1] - py[2]) * (xs - px[2]) + (px[2] - px[1]) * (ys - py[2])) / d
        l2 = ((py[2] - py[0]) * (xs - px[2]) + (px[0] - px[2]) * (ys - py[2])) / d
        l3 = 1 - l1 - l2
        eps = -0.02
        m = (l1 >= eps) & (l2 >= eps) & (l3 >= eps)
        z = l1 * pz[0] + l2 * pz[1] + l3 * pz[2]
        sub = zbuf[y0:y1 + 1, x0:x1 + 1]
        m &= z < sub
        sub[m] = z[m]
        img[y0:y1 + 1, x0:x1 + 1][m] = c

def main():
    args = [a for a in sys.argv[1:]]
    view = 'side'
    if '--view' in args:
        i = args.index('--view'); view = args[i + 1]; del args[i:i + 2]
    out, files = args[0], args[1:]
    dumps = [json.load(open(f)) for f in files]
    n = max(len(d['trees']) for d in dumps)
    # common scale from tallest tree
    maxh = 0
    for d in dumps:
        for t in d['trees']:
            for p in t['parts']:
                for tr in p['tris']:
                    maxh = max(maxh, tr[2], tr[5], tr[8])
    scale = (H_ROW - 40) / max(maxh, 1e-3)
    if view == 'top': scale = (H_ROW - 40) / 12
    img = np.full((H_ROW * len(dumps), W_PER_TREE * n, 3), (44, 54, 64), np.uint8)
    for r, d in enumerate(dumps):
        sub = img[r * H_ROW:(r + 1) * H_ROW]
        zb = np.full(sub.shape[:2], 1e9)
        for k, t in enumerate(d['trees']):
            ox = W_PER_TREE * k + W_PER_TREE / 2 - t['dx'] * scale
            # ground line
            sub[int(H_ROW - 20):int(H_ROW - 19), W_PER_TREE * k:W_PER_TREE * (k + 1)] = (90, 100, 80)
            for p in sorted(t['parts'], key=lambda p: p['name'] != 'wood'):
                raster(sub, zb, p['tris'], view, scale, ox, H_ROW - 20 if view != 'top' else H_ROW / 2, t['dx'])
    png(out, img)
    print('wrote', out, img.shape)

main()
