# Arbaro-alapú fajparaméter-készletek

## Kutatás

A generátor magja az Arbaro (Wolfram Diestel, Java, GPL v2) Weber–Penn-implementációjának
C#-portja (DendroKit). Az Arbaro 18 kész fát hoz (`trees/*.xml`, egy-egy ~3 KB); az
eredeti készletek: <https://github.com/wdiestel/arbaro/tree/master/trees>. Ezekből
letöltöttem a tűlevelűeket és lombos fákat (`tamarack`, `european_larch`, `quaking_aspen`,
`ca_black_oak`, `black_tupelo`, `sassafras`, `weeping_willow`, `eastern_cottonwood`,
`lombardy_poplar`), és a `ThirdParty/ArbaroPresets` alatt változtatás nélkül mellékeltem.

A korábbi saját paraméterek és az Arbaro-készletek eltérései magyarázzák a szögletes,
„hiányos” alakot:

| Paraméter | Korábbi kód | Arbaro-készletek |
|---|---|---|
| 1. szintű ágak | 11–22 | 25–75 |
| 2. szintű ágak | 6–9 | 25–120 |
| `CurveRes` (szegmens/ág) | 3–5 | 8–20 (törzs 8–20, vázág 8–17) |
| `CurveV` (görbületi szórás) | 20–120 | 40–300 |
| `Flare`, `Lobes` | – | 0,3–1,2; 3–9 karéj |
| `DownAngleV` | néhol | mindenütt, −10 … −50 (az ág szöge a törzs mentén változik) |
| `RotateV`, `LengthV`, `SegSplits` | többnyire 0 | változatosság és villásodás |

Ezek adják az Arbaro-fák természetes hajlását és szabálytalanságát; a kevés ág és
háromszegmenses görbület szabályos, merev vázat adott.

## Megvalósítás

* `ForesTycoon/Assets/Trees/*.xml`: négy faj saját készlete **Arbaro-formátumban** (beágyazott
  erőforrás), a megfelelő Arbaro-fából levezetve:
  `picea_abies` ← `tamarack`/`european_larch`, `quercus_robur` ← `ca_black_oak`,
  `betula_pendula` ← `quaking_aspen`, `fagus_sylvatica` ← `black_tupelo`. Az Arbaróban
  megnyitva összevethetők és finomhangolhatók, a játék újrafordítással átveszi.
* `TreeArchitecture.PresetParameters`: a kifejlett, jó fényű alapkészletet az életfázis és a
  fény szerint módosítja (ágszám, szög, ághossz, villásodás, `AttractionUp`, `BaseSize` a
  koronahányadból, csemetének 2 szint).
* A gazdag készlet költségét a `TreeSkeleton` fogja meg: a harmadrendű és annál finomabb ágakat
  elhagyja, a vázágakat (≤70) és gallyakat (≤200) egyenletesen ritkítja, az ágtengelyeket
  Douglas–Peucker módszerrel egyszerűsíti (a gyerekágak csúcsait megtartva), majd a vastagságot
  a pipe-modellel újraszámolja. Egy váz generálása átlag ~12 ms (legfeljebb ~40 ms), kb. 22 KB;
  fajonként/fázisonként/fénysávonként 16 változat gyorsítótárazódik.
* `TreeArchitecture.SkeletonFromXml`: tetszőleges Arbaro-XML betöltése (eszközökhöz, teszthez).

## Ellenőrzés és hangolás

* `ArbaroPresetTests`: minden mellékelt és saját XML érvényes vázat ad (topológia, pipe-azonosság).
* Képek: `ARBARO_DIR=<xml mappa> ARBARO_SET="picea_abies:Spruce,quercus_robur:Oak"
  TREE_PREVIEW_DIR=out dotnet test --filter DumpArbaroReference`, majd
  `python3 tools/render_tree_dump.py kép.png out/ref-*.json`.
* Hangolás: az XML-ben a faj jellegzetes értékei – `1DownAngle`/`1Curve` (ágtartás),
  `1CurveV`/`2CurveV` (szabálytalanság), `1Branches` (sűrűség), `0SegSplits` (törzsvillásodás),
  `Shape` (burokforma). Az életfázis- és fényhatás a `PresetParameters` kapcsolóiban van.
