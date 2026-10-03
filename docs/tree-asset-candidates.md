# Tree asset review — 2026-10-03

The supplied GLBs were inspected without modifying the originals. The selected
low-poly variants remain available as the imported forest preset. The native procedural generator is now the default. Counts below are indexed triangles per individual asset
group, not counts inferred from file size. CPU previews are in
`artifacts/tree-asset-review/comparison.png`; separate `*-engine.png` captures
use the actual GLB loader and OpenGL material renderer. These are visual checks,
not forest FPS benchmarks. The inspection is repeatable with
`python tools/review_tree_assets.py D:/Personal/Downloads` (numpy and Pillow).

| File | Individual triangles / primitives | Assessment |
| --- | --- | --- |
| Original `pine_tree.glb` | 437,500 / 12 | Preserved unchanged and selectable in Graphics; lightweight spruce variants remain selectable. |
| `trees_low_poly.glb` | tree4: 3,519 / 2; tree6: 1,862 / 2 | Best general broadleaf candidate: dense crowns, few draw calls, embedded PNGs. Species is not identified by metadata; do not label these spruce or birch. Leaves use BLEND. |
| `oak_trees.glb` | small: 3,783 / 25; medium: 2,951 / 59; large: 7,741 / 114 | Good oak candidates with three distinct forms and naturally sparse crowns. Merge rigid leaf primitives by material before instanced forest use; preserve node transforms. Leaves use MASK, cutoff 0.654154. |
| `low_poly_forest_tree_pack.glb` | Entire pack: 3,747 / 30 | Contains 13 background atlas tree meshes, rocks, separate branch and trunk meshes. Useful background/LOD source and conifer construction parts; the entire pack is not one ready-made 3D individual tree. Needs selection, assembly and checks from multiple viewing angles. Leaves/atlas use BLEND. |
| `oak_tree.glb` | 106,832 / 3 | Visually detailed but too expensive as the main forest asset. Also contains an embedded JPEG, which the current loader does not support. Offline preview only. |
| `birch_tree_low_poly.glb` | living: 10,616 / 6; bare: 3,280 / 4 | Distinctive white birch bark; paired bare crown is useful for dead-tree states. The healthy crown is sparse. Embedded metadata specifies CC BY-NC 4.0. Do not select it for commercial distribution without a suitable grant from the author. No textures required. |

The new broadleaf candidates have about 56–235 times fewer triangles per tree
than the current pine. This is a geometry ratio, **not a measured FPS gain**:
draw calls, pixel overdraw, shadow passes and visibility still affect cost.

## Integration status

`ImportedForestModels` extracts three assembled 3D conifers (630/624/624
triangles), three oaks and two broadleaf variants. Each tree is grounded and
normalized independently. Source node transforms and normals are baked before
material merging; all conifers and oaks draw as two rigid material groups.
Unused image maps are discarded from the extracted models. Seed-based variant
selection stays fixed as trees age. Height and crown width grow continuously;
flat fallback colours retain their texture's average colour when textures are off.
The generic broadleaf pair represents beech visually; the source does not claim
botanical species accuracy.

Graphics offers **new models**, **new broadleaves + original pine**, and
**procedural trees**. Birch is separately enabled (off by default, CC BY-NC).
Changing presets rebuilds affected chunk geometry without editing simulation
data. Harvest removes the imported tree and leaves the existing physical-diameter
stump. Native imported trunk proportions can differ from that physical diameter;
matching the imported bole exactly to the cut profile remains separate work.

All originals remain packaged unchanged. The noncommercial birch must be
excluded or separately licensed for commercial distributions, even when its
display switch is off. The detailed standalone oak was not installed because of
its geometry cost and JPEG requirement. Pack rocks/background atlas objects are
preserved in the source asset but excluded from living-tree instances.

Integration validation: 192 tests; `--forest-model-smoke-test` checks all presets,
birch, flat colours, shadow rendering and harvesting; `--tree-growth-smoke-test`
checks continuous growth and save replay. On the same 33-node forest smoke
fixture, submitted indices fell from 318,038,016 to 2,552,796. These counts are
geometry submission metrics, not an FPS improvement measurement.

## Material implementation

The GLB loader preserves OPAQUE, MASK, BLEND and alphaCutoff. Base texture alpha
is multiplied by the material factor's alpha. OPAQUE ignores alpha; MASK writes
depth only where alpha meets the cutoff; BLEND draws after solid/cutout meshes,
with depth testing, no depth writes, and source-alpha compositing. Blended
primitives are ordered back to front within each model by projected centre.
Blend equations/factors, blend enable and depth write state are restored.

Coverage remains active when colour textures or enhanced graphics are disabled,
and applies before contour drawing and in the depth shadow pass. MASK uses its
specified cutoff. BLEND shadows use a 0.5 cutout approximation; fractional
transparent shadow transmission is not implemented. Source-material rendering
now uses the textured base colour as well as the original untextured factor.

PNG decoding supports non-interlaced RGB/RGBA, grayscale/grayscale-alpha and
indexed palettes, including tRNS transparency and packed 1/2/4-bit palette or
grayscale pixels. This is necessary for the supplied packages' non-colour maps
as well as potentially transparent palette textures. JPEG, 16-bit and interlaced
PNG remain unsupported. Normal/roughness maps are not currently shaded.

BLEND ordering is not global between separate tree instances, and triangles
within a single primitive are not sorted. For large forests, leaf-card alpha
cutouts are preferable where the source image represents solid leaves with
empty background. Verify conversion thresholds visually instead of blindly
changing every BLEND material to MASK. Instancing, LOD and per-material merging
remain useful future performance work. The default pine geometry has now been
replaced with the lightweight assembled variants; original-pine mode remains expensive.

## Validation

`dotnet test --no-restore` checks alpha metadata and PNG alpha decoding alongside
existing simulation/rendering tests. `--material-alpha-smoke-test` measures real
framebuffer colour/depth for OPAQUE, MASK, BLEND, layer order, quality switches,
state restoration and shadow cutouts. `--wildlife-smoke-test` validates the
existing animated asset renderer. The pack, both low-poly broadleaves, all three
oaks and the birch load and render with `--tree-asset-preview <absolute-glb-path>`.

## Recorded provenance

License values below come from each GLB's `asset.extras`; retain the author's
credit, source and license when importing selected assets into production.

| Asset | Author | Metadata license | Source |
| --- | --- | --- | --- |
| Low Poly Forest Tree Pack | 99.Miles | CC BY 4.0 | https://sketchfab.com/3d-models/low-poly-forest-tree-pack-5ff5a51e74324845a4e4905f182dfb2b |
| Trees Low Poly | Igor_K. | CC BY 4.0 | https://sketchfab.com/3d-models/trees-low-poly-1d2dcca2ccb1496c85b7cc5789a2a261 |
| oak trees | DJMaesen | CC BY 4.0 | https://sketchfab.com/3d-models/oak-trees-d841c3bcc5324daebee50f45619e05fc |
| Oak Tree | BazukaliKartal | CC BY 4.0 | https://sketchfab.com/3d-models/oak-tree-caf081828df84d7cbb07b4107991facc |
| Birch Tree (Low Poly) | Larolei Low Poly | CC BY-NC 4.0 | https://sketchfab.com/3d-models/birch-tree-low-poly-3ef00a7105c54d37b3a0e528e1466b0b |

The NC restriction is stated in the [official license summary](https://creativecommons.org/licenses/by-nc/4.0/).
