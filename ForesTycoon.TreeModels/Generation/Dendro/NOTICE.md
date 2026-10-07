# DendroKit.Core

The `Params`, `Tree`, `Mesh` and `Transformation` sources were
copied from `C:\Projects\DendroKit\source\DendroKit.Core` on 2026-10-07.
They implement DendroKit's C# port of the Arbaro 1.9.9 Weber & Penn recursive tree
generator. The WPF frontend, OBJ exporter, presets and binaries are not included.
The algorithm is preserved. On 2026-10-07 the sources were integrated into
ForesTycoon.TreeModels under Generation/Dendro; namespaces and base-library
imports were adapted to the host project. There is no separate DendroKit assembly.

The source project's README declares GNU GPL version 2 and includes a
version-2-or-later grant. The full GNU GPL v2 text is included in `LICENSE.txt`.
Arbaro's source headers credit Wolfram Diestel (2002). DendroKit's README contains
a placeholder author credit, so no additional author name has been invented.
Retain this notice, source headers and license when distributing this code.

The skeleton adapter is in `ForesTycoon.TreeModels/Architecture`; the connected
low-poly crown and wood geometry are in `ForesTycoon.TreeModels/Meshing`. `Leaves=0` disables individual leaf generation in the copied core.

The Arbaro tree parameter files in `ThirdParty/ArbaroPresets` are copied unchanged from the Arbaro
project (https://github.com/wdiestel/arbaro, GNU GPL v2, Wolfram Diestel). The species sets in
`ForesTycoon.TreeModels/Architecture/Presets` are derived from them and are distributed under the same license.
