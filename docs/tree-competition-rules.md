# Fák fény- és térversenye a projekt gráfjaiból

A Szabályok közös listájában hat új számítási gráf szerkeszthető. Nem kell képletet írni: a források, műveletek, kapcsolatok és a kimenet is cserélhetők. A konstansok számmal és csúszkával állíthatók.

| Gráf | Az alapgráf feladata |
| --- | --- |
| Szomszéd árnyékoló hatása (`forest.shading`) | A koronák közelsége, a szomszéd magassági fölénye és egészsége adja az árnyékterhelést. |
| Szomszéd térfoglaló hatása (`forest.crowding`) | A koronák közelsége és méretaránya adja a koronaterhelést. |
| A fához jutó fény (`forest.light`) | Az összegzett árnyékterhelés csökkenti a környezeti besugárzást. |
| A fa szabad növekedési tere (`forest.space`) | Az összegzett koronaterhelés csökkenti a szabad teret. |
| Fafaj fényválasza (`forest.lightResponse`) | A fafaj árnyéktűrése alakítja a fény növekedési hatását. |
| A fa erőforráskorlátja (`forest.resourceLimit`) | Alapból a szabad tér, víz és faj szerinti fényválasz közül a legkisebb érvényesül. Ez is más összesítésre cserélhető. |

Például a szomszéd egészsége lekapcsolható az árnyékolásról, a koronasugár helyett törzsátmérő köthető be, vagy a fény helyett a tér kaphat erősebb hatást az erőforráskorlátban. A játék a szerkesztett gráfot futtatja; a régi képlet nem számol mellette.

## Objektum és mező választása

A forráscsomópontnál először objektumot, majd tulajdonságot kell választani. A fapár gráfjai a **Vizsgált fa**, **Szomszéd fa** és **Fapár** mezőit kínálják; az összegző gráfok **Verseny**, **Környezet** és **Erőforrások** mezőket olvasnak. A végső válaszokhoz a **Fafaj** árnyéktűrése is elérhető.

Az angol kódnevek megmaradnak (`neighbor.height`, `pair.distance`, `competition.crownLoad`, `resources.light`, `species.shadeTolerance`), a megjelenítési neveket és leírásokat az editor az attribútumokkal jelölt objektummodellből fedezi fel. A nézet, csomópont- és kapcsolatkezelés a meglévő közös számításigráf-szerkesztő. Nincs párhuzamos fény-/térverseny-szerkesztő vagy külön képletbeviteli panel. A gráf kimenete állandóra is cserélhető; az alapgráf visszaállítása szerkeszthető gráfot ad vissza, natív számítás nélkül.

## A mechanika és a szabály elválasztása

A `ForestCompetition` már csak:

1. közös, változatlan geometriapillanatképet készít;
2. a meglévő két szomszédsági gyűrű élő fáiból, stabil sorrendben jelölteket gyűjt;
3. a vizsgált fa saját azonosítóját kihagyja;
4. a gráfok szomszédonkénti terhelését sorrendtartóan összeadja;
5. az összegző gráfokat az aktuális vízzel és besugárzással futtatja.

A két gyűrű a futtató jelöltgyűjteményének jelenlegi határa. Ezen belül nincs beégetett koronatávolság szerinti levágás: egy saját gráf távolabbi, nem összeérő koronájú jelöltet is figyelembe vehet. A tényleges hatótávot és a csillapítást a gráf szerzője állítja össze a távolságból és méretekből. Az alapgráf távolsági feltétele és annak kihagyó ága is látható csomópont: saját forrással vagy más feltétellel helyettesíthető.

Az átfedés négyzetes lefutása, magassági dominancia, korona-méretarány, árnyék és szabad tér válasza, árnyéktűrési hatvány és minimumkorlát az alapgráfok csomópontjai. A korábbi C# számítások törölve. A régi `waterAllocated` elágazás és az önálló gyökérverseny-büntetés is megszűnt: a vízválaszt minden esetben a közös vízrendszer adja.

A `ForestResources.LightResponse` és `Limitation` már csak a megfelelő projektgráfhoz továbbítja az adatokat. Ugyanazt a programot olvassa a növekedés, egészség/stressz, a faobjektum szerkesztőmezője és az erdődiagnózis. A `ForestCompetitionRules` kizárólag induló gráfokat hoz létre; a mentett projekt a szerkesztett csomópontokat tartalmazza. A tiszta, legfeljebb 256 csomópontos gráfokat a közös `CompiledBehaviorModel` futtatja.

## Szeletelés és havi előkészítés

A pillanatkép készítése fánként, a verseny kiértékelése jelölt fapáronként is megszakítható. A normál játékútvonal nem hív szinkron `Evaluate` vagy `SnapshotPatch` befejezést. A havi előkészítés és az esetleges hónaphatári pótszámítás is ezeken az iterátorokon halad; nincs korlátlan, az összes párt egyetlen hívásban befejező ág.

Az előkészítés **árnyék- és koronaterhelést** tárol, kész fényérték helyett. A hónap végén a valódi besugárzással és vízzel újra lefut a fény-/térgráf. Ez szükséges például akkor, ha a szerző a besugárzás négyzetét választja fényválasznak: az előkészítés nem helyettesítheti ezt egy utólagos natív szorzással.

Egy gráf által módosított egészség, méret vagy saját növekedési ráta jelzi a környék versenypillanatképének és előkészítésének érvénytelenítését. Az egészségi és méretváltozásokat ugyanaz a szeletelt helyi erőforrás- és rátafrissítés követi, mint az ültetést és kitermelést. A növekedési gráf saját rátaírása nem kér önmagára új frissítést.

A mentés továbbra is a logikai művelethatárra vár. A pillanatkép a fafajt is megőrzi, az előkészítés pedig a terheléseket és az eredeti feldolgozási sorrendet menti. Mentett, részben előkészített hónap folytatása és naplóból visszajátszás is tesztelt. A futtató verziója `forestycoon-simulation/2026-10-16.7`.

A szállított és archivált jelenet új, külön könyvtárba rögzített referenciájában csak az Ecology és Behaviors lenyomat változott. A jelenetek küldetései és szerkesztett pályaadatai megmaradtak. A helyi projekt előző változata az `artifacts/competition-project-backup/project.before-competition.json` biztonsági másolatban van; a helyi kezdeti állapot előkészítése egyszer újrainicializálva lett az új adatformára. Nincs régi versenyszámítást vagy régi előkészítési erőforrást futás közben átvezető betöltési ág.

## Ellenőrzés és mérés

A célzott tesztek ellenőrzik a távoli jelölt saját forrásgráfját, a pillanatkép változatlanságát, a fapáronkénti megszakítást, saját nemlineáris fénygráf mellett az előkészített és közvetlen hónap egyezését, a játékmezők közös fény-/korlátválaszát, a gráfból írt egészség utáni érvénytelenítést, valamint a hónap közben mentett terhelések folytatását és naplóból visszajátszását. A régi natív képlet másolatát tartalmazó levágási referenciateszt helyett a közös gráf végrehajtási útjait hasonlítjuk össze.

Az editorfüstteszt mind a hat új gráfot megnyitja, képet készít, és a külön editorablakot is ellenőrzi. Nagypályás CPU-mérés: `dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --competition-benchmark` (64×64 és 256×256, erősen erdősített pálya, eső és hó, erdőhónap-határral). A diagnosztika a teljes tick és a 64 munkadarabos szeletek ideje mellett a teljes állapot és erdő-ujjlenyomat pontos egyezését is ellenőrzi. A mért munka nem teljes kirajzolt képkockaidő.

### Végső eredmények – 2026-10-16

A teljes csomag **1841/1841** tesztje átment. A végső gráfos távolsági feltétel, fánkénti pillanatkép és üres függőségek szeletelése után **66/66** célzott verseny-, motor-, katalógus-, mentési és visszajátszási teszt is sikeres. A Release editor buildje 0 figyelmeztetéssel és 0 hibával készült. A gráf- és különablak-füstteszt átment; a helyi projekt 79 folyamatgráfja érvényes.

Release, .NET 8.0.32, `DOTNET_TieredCompilation=0`, 60 bemelegítő és 330 mért tick. A nagy pálya szándékosan sűrű (247 238 fa). A teljes tick és a 64 munkadarabos hívás ideje CPU-munka, memóriafoglalással és szemétgyűjtéssel együtt; nem a kirajzolt képkocka ideje.

| Pálya | Időjárás | Fa | Szelet p95 | Szelet p99 | Szelet maximum | Teljes tick maximum | Pontos állapotegyezés |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| 64×64 | Eső | 15 797 | 0,042 ms | 0,054 ms | 1,909 ms | 641,226 ms | igen |
| 64×64 | Hó | 15 797 | 0,033 ms | 0,049 ms | 1,504 ms | 614,747 ms | igen |
| 256×256 | Eső | 247 238 | 0,045 ms | 0,054 ms | 17,506 ms | 9606,365 ms | igen |
| 256×256 | Hó | 247 238 | 0,034 ms | 0,050 ms | 6,396 ms | 9546,834 ms | igen |

A natív, koronasugárra szabott levágás elhagyása és a szabadon összeállítható párgráfok növelik az összes CPU-munkát. Az alapgráfban látható és szerkeszthető távolsági feltétel lusta ága kihagyja a drágább számítást, ha a saját feltétele nem teljesül; a motor nem tesz hozzá másik hatótávkorlátot. A szeletelés a munkát felbontja, önmagában nem csökkenti annak teljes költségét. A teljes tick egyben történő befejezése csak a diagnosztikai összehasonlító oldal; az interaktív játékút szeletelten folytatja.

Mind a négy esetben pontosan azonos ellenőrzőpont és víz-/úthálózati ujjlenyomat született. A ritka 17,506 ms-os kiugrást a mérés nem bontja szét szemétgyűjtésre, operációs rendszerre és egyedi munkadarabra. A szeletelés nem garantál maximális képkockaidőt, a ritka tüskék további profilozást indokolhatnak.
