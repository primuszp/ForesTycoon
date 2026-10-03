# EZ-Tree integration

EZ-Tree was inspected and checked out at
`dcf309bd86bd521083d9c70f01f2de45fdc7c457` from
https://github.com/dgreenheck/ez-tree. It is a JavaScript/Three.js tree generator,
not a C# simulation engine. ForesTycoon uses it as an offline asset authoring
tool; the game has no Node, browser or Three.js runtime dependency.

The source checkout/dependencies live under ignored `artifacts/ez-tree-source`.
The reproducible exporter is `tools/ez-tree/generate.mjs`. Twelve generated GLBs
are committed under `ForesTycoon/Assets/Forest/Generated`, with a manifest of
source revision, seed, full tree options, detail parameters and triangle counts.
Source generation is deterministic: all twelve GLBs matched byte-for-byte in
two separate output directories. The exporter refuses an unreviewed revision.

## Usage in the game

**Graphics → Tree models → Generated trees (EZ-Tree)** selects this additional
preset. It replaces spruce and oak visuals with two seed-selected variants of
each. Beech retains the supplied generic broadleaf pair. The optional imported
birch setting is unchanged. Existing imported models, original high-detail pine
and procedural trees remain selectable and their sources are preserved.

| Generated prototype | Near | Medium | Far |
| --- | ---: | ---: | ---: |
| Pine (two seeds) | 7,896 triangles | 3,614 | 1,314 |
| Oak (two seeds) | 4,416 triangles | 2,118 | 694 |

Pine uses a tuned Pine Medium preset; it is a conifer prototype, not a certified
botanically exact Norway spruce. Oak uses tuned Oak Medium. The presets reduce
branch tessellation/recursion while retaining leaf-card density. Current foliage
and branching remain subjects for visual tuning; these are alternative models,
not a forced replacement for the previously selected forest pack.

All LODs are meshed from the same skeleton using `createGeometry(detail)`.
Separate files prevent accidentally drawing all LODs from one exported scene.
The C# library uses the near model's grounding/normalization for every LOD;
otherwise larger distant leaves would change the whole tree's height or root
position. Existing orthographic zoom thresholds and hysteresis choose the LOD,
consistently in main and shadow passes. Selection currently switches between
LODs; crossfading is not implemented.

Each model uses two material groups, embedded non-interlaced 512px PNGs, source
leaf alpha cutoffs and repeat UVs. JPEG bark is converted to PNG offline so the
existing GLB loader can read it. Static geometry is shared by all instances of a
variant/LOD. Average texture colour supplies the flat display modes.

Height/crown size come from the game's continuously evaluated tree dimensions;
the generator does not run per frame or implement biological aging. Native bole
proportions may still differ from the physical diameter used by the stump.
Drying, dead crowns and falling trees are not introduced by this integration.
The generator's Three.js wind shader is not embedded in GLB or ported here.
GPU instancing remains useful future work for large visible tree populations.

## Regeneration

Requires Node, npm, Python, Pillow and Git. Run from the repository root:

```powershell
git clone https://github.com/dgreenheck/ez-tree.git artifacts/ez-tree-source
git -C artifacts/ez-tree-source checkout dcf309bd86bd521083d9c70f01f2de45fdc7c457
npm --prefix artifacts/ez-tree-source ci --ignore-scripts --no-audit --no-fund
node tools/ez-tree/generate.mjs artifacts/ez-tree-source artifacts/ez-tree-generated
```

If the checkout already exists, reuse it instead of cloning over it. Review
generated meshes and all LODs before copying the twelve GLBs and manifest into
`Assets/Forest/Generated`. The exporter bundles the generator with esbuild,
generates geometry headlessly and writes a standards-based GLB directly; no
browser DOM/canvas emulation is needed. Only scripts in this repository run;
dependency install hooks are disabled.

Validation: `dotnet test --no-restore`, `--forest-model-smoke-test`,
`--material-alpha-smoke-test`, and `--tree-growth-smoke-test`. The forest model
fixture verified decreasing generated geometry submission at near/medium/far
zoom: 152,730 / 113,250 / 90,906 indices, including other trees, terrain and
shadow passes. These figures are not an FPS benchmark.

The active Visual Studio/game process locked the normal Debug output, so latest
validation used a separate build output:

```powershell
dotnet build --no-restore -p:BaseOutputPath=D:/Repositories/Primusz/ForesTycoon/artifacts/model-validation/bin/ -p:UseAppHost=false
dotnet artifacts/model-validation/bin/Debug/net8.0/ForesTycoon.dll --forest-model-smoke-test
```

Stop the running game, rebuild normally and restart to see the new selector.

## Attribution

EZ-Tree: Daniel Greenheck, copyright 2024,
[MIT license](https://github.com/dgreenheck/ez-tree/blob/main/LICENSE). Its notice
is retained in `Assets/Forest/Generated/EZ-TREE-LICENSE.txt` and copied to output.
Leaf images are from the same MIT-licensed repository. Bark images are the
repository's ambientCG sets (selected through `src/app/textures.js`), available
under [CC0](https://docs.ambientcg.com/license/). No source app music, fonts or
other scene assets are included. Adaptations include preset tuning, seed choice,
LOD meshing, resized PNG textures and custom GLB packaging.
