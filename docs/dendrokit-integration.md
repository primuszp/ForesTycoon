# DendroKit alapú, kis poligonszámú növényzet

> A váz, életfázisok, lombhullás, életerő és környezet leírása: [tree-shape-system.md](tree-shape-system.md). Az alábbi szöveg a korábbi állapotot írja le részleteiben.

Az alapértelmezett **Eljárásos fák (kor és fény)** mód a DendroKit.Core
Weber–Penn generátort használja. A .NET 8 forrás, forrásmegjelölés és GPL v2
licenc a `ForesTycoon.TreeModels/Generation/Dendro` könyvtárban van. A forrás közvetlenül a `ForesTycoon.TreeModels` részeként fordul;
nincs külön DendroKit assembly vagy projektfüggőség. A játék buildjéhez nem
kell a helyi DendroKit projekt vagy WPF.

## Levélpontokból tömör korona

1. A DendroKit elkészíti a fajra, életfázisra és fénysávra jellemző ágrendszert
   és a levelek helyét. A fajparaméterek botanikai hátterét a
   [fagenerálási irodalmi áttekintés](tree-generation-literature.md) írja le.
   Levélháló nem készül; legfeljebb 160 levélpont marad mintának.
2. Gyűrűnként és irányonként a levélpontok **szögablakos radiális támasza** adja a
   sugarat. (A vetületek maximuma a konvex burkot adná; a szögablak a vázágak közti
   hézagokat karéjként megtartja.) A fajprofil (legszélesebb pont helye, alj és
   tető laposága) súlyozottan keveredik ezzel: a tölgy mélyen karéjos, a bükk sima.
3. Egyetlen zárt, összefüggő, szabályos topológiájú koronaháló készül. A lucnál
   gyűrűpárok (lelógó szoknya + keskeny váll) adják az örvemeleteket.
4. A törzs kevés oldalú csövekből épül; a látható vázágak száma fajfüggő, a
   képernyőn 0,6 px-nél vékonyabb ágak elmaradnak.

A lomb teljesen tömör, a szín- és árnyékpasszban sincs alfa-kivágás vagy
lombtextúra-mintavétel.

A korona oldalszáma a képernyőtérbeli sziluetthibából adódik (≤ 1 px a LOD finom
végén), ezért a kis fák kevesebb, a nagyok több háromszöget kapnak:

| Részletesség | Korona háromszögei | Érett élő fa összesen |
|---|---:|---:|
| Közel | 30–192 | ≤ 320 |
| Közepes | 30–108 | ≤ 130 |
| Távol | 16–20 | 16–20 |

## Fajok, kor, fény és cserjék

A lucfenyő, nyír, tölgy és bükk csemete, fiatal, középkorú és idős alakot kap.
A fizikai magasság, törzsátmérő és koronaméret a szimulációból származik.
A `ForestTree.Resources.Light` három fénysávban módosítja az ágrendszert;
kevés fényben a korona keskenyebb és magasabban kezdődik. Ez alakmodell,
nem irányfüggő fototropizmus. A havi növekedés továbbra is GPU-skálázás,
a fény- és életfázisváltás meglévő, időkeretes geometriacserét használ.

A `DendroTreeGenerator.GenerateShrub` két cserjeformát ismer (`ShrubForm`):
a mogyoró 5–8 tőhajtásos, felfelé szélesedő váza, a galagonya rövid, villás
törzsű, sűrű, gömbölyded bokor. Közelről legfeljebb ~170 háromszög. A cserje
jelenleg generátor- és előnézeti funkció, nem új ültethető szimulációs faj.

A normalizált ágvázak és levélpontok szálbiztos cache-ben vannak, amely minden
lehetséges kulcsot (fák és cserjék, 1728 bejegyzés, egyenként ~5 KB) megtart. Faj/életfázis/fénysáv/növekedési forma kombinációnként
32 seedváltozat használható; minden LOD ugyanabból az ágvázból készül.
Az egyed teljes seedje továbbra is módosítja a koronadudorokat és színt,
mérete és elfordulása különbözhet. A cache sem GL-erőforrást, sem teljes
DendroKit fát nem tart meg, csak pozíciókat és ritka törzsszakaszokat.

## Mérés és ellenőrzés

```powershell
dotnet test ForesTycoon.sln
dotnet run --project ForesTycoon -- --vegetation-preview
dotnet run --project ForesTycoon -- --vegetation-benchmark
dotnet run --project ForesTycoon -- --dendro-tree-preview
dotnet run --project ForesTycoon -- --dendro-tree-smoke-test
dotnet run --project ForesTycoon -- --forest-smoke-test
```

A `vegetation-preview` az `artifacts/vegetation-benchmark/vegetation.png`
kontaktlapon soronként lucot, tölgyet, nyírt, bükköt és cserjéket mutat
(csemete, fiatal, érett, idős, érett árnyékban), és kiírja LOD-onként a
háromszögszámokat. A `dendro-tree-preview`
négy faj × négy életfázis képeit készíti el azonos méretben, képenként 12%,
48% és 100% fényellátással.

2026-10-07, Intel UHD Graphics, Debug build, azonos 960×640-es kamera és
1304 fás jelenet, árnyékokkal, időjárás/köd nélkül, `GL.Finish` méréssel:

| Mérőszám | Előtte | Optimalizálás után |
|---|---:|---:|
| Átlagos közeli háromszög/fa, 128 minta | 1341 | 190 |
| Jelenet előkészítése | 3987 ms | 1011 ms |
| Medián képkockaidő | 42,3 ms | 11,5 ms |
| 95. percentilis képkockaidő | 43,3 ms | 11,8 ms |
| 128 fa ismételt generálása | 194 ms | 10 ms |

Ez rögzített renderelési teszt, nem a teljes játék minden térképére ígért
képkockasebesség. A JSON riportok az `artifacts/vegetation-benchmark` mappában
vannak. A tesztek a zárt éleket, összefüggést, normálokat, méretkorlátot,
determináltságot, fényválaszt, cserjéket, tömör szín/mélység/árnyékfedést és
a helyi, illetve késleltetett erdő-cache frissítéseket ellenőrzik.
