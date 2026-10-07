# Fa alakrendszer: szimuláció ↔ generátor

A szimuláció egy fát `TreeShapeSpec`-kel ír le; a generátor ebből hálót
(`DendroTreeGenerator.Generate(spec, lod)`) vagy fizikai alakmutatókat
(`TreeShapeModel.Measure(spec)`) ad vissza. Semmi nem függ GL-től.

## Bemenet: `TreeShapeSpec`

| Mező | Jelentés |
|---|---|
| `Phase` | 6 életfázis: csemete, suhángfa, fiatal, érett, öreg, elöregedő (`TreeLifePhases`, a régi 4 fokozatra `Coarse()` képez) |
| `Vigor` | életerő 0..1 (egészség × tartós stressz); sávjai: életerős, csökkent, hanyatló, haldokló |
| `Site` | fény, víz, szélkitettség + szélirány, **résirány/réserősség** (a szomszédos fák felől, `ForestTreeSites.Gap`) |
| `Leaves` | lombállapot: csupasz, rügyfakadás, teljes, őszi, lombhullás (`TreePhenology`, év-törttől és maggal determinisztikus; a lucfenyő örökzöld) |
| `Dead` | álló holtfa: szürke, korona nélkül |

A folytonos értékeket a `TreeShapeBands` sávokra bontja; `ShapeKey` csak sávhatár
átlépésekor változik, így a `Terrain` csak ekkor építi újra a csempe hálóját. A
fázis- és lombváltás időpontját a `NextStageYear` ütemezi.

## Váz és topológia (`TreeSkeleton`)

A DendroKit Weber–Penn fájából (fajonként Arbaro-formátumú készlettel, lásd [arbaro-parameter-sets.md](arbaro-parameter-sets.md)) teljes ágváz készül (törzs, vázágak, gallyak,
villásodások). Garantált (és `Validate()` ellenőrzi, tesztelve minden faj×fázis×fény×seed
kombinációra):

* minden gyerek ág első pontja a szülő egy csúcsa (a szülő tengelyébe beszúrt csúcs);
* **pipe-model** vastagság: egy ág keresztmetszete a belőle ágazó gallyak
  keresztmetszetének összege; gyerek nem lehet vastagabb a szülőnél az elágazásnál, a
  vastagság a csúcs felé sosem nő;
* a törzs sugara mellmagasságban pontosan a szimulált átmérő, felette a pipe-model
  szerint vékonyodik, alul gyökérfej (flare) és felszíni gyökerek;
* ugyanaz a váz szolgál nyárra és télre: csak a megjelenítés különbözik.

## Megjelenítés

* **Lombos**: törzs + gyökerek + a korona alól kilátszó vázágak + egyetlen zárt, gyors
  korona-felhő a *élő* levélpontokból.
* **Csupasz**: a teljes ágrendszer, vastagság szerinti sorrendben, LOD-költségkerettel
  (közel ≤1400, közepes ≤330, távol ≤90 háromszög az ágakra); a 0,45 px-nél vékonyabb
  gallyak ennél vastagabbra emelve látszanak.
* **Életerő**: a hanyatló fán a felső és külső ágak elhalnak (szürke, látható holtág), a
  korona csak az élő levelekből épül, elöregedéskor a csúcs is megrövidül.
* **Környezet**: sima, topológiát nem érintő vízszintes torzítás – lejtés a rés felé,
  korona-ferdítés (rés/szél felé nyúlik), szélzászlósodás.

## Kimenet a szimulációnak: `TreeShapeMetrics`

Törzstérfogat, teljes faanyag, alaktényező, koronaalap-magasság, élő koronasugár,
vetületi terület, koronatérfogat, koronaeltolódás, élő lomb aránya, elhalás, vázágak száma.
Ezek ugyanabból a vázból és vastagságokból jönnek, mint a megjelenített háló.
A jelenlegi növekedési modellt a metrikák még nem módosítják; a `ForestTree.Volume`
állandó 0,45-ös alaktényezője a `FormFactor`-ral váltható ki.

## Ellenőrzés

`ForesTycoon.Tests/TreeSkeletonTests.cs`, `TreeShapeTests.cs`. Képek:
`TREE_PREVIEW_DIR=out dotnet test --filter TreePreviewDump`, majd
`python3 tools/render_tree_dump.py kép.png out/*.json`.
