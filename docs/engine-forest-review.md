# Motor- és erdőrenderelés áttekintése – 2026-10-03

A vizsgálat a fix lépéses szimulációra, a rendszerek futtatására, a renderpassok sorrendjére, az erdő GPU-cache-ére, a fokozatos geometriaépítésre és a fa/tönk geometriára terjedt ki. A már meglévő munkafabeli erdő-, textúra- és tönkmódosításokra épül.

## Megvalósított javítások

- A `Terrain.ForestPlacement.cs` külön kezeli a determinisztikus törzselhelyezést. A fa és a tönk ugyanabból a törzslistából készül, a ritka állományok egyetlen tartalék törzsét is beleértve. Az elhelyezés OpenGL nélkül tesztelhető.
- A `ForestTrunkProfile` az álló törzs és a kivágott tönk közös kúpos profilja. A tönk vágási magasságában a törzs tényleges sugara érvényesül, minimumsugár nélkül. A faj, méret, hely és elfordulás a kivágott fa látványállapotából származik.
- A `Terrain.ForestStumps.cs` a vágási laphoz és a kéreghez közös peremcsúcsokat használ. Megszűnik a ferde vágási lap és a vízszintes oldalfal közötti rés. Az elkorhadás süllyeszti a geometriát.
- Az újratelepített kis csemeték mellett az öreg tönkök megmaradhatnak; a nagyobb élő törzsek tényleges helyzete és mérete dönti el az eltakarásukat. Eltérő fafaj esetén nem feltételezünk azonos elhelyezési rácsot.
- A tölgy tagoltabb lombtömegeket, a nyír keskenyedő koronát kapott. A koronák továbbra is egyetlen zárt felületek; az új formák nem növelik a csúcsszámot.
- A közvetlen és a fokozatos erdőépítés közös `AppendTreeCrown` függvényt használ, így a méret, szín és fajkód nem térhet el a két útvonalon. A tönkök fokozatos építése egyenként ellenőrzi a CPU-időkeretet.
- A renderpassok azonos rétegen belül megőrzik a regisztráció sorrendjét. A korábbi `List.Sort` ezt nem garantálta. Hiba esetén is lezárul a renderpass teljesítménymérése.
- A `WorldSystemCollection` elutasítja a NaN és végtelen időlépést, mielőtt az bármely játékrendszerhez eljutna.

## Ellenőrzés

```powershell
dotnet test ForesTycoon.sln --no-restore
dotnet run --project ForesTycoon/ForesTycoon.csproj --no-build -- --forest-smoke-test
dotnet run --project ForesTycoon/ForesTycoon.csproj --no-build -- --capture-frame artifacts/forest-review
```

A geometriai regressziótesztek négy faj, több kor/méret és 64 csempe esetén ellenőrzik az elhelyezést és a tartalék törzset. A tönk tesztje a sugárilleszkedést, a közös peremet és a nem elfajuló háromszögeket is ellenőrzi. A koronatesztek determinisztikusságot, zártságot és kifelé mutató normálokat vizsgálnak. A képi ellenőrzéshez a `05-stumps-grid.png` és `06-species-closeup.png` készült.

## Fennmaradó, külön mérendő feladatok

1. A jelenlegi `DrawTrees` mindig Near részletességet választ. A régebbi architektúradokumentum LOD-leírása ezért nem a jelenlegi működés: a GL-próba is azonos csúcsszámot vár minden zoomnál. A távoli erdők optimalizálása külön vizuális és teljesítménymérést igényel.
2. Az erdő elhelyezése, a GPU-cache és a geometriarajzolás már külön fájlokban van, de továbbra is a `Terrain` részleges osztályához tartozik. Egy önálló erdőrenderelő következő lépése egy csak olvasható terepfelület-interfész; a szimulációban maradjanak a növekedési és kitermelési szabályok.
3. A fokozatos geometriaépítés 2 ms-os együttműködő CPU-kerete nem kemény felső határ: egy geometriai elem és a kész GPU-feltöltés nem szakítható meg. Nagy térképeknél külön fel kell mérni az indulási időt, a kitermelési csúcsokat és a GPU-memóriát, mielőtt instancing vagy streaming következik.
4. A mentés parancsvisszajátszásra épít. Hosszú játékhoz verziózott állapotellenőrző pontok és kompatibilitási mintamentések szükségesek. Ez az áttekintés nem vezet be új mentési formátumot.

Az elvégzett javítások az átnézett motor- és erdőútvonalakat erősítik; a teljes projekt kiadási minőségét külön teljesítmény-, kompatibilitási és tartós futási ellenőrzés igazolhatja.
