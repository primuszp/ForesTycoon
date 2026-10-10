# ForesTycoon licensing status

Updated: 2026-10-10. Maintainer: [primuszp](https://github.com/primuszp).

## Current status

This repository does **not** currently offer a single project-wide license. Existing third-party notices, licenses and rights remain applicable. Public availability of source code is not, by itself, permission to use independently authored project material in commercial products.

The proposed [PolyForm Noncommercial License 1.0.0](docs/licensing/PolyForm-Noncommercial-1.0.0.md) is an unmodified reference copy of the [official text](https://github.com/polyformproject/polyform-licenses/blob/1.0.0/PolyForm-Noncommercial-1.0.0.md). It is **a proposal, not a license grant for ForesTycoon**. No root `LICENSE` declaring the integrated game noncommercial has been added.

## Intended licensing model

The intended model is a source-available license for independently authored ForesTycoon code, allowing the purposes permitted by PolyForm Noncommercial while requiring separate permission for other commercial use. The license's own permitted-purpose definitions, including its specified organizational uses, must be read in full; this document does not narrow or replace them.

The rights holder can offer their own paid game and separate commercial permissions for material they own. This does not give the rights holder additional rights to third-party code or assets. A future paid binary release may use its own end-user terms, subject to all dependency and asset licenses. Donations to the official project would not grant commercial reuse rights.

## Confirmed compatibility blocker

The repository's [DendroKit notice](ForesTycoon.TreeModels/Generation/Dendro/NOTICE.md) records a C# port of the Arbaro generator, GPL version 2 with a version-2-or-later grant. Its sources are compiled directly into `ForesTycoon.TreeModels`; the game references that assembly. There is no separate executable boundary. [GPL text](ForesTycoon.TreeModels/Generation/Dendro/LICENSE.txt).

[Arbaro presets](ThirdParty/ArbaroPresets/README.md) and the derived presets embedded from `ForesTycoon.TreeModels/Architecture/Presets` are also recorded as GPL-licensed. Their license and notices must remain intact.

A noncommercial restriction is not compatible with the GPL distribution requirements for the combined program. Merely excluding the GPL directory from a root license, moving it to a DLL, or switching off its renderer does not resolve the linkage. See the GNU project's [GPL v2 FAQ on proprietary combinations](https://www.gnu.org/licenses/old-licenses/gpl-2.0-faq.en.html#GPLInProprietarySystem) and [commercial redistribution](https://www.gnu.org/licenses/old-licenses/gpl-2.0-faq.en.html#DoesTheGPLAllowMoney).

Before applying the proposed license to the integrated game, either:

1. Replace the GPL generator and GPL-derived parameter sets with independently authored or compatibly licensed implementations and data, preserving the approved tree models, seasonal behavior, detail and diorama appearance; or
2. Obtain sufficient separate permissions from **all relevant rights holders** for the incorporated code and derived data.

Keeping GPL distribution conditions would permit commercial redistribution and therefore would not satisfy the project's requested noncommercial reuse restriction. GPL does allow selling software; the blocker here is the requested restriction on recipients, rather than charging for the game itself.

## Third-party material and release checks

This is a list of confirmed items requiring separate treatment, not a completed inventory or a declaration that every remaining asset is commercially cleared.

| Material | Existing source of terms | Required treatment |
| --- | --- | --- |
| DendroKit / Arbaro generator | [Notice and GPL grant](ForesTycoon.TreeModels/Generation/Dendro/NOTICE.md) | Resolve the compatibility blocker above; retain notices |
| Arbaro reference and derived presets | [Preset notice](ThirdParty/ArbaroPresets/README.md) | Replace or obtain sufficient permission along with the generator |
| Imported tree candidates | [Asset evaluation and attribution](docs/tree-asset-candidates.md) | Check the actually distributed asset set and metadata; CC BY-NC assets require removal or additional permission for a paid release, even if disabled |
| EZ-Tree reference integration | [Integration sources and terms](docs/ez-tree-integration.md) | Preserve applicable MIT/CC0 notices and verify which generated assets are distributed |
| Purchased vehicle / facility assets | [Local asset policy](docs/logging-facility-assets.md) | Keep source model files out of the public repository unless their terms permit redistribution; verify rights for the paid binary |
| Other bundled GLB models | `ForesTycoon/Assets/Buildings`, `Assets/Vehicles`, `Assets/Wildlife` | Verify the source, attribution and redistribution rights of each actual release asset |
| NuGet and native dependencies | Package-specific licenses, including OpenTK and ImGui.NET | Preserve the required notices and review the exact packages shipped |

When the GPL blocker and release-asset inventory are resolved, introduce a root license with the correct copyright holder notice and clearly scoped third-party exceptions. Do not describe the current integrated game as already cleared for restricted commercial distribution.
