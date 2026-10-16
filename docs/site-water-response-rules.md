# Termőhelyi alkalmasság és vízstressz a projekt gráfjaiból

A Szabályok listájában négy közvetlenül kiválasztható gráf van:

- **Termőhelyi alkalmasság** (`forest.site`): alapból a termékenység és a fafaj magasságigénye határozza meg az alkalmasságot. A térképi nedvesség is beköthető.
- **Fafaj vízválasza** (`water.stress`): a vízellátottságot a szárazságérzékenység alakítja, a pangóvíz csökkenti a választ.
- **Aszály követése** (`water.drought`): a vízhiányt követő állapot; alapból 90 másodperces exponenciális simítás.
- **Pangóvíz követése** (`water.waterlogging`): a talaj pillanatnyi víztöbbletét követő állapot; alapból szintén 90 másodperces simítás.

A csomópontok, kapcsolatok és a kimenet szerkeszthetők, képletbeírás nélkül. Egy forráscsomópontnál először az objektumot (Csempe, Fafaj, Időlépés), utána a mezőt kell választani. A kódnevek angolul maradnak (`tile.waterSupply`, `species.drySensitivity`, `step.seconds`), mellettük felderített magyar megjelenítési nevek és leírások vannak. Az állandók számmal és csúszkával állíthatók. A Teljes gráf gomb a teljes hálót a nézetbe igazítja; a görgő nagyít, a jobb gomb mozgat.

A négy gráf ugyanabban a munkafelületben jelenik meg, mint a többi szabály számítási gráfja. Nem maradt mellettük külön termőhely- vagy vízstressz-szerkesztő. A folyamat kikapcsolása vagy egyszerűsítése állandó kimenettel lehetséges; a motor nem tér vissza egy natív képlethez.

## Egyetlen számítási út

Az `EcologyResponseProgram` a közös, korlátozott `CompiledBehaviorModel` futtatóval fordítja és értékeli ki a mentett gráfokat. A képletszövegek kizárólag az induló gráfok létrehozásának rövid leírásai a forrásban; a projekt a szerkeszthető csomópontokat és kapcsolatokat tárolja, és a játék ezeket futtatja.

Töröltük a natív `ForestSystem.Fitness` és `EnvironmentSystem.WaterResponse` számítást, az eltérő környezet nélküli termőhelyágat, a fajajánló külön termékenységszorzóját és a `WaterFormulas` felülírási képletet. A `Suitability` már csak a gráfhoz továbbítja a mért adatokat. Az ültetés, újulatválasztás, fák erőforrása, kezdeti erdő és fajajánló ugyanazt az alkalmassággráfot használja. A pillanatnyi és havi vízválasz is ugyanazt a gráfot futtatja; csak a pangóvízbemenet különbözik (aktuális érték, illetve időátlag).

Az aszály és a pangóvíz frissítésében sem maradt párhuzamos natív simítás. A talaj készleteiből származó vízellátottság és nyers telítettség mérés, a havi időintegrál gyűjtése és törlése továbbra is motorfeladat. A talaj- és fajparaméterek a Típusok lapon szerkeszthető tartalmi adatok.

Az Ecology most a tiszta Rules futtatóra is hivatkozik; a Rules továbbra sem függ a játéktól, az Editortól, az Engine-től vagy grafikai könyvtártól. Ezt az architektúrateszt ellenőrzi. Az önálló ökológiai tesztvilág is a szállított alapgráfokat futtatja, külön C# szabálymásolat nélkül.

## Futás, generálás és mentés

Minden lekérdezés egyetlen csempe/fafaj vagy állapot válasza, legfeljebb 256 csomópontos, ciklusmentes gráfból. Nincs benne pályabejárás, véletlenforrás, világállapotból visszahivatkozó számítás vagy natív eredmény. A csempe-, fa- és generálási bejárások a meglévő szeletelt munkafolyamatokban maradnak; a gráfok nem vezetnek be korlátlan befejezési ágat.

A projekt és jelenet előkészítése dolgozó szálon fordítja a tiszta válaszgráfokat. A generálás már az induló fák létrehozásához megkapja a projekt saját válaszait, a jelenetfestés pedig ugyanezeket használja. A szinkron és szeletelt generálás saját gráfokkal is pontosan azonos ellenőrzőpontot ad. A tiszta gráfok fordítása nem próbálja világ nélkül kiértékelni más folyamatok készlet-/világkötéseit.

A szabálycsere szeletelt erőforrás- és növekedésiráta-frissítést kér, ha valamelyik válaszgráf módosult. A frissítési jel, az aszály, pangóvíz, havi integrál és a szerkesztett gráfok a mentés részei. A mentés továbbra is a logikai művelethatárt várja meg. A futtató verziója `forestycoon-simulation/2026-10-16.6`.

A régi `water.stress / tile` kötést a szállított, archivált és helyi projektadatokból egyszer távolítottuk el; a helyi előző változat az `artifacts/site-project-backup/project.before-site.json` fájlban van. Az új helye `water.stress / site`. Nincs régi kötést átfordító vagy párhuzamosan futtató betöltési ág. A nyitójelenet saját küldetései és pályaadatai megmaradtak; az új referencia csak az Ecology és Behaviors lenyomatát változtatja.

## Ellenőrzés

A `SiteResponseGraphTests` ellenőrzi a közös fajajánlót, ültetést, faerőforrást, pillanatnyi/havi vízválaszt, saját gráfos generálást, felderített megjelenítési neveket, egy munkadarabos szeletelést, mentett függő projektcserét és naplóból visszajátszást. A natív eredményre és világállapotra visszahivatkozó válaszgráfokat a validálás elutasítja. Futás közbeni aritmetikai hiba (például nullával osztás) esetén a válasz 0, az editor megnevezi a hibás gráfot és a hibát; nincs natív képletre visszaesés és nem omlik össze a világfrissítés. Javított projekt alkalmazása új, hibamentes programot telepít.

Az editor füsttesztje mind a négy gráfot megnyitja és képet ment, majd a külön editorablakot is ellenőrzi. Nagypályás CPU-mérés: `dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --site-response-benchmark` (64×64 és 256×256 pálya, eső és hó, erdőhónap-határral, teljes tick és 64 munkadarabos szelet pontos állapotegyezésével). Ez fej nélküli szimulációs mérés; nem teljes kirajzolt képkockaidő.

Végső ellenőrzés: **1837/1837 teszt**. A fordítási izoláció, az újonnan bekötött nedvességforrás és a hibás aritmetikai gráf jelentésének tesztjei is részei a teljes futásnak. Az editor buildje figyelmeztetés és hiba nélkül sikerült; a gráf- és különablak-füstteszt is átment.

### Nagypályás mérés – 2026-10-16

Release, .NET 8.0.32, `DOTNET_TieredCompilation=0`, erősen erdősített pálya, 60 bemelegítő és 330 mért tick. A mért idő a szimuláció CPU-munkája, memóriafoglalással és szemétgyűjtéssel együtt; a 64 munkadarabos hívás nem egy teljes megjelenített képkocka. A teljes tick egyben futtatása csak a diagnosztikai összehasonlító oldalon szerepel.

| Pálya | Időjárás | Faegyed | Szelet p95 | Szelet p99 | Szelet maximum | Teljes tick maximum | Pontos állapotegyezés |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| 64×64 | Eső | 15 797 | 0,063 ms | 0,131 ms | 1,532 ms | 131,581 ms | igen |
| 64×64 | Hó | 15 797 | 0,051 ms | 0,115 ms | 2,042 ms | 104,102 ms | igen |
| 256×256 | Eső | 247 238 | 0,051 ms | 0,077 ms | 4,852 ms | 1578,596 ms | igen |
| 256×256 | Hó | 247 238 | 0,046 ms | 0,069 ms | 10,384 ms | 1549,132 ms | igen |

A táblázat a végső, tesztek és editorfüstteszt utáni külön futás. Egy korábbi, ugyancsak teljesen egyező állapotú futásban a 256×256-os havas eset maximuma 17,088 ms volt. A végső futás 10,384 ms-os maximumát és a korábbi kiugrást a mérés nem bontja szét szemétgyűjtésre, rendszerütemezésre és egyedi munkaegységre. Ezért nem igazol garantált maximális képkockaidőt vagy minden ritka akadás megszűnését. A gráfok korlátozottak, a bejárás szeletelt; minden esetben bitre egyező állapot és erdő-ujjlenyomat született a teljes és szeletelt futásból. A ritka kiugrás külön profilozási feladat.
