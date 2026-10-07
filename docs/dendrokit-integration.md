# DendroKit alapú, kis poligonszámú növényzet

Az alapértelmezett **Eljárásos fák (kor és fény)** mód a DendroKit.Core
Weber–Penn generátort használja. A .NET 8 forrás, forrásmegjelölés és GPL v2
licenc a `ThirdParty/DendroKit.Core` könyvtárban van. A játék buildjéhez nem
kell a helyi DendroKit projekt vagy WPF.

## Levélpontokból tömör korona

1. A DendroKit elkészíti a fajra, életfázisra és fénysávra jellemző ágrendszert
   és a levelek helyét (`Leaves=8`, `LeafBend=0`). Levélháló nem készül.
2. A levélpontok magasság és irány szerinti támaszértékei alakítják a korona
   sugarát. A fajprofil megőrzi a lombos vagy kúpos sziluettet; a fenyőnél
   enyhe emeletek, a lombosoknál eltérő dudorok jelennek meg.
3. Egyetlen zárt, összefüggő, szabályos topológiájú koronaháló készül.
   Ez simított alakillesztés, nem minden levélpontot szigorúan tartalmazó
   konvex burok. Így nem követ apró, drága levélrészleteket.
4. A törzs kevés oldalú csövekből épül. Legfeljebb hat főág rövid, látható
   elágazása marad meg; a belső gallyak hálója teljesen elmarad.

A lomb teljesen tömör, a szín- és árnyékpasszban sincs alfa-kivágás vagy
lombtextúra-mintavétel. Az egyed színe, a részben simított lapnormálok és a
megvilágítás adják a felület változatosságát. A LOD-váltás meglévő képernyőtérbeli
átmenete továbbra is működik; ez nem a korona anyagának áttetszősége.

| Részletesség | Korona háromszögei | Élő fa maximuma |
|---|---:|---:|
| Közel | 120 | 196 |
| Közepes | 64 | 98 |
| Távol | 20 | 20 |

Távol csak a korona rajzolódik. Az elhalt fák megtartják a ritka ágvázat;
ugyanazt a geometriát használják minden LOD-ban a dőlési animációhoz.

## Fajok, kor, fény és cserjék

A lucfenyő, nyír, tölgy és bükk csemete, fiatal, középkorú és idős alakot kap.
A fizikai magasság, törzsátmérő és koronaméret a szimulációból származik.
A `ForestTree.Resources.Light` három fénysávban módosítja az ágrendszert;
kevés fényben a korona keskenyebb és magasabban kezdődik. Ez alakmodell,
nem irányfüggő fototropizmus. A havi növekedés továbbra is GPU-skálázás,
a fény- és életfázisváltás meglévő, időkeretes geometriacserét használ.

A `DendroTreeGenerator.GenerateShrub` külön háromtörzsű, alacsonyan kezdődő
koronájú cserjeprofilt készít, legfeljebb 276 közeli háromszöggel. A hívó
alacsony magasságot és nagyobb szélesség/magasság arányt ad meg. A cserje
jelenleg generátor- és előnézeti funkció, nem új ültethető szimulációs faj.

A normalizált ágvázak és levélpontok legfeljebb 256 bejegyzéses, szálbiztos
FIFO cache-ben vannak. Faj/életfázis/fénysáv/növekedési forma kombinációnként
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

A `vegetation-preview` balról jobbra tölgyet, lucot és cserjét mutat az
`artifacts/vegetation-benchmark/vegetation.png` képen. A `dendro-tree-preview`
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
