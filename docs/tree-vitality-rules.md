# Fák egészsége, stressze és elhalása

A játék alapértelmezett faéletciklusát a projekt négy, képlet nélkül szerkeszthető
szabálya adja. A Szabályok lapon mindegyik ugyanazt a forrás → kapcsolat → cél
szerkesztőt használja. A gráf adatából fordított program fut a játékban;
ugyanennek a programnak az előnézete látható a kiválasztott fán.

| Gráf | Alapértelmezett működés |
| --- | --- |
| `forest.health` | A termőhelyi alkalmasság és a legszűkösebb erőforrás adja az egészség célértékét. Az egészség évente legfeljebb 0,42 értékkel közelít hozzá, túllépés nélkül. |
| `forest.stress` | Erőforráshiány vagy 0,18 alatti egészség évente 1 stresszévet ad. A két ok egyszerre sem ad dupla növekedést. Jó körülmények között évente 0,5 stresszév oldódik. |
| `forest.mortality` | Legalább 4 stresszév és elégtelen egészség, vagy a faj legnagyobb korának túllépése holtfává alakítja a fát. |
| `forest.deadwood` | Az elhalt fa 8 erdőév után eltűnik. Ez a kivágáskor keletkező tönktől külön objektum. |

Az erőforráshiány küszöbe az árnyéktűrés függvényében 0,5-ről 0,25-re,
az elhalási egészségküszöb 0,65-ről 0,4-re csökken. Mindkettő rendes,
szerkeszthető feltételgörbe. A tervező átírhatja a görbét, másik forrást,
más feltételt vagy teljesen más kapcsolatokat választhat. A számok
csúszkával és számbillentyűzettel is megadhatók. A megszűnt
`forest.health` natív viselkedéskötésnek nincs párhuzamos szerkesztője vagy
futási tartalékága.

A felfedezett `tree` objektum új olvasható tulajdonságai: `space`,
`lightResponse`, `resourceLimit`, `fitness`, `maximumAge`; művelete `die`.
A `deadTree` objektum `deadYears`, `treeId`, `diameter` és `tile` adatot,
valamint `remove` műveletet kínál. A kódnevek angolok; az objektum- és
mezőválasztó érthető magyar megnevezéseket mutat. A meglévő `health` és
`stressYears` írható állapot. A megszűnt natív döntéseket nem pótolja
betöltéskori szabályátvezetés.

## Futás és állapothatárok

A havi erdőlépés lezárja a méretinterpolációt és közös versengési
pillanatképből előkészíti az erőforrásokat. Ezután a közös RuleEngine
futtatja az engedélyezett `month` szabályokat a projekt sorrendjében.
Végül a túlélő fák növekedése az új egészségből indul tovább, frissülnek
a szomszédos erőforrások és a statisztikák. Az elhalt fa megtartja az
azonosítóját és méretét, nem keletkezik kitermelt faanyag vagy tönk.

A hatókör előkészítése, a faegyedenkénti kiértékelés, az elhalás és az
utólagos frissítések megszakítható munkaszeletek. A faegyedek csempénként
fordított tárolási sorrendben következnek, így az eltávolítás nem tolja el
a még feldolgozandó egyedek indexét. Minden következő szabály újrakészíti
a hatókört. Nincs teljes hónapot befejező, költségkorlát nélküli futási ág.

A logikai tick csak a havi munka befejezésekor zárul. A mentés és a
projektcsere erre a határra vár; részleges hónapból nem készül pillanatkép.
Teljes mentés betöltése megszakítja a korábbi folytatást és annak havi
időfelülírását is. Az eltelt havi idő önálló adat, ezért több hónap egyetlen
bemeneti időlépésben is azonos eredményt ad. Az időmérés nem befolyásolja
a determinisztikus feldolgozási sorrendet.

A geometriai versengés, termőhelyi lekérdezés, növekedés és újulat továbbra
is natív; ezek nem számolják újra az egészséget, stresszt vagy elhalást.
Az önálló ForestSystem projekt nélkül csak ezeket a mechanizmusokat futtatja.

## Ellenőrzés

A `TreeVitalityRuleTests` a havi alapgráfokat, az átépített hatásokat,
a célérték-közelítést, a görbékből képzett küszöböt, az eltávolítás
azonosságait, a natív tartalékág hiányát, a szeletelést, a mentési határt,
a többhónapos csoportosítást és a naplóból visszajátszást ellenőrzi.
A régi natív önritkulási tesztek helyét világszintű gráftesztek vették át.
Az editor füsttesztje mind a négy gráfot kirajzolja.

Release mérés: `dotnet run --project ForesTycoon -c Release --no-build -- --tree-vitality-benchmark`.
.NET 8.0.32, `DOTNET_TieredCompilation=0`; felmelegített gráffutás,
4 ms célú gazdakeret, hívásonként 16 determinisztikus munkaegység.
A teljes havi erdőmunka mérése sok fa egyszerre történő elhalásával:

| Pálya | Induló fa | Munkaképkocka | CPU összesen | p95 | Legrosszabb |
| --- | ---: | ---: | ---: | ---: | ---: |
| 64×64 | 2 000 | 5 | 20,817 ms | 6,304 ms | 6,304 ms |
| 256×256 | 31 638 | 31 | 138,724 ms | 4,214 ms | 18,938 ms |

Ez egy kis mintás, grafika nélküli CPU-mérés; a létrehozás és a próbaállapot
beállítása kívül esik rajta. A mért futás tartalmazza a memóriafoglalás és
szemétgyűjtés költségét. A 4 ms időkeret puha korlát, nem garantált maximális
képkockaidő. A hónap teljes munkája több képkockára oszlik, a legrosszabb
kiugrás még további motoroldali vizsgálatra szorulhat.
