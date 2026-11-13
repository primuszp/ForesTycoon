# Memória- és teljesítményvizsgálat — 2026-11-13

## Eredmény és hatókör

A vizsgált kezelt világobjektumoknál nem mutatkozott memóriaszivárgás. Negyven
azonos mentésbetöltés során 200 lecserélt térkép-, erdő-, logisztika-, szabálykönyv-
és fajkontextus-objektum mind felszabadult. A világ lezárása után mind a 204
figyelt objektum elérhetetlenné vált. Az ötödik és negyvenedik betöltés között
a teljes GC utáni kezelt memória 94,31–94,77 MB között maradt; lezárás után
72,54 MB volt. Ez a folyamat közös infrastruktúráját is tartalmazza.

A hosszú tesztben növekvő parancsnapló és mentés élő adat, ezért annak növekedése
önmagában nem szivárgás. A rögzített, 3 790 244 bájtos mentés ismételt betöltése
különíti el ezt az objektumok véletlen megtartásától. A vizsgálat nem igazolja
az összes natív OpenGL/OpenAL-erőforrás vagy más platform szivárgásmentességét.

## Javítások és mérés

Az allokációs nyomkövetés a járműkinematika RK2-részlépéseinek ideiglenes
tömbjeit, a fordulási sugár LINQ-iterátorát és a logisztikai szabályazonosítók
ismételt stringépítését emelte ki. Ezek helyett korlátozott méretű, részlépések
között újrahasznált stack-munkaterület, közvetlen ciklus és kanonikus azonosítók
szerepelnek. A publikált járműállapot továbbra is független tömb.

A fajkontextus hatóköre értéktípus lett; az AsyncLocal-burkolók gyenge kulcsú
ConditionalWeakTable-ben újrahasználhatók. A kivételek utáni visszaállítást,
a háttérfeladatok kontextusöröklését és a régi fajkönyvek felszabadulását
regressziós teszt ellenőrzi.

Azonos 16×16-os, 42-es seedű világban, 300 bemelegítő és 3×3000 mért tickkel:

| Mérési menet | Korábbi allokáció (bájt) | Végleges allokáció (bájt) |
| --- | ---: | ---: |
| 1 | 150 765 688 | 74 248 864 |
| 2 | 100 192 776 | 60 690 320 |
| 3 | 93 294 152 | 58 233 456 |
| Összesen | 344 252 616 | 193 172 640 |

Ez **43,9% allokációcsökkenés** ebben a terhelésben. A futásidőt a JIT és a
gép egyéb terhelése érdemben befolyásolta; általános FPS- vagy gyorsulási
garancia ebből nem következik. A kinematikai regresszió 2000 részlépésre is
legfeljebb 256 bájt allokációt enged, és ellenőrzi a korábbi állapotok sértetlenségét.

A kinematika/szabályazonosító optimalizálás előtti és utáni 9300 tick teljes
checkpointjának SHA-256 értéke azonos:
`A0EB674CE9B151BBF37D6DF3AB3444F345B4A2FB353858EF79088C5A7D0C3DB7`.

A mérés a `dotnet-trace` GC-allokációs eseményeit is használta; a mintavételezett
méretek becslések, a fenti táblázat a `GC.GetTotalAllocatedBytes` számlálójából
származik. A thread-time mintavétel nem azonos a tiszta CPU-idővel.
Eszközleírás: [Microsoft dotnet-trace dokumentáció](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace).

## Lezárt ellenőrzések

- Release solution build: 0 hiba, 0 figyelmeztetés.
- Teljes tesztkészlet: **1969/1969 sikeres**.
- 16×16 világ, 120 hónap / 10 év: 270 000 tick világonként,
  120 újratöltés, 600 figyelt lecserélt objektumból 0 megtartott.
- 32×32 világ, 24 hónap / 2 év: 54 000 tick világonként,
  24 újratöltés, 120 figyelt lecserélt objektumból 0 megtartott.
- Összesen 648 000 világfrissítés; minden hónaphatáron pontosan azonos
  teljes, szeletelt és visszatöltött szimulációs állapot.

A tízéves kis térképen a faállomány elfogyott; ez a terhelési forgatókönyv
eredménye, nem az ökológiai modell tudományos validációja.

## Újrafuttatás

```sh
dotnet build ForesTycoon.sln -c Release
dotnet test ForesTycoon.Tests -c Release --no-build --blame-hang-timeout 180s
dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --memory-performance-probe artifacts/memory-performance/after 40
dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --simulation-soak 120 16 42 artifacts/memory-performance/soak-16
dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --simulation-soak 24 32 7 artifacts/memory-performance/soak-32
```

A `tools/verify-engine.ps1 -Stress` a memóriapróbát is futtatja.
A helyi mérési bizonyítékok az ignorált `artifacts/memory-performance/`
könyvtárban vannak: `before.log`, `after.log`, `full.trx`, `full-tests.log`,
`soak-16.log`, `soak-32.log`, `before.nettrace`, `tick-allocations-before.txt`.
Környezet: macOS, Release net8.0 célkeretrendszer, .NET 10.0.15 roll-forward.
