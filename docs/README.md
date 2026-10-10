# Documentation / Dokumentáció

[English project overview](../README.md) | [Magyar projektáttekintés](../README.hu.md)

## Current guides / Aktuális útmutatók

| Topic / Téma | Document / Dokumentum |
| --- | --- |
| Project structure / Projektfelépítés | [Architecture](architecture.md) |
| Engine systems / Motorrendszerek | [Engine architecture](engine-architecture.md) |
| Rule editor / Szabályeditor | [Editor guide](rule-editor.md) |
| Model import / Modellimport | [Import contract](model-import-contract.md), [animation](animated-models.md) |
| Local purchased assets / Helyi megvásárolt modellek | [Asset guide](logging-facility-assets.md) |
| Tree models / Famodellek | [Shape system](tree-shape-system.md), [generation literature](tree-generation-literature.md) |
| Forest and environment / Erdő és környezet | [Forest implementation](tree-individual-implementation.md), [environment coupling](environment-forest-coupling.md) |
| Weather / Időjárás | [Rain, storms and snow research](rain-storm-cloud-research.md) |
| Performance / Teljesítmény | [Large-world plan and gates](large-world-performance.md) |
| Licensing / Licenc | [Current status](../LICENSING.md), [deferred generator replacement](own-tree-generator-plan.md) |

## Reviews and plans / Review-k és tervek

[2026-10-10 engine review](engine-review-2026-10-10.md) and
[2026-09-13 engine review](engine-review-2026-09-13.md) describe the revisions reviewed
at those dates. Other `*-plan.md`, `*-design.md` and study documents record design
decisions and proposed work; their presence does not mean every item is implemented.

A dátumozott review-k az akkori revíziók ellenőrzését dokumentálják. A terv-, design-
és tanulmányfájlok döntéseket és tervezett munkát is tartalmaznak; nem helyettesítik
a jelenlegi forrás és CI állapotának ellenőrzését.

## Generated files / Generált fájlok

`artifacts/` contains local captures, exports, logs and validation reports. It is
ignored by Git. `bin/`, `obj/`, `TestResults/` and Python caches are also generated.
Source, game assets, third-party notices and this documentation remain in version control.

Az `artifacts/` helyi képeket, exportokat, naplókat és mérési riportokat tartalmaz;
nem kerül Gitbe. A `bin/`, `obj/`, `TestResults/` és Python-cache szintén generált.
A forrás, játékassetek, licencnyilatkozatok és dokumentáció verziózott fájlok.

From the repository root / A repó gyökeréből:

```powershell
./tools/clean-generated.ps1 -WhatIf
./tools/clean-generated.ps1
```

The cleanup removes diagnostic outputs older than two hours, preserving the local
GitHub runner, recent work, seasonal reference captures, the latest engine-validation
directory, performance reports and solid-crown validation logs. It does not remove
build directories used by an open game or IDE. Review retained evidence periodically.

A takarítás a két óránál régebbi diagnosztikai kimeneteket törli. Megőrzi a helyi
GitHub-runnert, a friss munkát, az évszakos referenciaképeket, az engine-validation
könyvtárat, a teljesítményriportokat és a tömör koronák ellenőrzési naplóit.
A nyitott játék vagy IDE buildmappáit nem törli. A megtartott bizonyítékokat időnként
érdemes újra áttekinteni.
