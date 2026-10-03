# Pine Tree

Author: **EmreAlkan** — https://sketchfab.com/Anadolubebesi

Source: https://sketchfab.com/3d-models/pine-tree-27ab66bf87fd47bda321efde1d7a6fae

License: **CC BY 4.0** — https://creativecommons.org/licenses/by/4.0/

The user supplied `pine_tree.glb`; author, title, source and license are recorded in its embedded asset metadata.
`pine-tree-original.glb` is an unchanged copy of the supplied file. SHA-256:
`A25B9EDEF38E149BCCC8B70CE48DB4F715B77BE71B36A59D3E0EDF1A983874F0`.
No geometry simplification, needle thinning, material recoloring or source-file modifications are applied.
Runtime applies scene transforms, Y-up to Z-up conversion, ground anchoring, rotation and uniform height scaling.
The original linear material factors are rendered as linear colors and converted to sRGB on output.

# Additional forest sources

The following files are unchanged copies of the user-supplied GLBs. Author and
license information is recorded in each file's embedded `asset.extras` metadata.

| Packaged file | Author | License | Source |
| --- | --- | --- | --- |
| `forest-pack-original.glb` | 99.Miles | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | https://sketchfab.com/3d-models/low-poly-forest-tree-pack-5ff5a51e74324845a4e4905f182dfb2b |
| `oak-trees-original.glb` | DJMaesen | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | https://sketchfab.com/3d-models/oak-trees-d841c3bcc5324daebee50f45619e05fc |
| `trees-low-poly-original.glb` | Igor_K. | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | https://sketchfab.com/3d-models/trees-low-poly-1d2dcca2ccb1496c85b7cc5789a2a261 |
| `birch-original.glb` | Larolei Low Poly | [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/) | https://sketchfab.com/3d-models/birch-tree-low-poly-3ef00a7105c54d37b3a0e528e1466b0b |

Runtime adaptations: select individual node groups; combine matching conifer
trunks and crowns; bake scene transforms and normals; ground and normalize each
tree; merge rigid primitives by material; use alpha cutouts for the supplied
solid foliage cards; derive flat fallback colours from the textures; apply
instance rotation and continuous height/crown scaling. No source triangles are
removed from selected groups. Other objects remain in the original files.

Birch display is opt-in and defaults off. This switch does not remove the
noncommercial license restriction on the packaged asset. Commercial releases
must omit the asset or obtain an appropriate license.

# EZ-Tree generated variants

Generator: Daniel Greenheck — https://github.com/dgreenheck/ez-tree,
revision `dcf309bd86bd521083d9c70f01f2de45fdc7c457`, MIT license.
The complete copyright/license notice is in `Generated/EZ-TREE-LICENSE.txt`.
Leaf images come from the same repository. Bark images are its ambientCG
textures, CC0 — https://docs.ambientcg.com/license/.

The generated geometry uses tuned Pine Medium/Oak Medium presets and fixed
seeds. Adaptations: changed branch/leaf budgets, independent LOD exports,
512px embedded PNG textures, JPEG-to-PNG bark conversion, custom GLB packaging,
runtime shared near-LOD grounding and instance size/rotation. Full generation
parameters are recorded in `Generated/manifest.json`.
