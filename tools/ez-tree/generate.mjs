// Offline EZ-Tree -> embedded PNG GLB export. No browser or JS game runtime.
// node tools/ez-tree/generate.mjs artifacts/ez-tree-source <output-directory>
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';

const source = path.resolve(process.argv[2] ?? 'artifacts/ez-tree-source');
const output = path.resolve(process.argv[3] ?? 'artifacts/ez-tree-generated');
const revision = execFileSync('git', ['-C', source, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const supportedRevision = 'dcf309bd86bd521083d9c70f01f2de45fdc7c457';
if (revision !== supportedRevision) throw new Error(`Check out EZ-Tree ${supportedRevision} before regenerating assets.`);
const { build } = await import(pathToFileURL(path.join(source, 'node_modules/esbuild/lib/main.js')));
await build({ entryPoints: [path.join(source, 'src/lib/index.js')], bundle: true,
  format: 'esm', external: ['three'], outfile: path.join(source, 'build/ez-tree.es.js') });
const { Tree } = await import(pathToFileURL(path.join(source, 'build/ez-tree.es.js')));
fs.mkdirSync(output, { recursive: true });

const details = {
  near: {},
  medium: { sectionStride: 2, segmentFactor: .75, leafStride: 2, leafScale: 1.25 },
  far: { sectionStride: 4, segmentFactor: .5, leafStride: 4, leafScale: 1.8, billboard: 'single' }
};
const manifest = { generator: 'EZ-Tree', revision, license: 'MIT', models: [] };
const textureCache = new Map();
function png(filename) {
  if (!textureCache.has(filename)) {
    const bytes = execFileSync('python', ['-c',
      'from PIL import Image; import sys,io; im=Image.open(sys.argv[1]).convert("RGBA"); im.thumbnail((512,512)); b=io.BytesIO(); im.save(b,format="PNG"); sys.stdout.buffer.write(b.getvalue())', filename],
      { maxBuffer: 8 * 1024 * 1024 });
    textureCache.set(filename, bytes);
  }
  return textureCache.get(filename);
}

function exportGlb(tree, geometry, filename, metadata) {
  const parts = [], views = [], accessors = [];
  let length = 0;
  function append(bytes, target) {
    const padding = (4 - length % 4) % 4;
    if (padding) { parts.push(Buffer.alloc(padding)); length += padding; }
    const id = views.length;
    views.push({ buffer: 0, byteOffset: length, byteLength: bytes.length, ...(target ? { target } : {}) });
    parts.push(bytes); length += bytes.length;
    return id;
  }
  function attribute(array, size, type, indexed = false) {
    const typed = indexed ? new Uint32Array(array) : new Float32Array(array);
    const id = accessors.length;
    const accessor = { bufferView: append(Buffer.from(typed.buffer), indexed ? 34963 : 34962),
      componentType: indexed ? 5125 : 5126, count: typed.length / size, type };
    if (type === 'VEC3' && !indexed) {
      accessor.min = Array.from({ length: 3 }, (_, axis) => {
        let value = Infinity; for (let i = axis; i < typed.length; i += 3) value = Math.min(value, typed[i]); return value;
      });
      accessor.max = Array.from({ length: 3 }, (_, axis) => {
        let value = -Infinity; for (let i = axis; i < typed.length; i += 3) value = Math.max(value, typed[i]); return value;
      });
    }
    accessors.push(accessor); return id;
  }
  const meshes = [geometry.branches, geometry.leaves].map((g, material) => ({
    primitives: [{ attributes: {
      POSITION: attribute(g.attributes.position.array, 3, 'VEC3'),
      NORMAL: attribute(g.attributes.normal.array, 3, 'VEC3'),
      TEXCOORD_0: attribute(g.attributes.uv.array, 2, 'VEC2') },
      indices: attribute(g.index.array, 1, 'SCALAR', true), material }]
  }));
  const texturesRoot = path.join(source, 'src/app/public/textures');
  const bark = tree.options.bark.type;
  const images = [png(path.join(texturesRoot, 'bark', `${bark}_1K-JPG`, `${bark}_1K-JPG_Color.jpg`)),
    png(path.join(texturesRoot, 'leaves', `${tree.options.leaves.type}.png`))]
    .map(bytes => ({ bufferView: append(bytes), mimeType: 'image/png' }));
  const tint = [tree.branchesMesh.material.color, tree.leavesMesh.material.color];
  const materials = tint.map((color, i) => ({ name: i ? 'leaves' : 'bark',
    doubleSided: true, alphaMode: i ? 'MASK' : 'OPAQUE', ...(i ? { alphaCutoff: tree.options.leaves.alphaTest } : {}),
    pbrMetallicRoughness: { baseColorFactor: [color.r, color.g, color.b, 1],
      baseColorTexture: { index: i }, metallicFactor: 0, roughnessFactor: 1 } }));
  const json = { asset: { version: '2.0', generator: `EZ-Tree ${revision}`, extras: metadata },
    scene: 0, scenes: [{ nodes: [0] }], nodes: [{ name: 'Tree', children: [1, 2] },
      { name: 'branches', mesh: 0 }, { name: 'leaves', mesh: 1 }], meshes, materials, images,
    textures: [{ source: 0, sampler: 0 }, { source: 1, sampler: 0 }],
    samplers: [{ wrapS: 10497, wrapT: 10497, minFilter: 9987, magFilter: 9729 }],
    bufferViews: views, accessors, buffers: [{ byteLength: length }] };
  const encoded = Buffer.from(JSON.stringify(json));
  const jsonChunk = Buffer.alloc((encoded.length + 3) & ~3, 32); encoded.copy(jsonChunk);
  const bin = Buffer.concat(parts); const binaryChunk = Buffer.alloc((bin.length + 3) & ~3); bin.copy(binaryChunk);
  const header = Buffer.alloc(20); header.writeUInt32LE(0x46546c67); header.writeUInt32LE(2, 4);
  header.writeUInt32LE(28 + jsonChunk.length + binaryChunk.length, 8);
  header.writeUInt32LE(jsonChunk.length, 12); header.writeUInt32LE(0x4e4f534a, 16);
  const binHeader = Buffer.alloc(8); binHeader.writeUInt32LE(binaryChunk.length); binHeader.writeUInt32LE(0x004e4942, 4);
  fs.writeFileSync(filename, Buffer.concat([header, jsonChunk, binHeader, binaryChunk]));
}

for (const species of ['pine', 'oak']) for (const variant of [1, 2]) {
  const tree = new Tree(); tree.loadPreset(species === 'pine' ? 'Pine Medium' : 'Oak Medium');
  tree.options.seed = 4100 + variant;
  tree.options.branch.sections = { 0: 10, 1: 6, 2: 4, 3: 3 };
  tree.options.branch.segments = { 0: 6, 1: 4, 2: 3, 3: 3 };
  if (species === 'pine') {
    tree.options.branch.children[0] = 54;
    tree.options.leaves.count = 24; tree.options.leaves.size *= 1.15;
  } else {
    tree.options.branch.levels = 2;
    tree.options.branch.children[0] = 5; tree.options.branch.children[1] = 4;
    tree.options.leaves.count = 24; tree.options.leaves.size *= 1.5;
    tree.options.leaves.start = .2;
  }
  tree.generate();
  for (const [lod, detail] of Object.entries(details)) {
    const geometry = tree.createGeometry(detail);
    const filename = `ez-${species}-${String(variant).padStart(2, '0')}-${lod}.glb`;
    const triangles = (geometry.branches.index.count + geometry.leaves.index.count) / 3;
    const metadata = { source: 'https://github.com/dgreenheck/ez-tree', revision, seed: tree.options.seed,
      preset: species === 'pine' ? 'Pine Medium' : 'Oak Medium', detail, author: 'Daniel Greenheck (EZ-Tree)',
      license: 'MIT (EZ-Tree); ambientCG bark CC0', options: tree.options,
      adaptation: 'Reduced branch/leaf budgets; embedded 512px PNG; separate LOD files' };
    exportGlb(tree, geometry, path.join(output, filename), metadata);
    manifest.models.push({ filename, triangles, ...metadata });
    geometry.branches.dispose(); geometry.leaves.dispose();
    console.log(`${filename}: ${triangles} triangles`);
  }
  tree.branchesMesh.geometry.dispose(); tree.branchesMesh.material.dispose();
  tree.leavesMesh.geometry.dispose(); tree.leavesMesh.material.dispose();
}
fs.writeFileSync(path.join(output, 'manifest.json'), JSON.stringify(manifest, null, 2));
fs.copyFileSync(path.join(source, 'LICENSE'), path.join(output, 'EZ-TREE-LICENSE.txt'));
