# Szabályrendszer editor

Az editor külön indítható projektben mutatja a jelenlegi játék szabályrendszerét és összefüggéseit,
valamint szerkeszti a játékba kötött útkopási egyenleteket.

```powershell
dotnet run --project ForesTycoon.Editor
# Meglévő modell megnyitása:
dotnet run --project ForesTycoon.Editor -- --model artifacts/rule-editor/road-wear.json
```

## Projekthatárok

- `ForesTycoon.Rules`: közös JSON-modell, ellenőrzés és egyenletfuttató. Nincs játék-, grafikai vagy UI-függősége.
- `ForesTycoon.Editor`: önálló alkalmazás, gráfszerkesztő, próbaszámítás, fájlkezelés és saját tesztvilág.
- `ForesTycoon`: a szabályok világkötései, alkalmazása, parancsnapló és játékmentés. Nem hivatkozik az editorra.

Az editor a meglévő játékfuttatót és ImGui/OpenGL hátteret használja a tesztvilágához.
A függőség iránya `Editor → játék → Rules`, és az editor közvetlenül is használja a `Rules` könyvtárat.
A szerkesztőben végzett alkalmazás a saját tesztvilágot módosítja; a külön futó játékba fájlon keresztül kerül a modell.

## Használat

Indításkor a **Jelenlegi játék szabályrendszere** nézet nyílik meg. A modulra kattintva annak
szabályhálója látható; a szabály kijelölése megmutatja a hatókört, ütemezést, képletet,
bemeneteket, kimeneteket, paramétereket és a forráskód fájlját, illetve szimbólumát.
A bemeneteknél és kimeneteknél a kapcsolódó szabályokra is át lehet lépni.
A keresés az összes modul szabályait eléri. Az elrendezés és a saját megjegyzések menthetők.
Az útkopási nézet és a katalógus ugyanazt a szerkesztési gráfot őrzi nézetváltáskor.
Hibás gráf külön szerkesztési JSON-ként menthető; teljes katalógusként csak érvényes gráf exportálható.

Az **Útkopási gráf szerkesztése** nézetben:

1. A gráfban jelöld ki a **Kopási szorzó** csomópontot, és módosítsd az értékét.
2. A **Próbaszámítás** megmutatja a kimenetet az adott terhelésre, burkolatra és sérültségre.
3. A **Mentés JSON-ba** kiírja a modellt. A játékban **F12 → Szabálymodell** alatt add meg ezt a fájlt,
   majd kattints a **Szabálymodell alkalmazása** gombra. A modellváltás naplózott parancs.
4. Közúton mozgó teherautó következő csempeváltásakor már a módosított modell számolja az útkopást.
5. A játék szabálymodell-paneljén látható a legutóbbi áthaladás bemenete, kimenete és útállapot-változása.

Az editor **Alkalmazás a tesztvilágban** gombja a saját, elkülönített GameWorld-példányára vonatkozik.
Ebben a változatban nincs térképes tesztvilág-kezelő vagy folyamatközi élő kapcsolat; az egyenletek
próbaszámítása közvetlenül használható, a teljes világkötést a grafikus smoke teszt is ellenőrzi.

A csomópontok bal gombbal húzhatók, a vászon jobb gombbal mozgatható, a görgő nagyít.
Kapcsolat létrehozása vagy módosítása: jelöld ki a műveletet, majd válaszd az **A bemenet** és
**B bemenet** csomópontját. Új elem, törlés és kimenetválasztás a jobb oldali panelen található.
Az alapmodell gomb csak a szerkesztési dokumentumot cseréli; a játékban külön alkalmazni kell.
Új világ alapmodellt kap. Betöltött játék a mentésben rögzített aktív modellt használja.

## Modell és világkötés

### A jelenlegi játék katalógusa

A forráskódból felmért katalógus 74 szabályt, 13 modult és 553 mezőkapcsolatot tartalmaz.
Lefedi az időlépéseket, terepet, talajt és klímát, vízháztartást, erdőfejlődést, erdészeti
beavatkozásokat, utakat és nyomokat, járműveket, fenntartást, faanyag-logisztikát, gazdaságot,
vadakat és a világállapot megjelenítését. A kapcsolatok a közös olvasott és írt állapotmezőkből
származnak; a visszacsatolási körök megengedettek. Ezek a kapcsolatok nem határoznak meg új
futtatási sorrendet: az ütemezés a meglévő játékfolyamatoké marad.

A `CurrentGameRules.Build` a játék konstansaiból, faj-, talaj- és klímaprofiljaiból állítja össze
a paramétereket. Az editor saját világából az aktív évhosszt, profilokat és útkopási modellt
veszi át. Egyes állapotmezők mellett a tesztvilág élő, összesített megfigyelése is látható.
A hiányzó játékrendszereket külön lista jelzi; például a vadak jelenleg nem károsítják a fákat,
és a vizuális lombhullás még nem módosítja a vízmodell levélfelületét.

A verziózott alapállapot: [current-game-rules.json](../ForesTycoon.Rules/Catalog/current-game-rules.json).
Grafika nélkül újragenerálható:

```powershell
dotnet run --project ForesTycoon.Editor -- --export-current-rules ForesTycoon.Rules/Catalog/current-game-rules.json
dotnet run --project ForesTycoon.Editor -- --model ForesTycoon.Rules/Catalog/current-game-rules.json
```

A katalógus JSON a szabályleírásokat, forráshivatkozásokat, elrendezést, megjegyzéseket és az
útkopási gráfot együtt tárolja. Betöltése az editor dokumentumát nyitja meg. A natív C#-folyamatok
képletének szerkezete itt csak olvasható, a számaik viszont hangolhatók (lásd lent). Az útkopás
kijelölt szabályánál külön gomb nyitja a ténylegesen szerkeszthető gráfot.

## Hangolás

A kulccsal jelölt paraméterek (`GameTuning`, jelenleg 48 szám: gépkapacitások és ráták, üzemanyag
és dízelár, kopás, meghibásodás és javítás, út- és nyomköltségek, útkopási bemenetek, a nyomok
benövése, a teherautó tömege, motorja, gördülési és légellenállása, a járműidő szorzója) az
inspectorban és a **Hangolás** fülön szerkeszthetők: húzással, Ctrl+kattintással beírva, az
`alap` gombbal visszaállítva. Minden számnak tartománya van; azon kívüli értéket a játék nem fogad el.
Az **Alkalmazás a tesztvilágban** gomb a gráfot és az összes hangolt számot naplózott parancsként
(`SetTuning`) alkalmazza, így a visszajátszás és a checkpoint pontosan ugyanazokkal az értékekkel fut.
A fájl csak az alapértéktől eltérő számokat tárolja.

A játékba a katalógus (Fájl » Mentés) F12 » Szabálymodell alatt tölthető be: ekkor az útkopási
gráf és a hangolás is alkalmazódik; egy önálló útkopási gráf JSON továbbra is betölthető.
A talaj-, klíma- és fafajparaméterek egyelőre tájékoztató adatok, a világ létrehozásakor rögzülnek.

### Futtatható útkopási gráf

Az első világkötés: `road.trafficWear`. Hatóköre közúti csempe, kiváltója a jármű áthaladása.

| Bemenet | Jelentés | Egység |
|---|---|---|
| Áthaladási terhelés | A meglévő járműszabály: `0.0015 × össztömeg / 36000 kg` | Állapotveszteség/áthaladás |
| Burkolati szorzó | Aszfalt: 0,2; makadám: 1 | Dimenzió nélküli |
| Út sérültsége | `1 - útállapot`, az áthaladás előtt | Dimenzió nélküli |

Az alapgráf: `terhelés × burkolati szorzó × kopási szorzó`, az utolsó alapértéke 1.
Ez megőrzi a korábbi közúti kopási szabályt. A kimenet 0–1 közé korlátozott állapotveszteség.
A világ adaptere a térkép `WearRoad` műveletét hívja. A járműdinamika a térképből olvassa vissza
az útállapotot, és ebből számolja a sebességkorlátot és a gördülési ellenállást. A fogyasztás továbbra
is a meglévő fizikai modellből származik. A közelítőnyom kopási és regenerációs szabálya változatlan.

Műveletek: állandó, összeadás, szorzás, minimum, maximum. A bemenetek és a kimenet egységét a
fordító ellenőrzi; például állapotveszteség és dimenzió nélküli szorzó összeadása hibás.
A gráf legfeljebb 128 csomópontot tartalmazhat. A hiányzó bemenet, az ismétlődő azonosító,
azonnali képletkör és túlcsordulás megakadályozza az alkalmazást. A visszacsatolás az útnak
a világban tárolt állapotán keresztül történik, két áthaladás között.

## Mentés és futtatás

Az editor JSON-dokumentuma a gráfot, neveket, paramétereket és elrendezést tárolja. Az alapértelmezett
hely a helyi alkalmazásadatok `ForesTycoon/rules/road-wear.json` fájlja; a fájlútvonal átírható.
Befejezetlen gráf is menthető és újranyitható. A fájl betöltése nem módosítja az aktív játékmodellt.

A futtató ellenőrzött, topologikusan rendezett, privát modellpéldányt használ, újrahasznosított
számítási tömbökkel. A szerkesztési dokumentum későbbi módosítása nem változtatja meg ezt.
A szabályváltás `SetRuleModel` parancs, a teljes modell a parancs saját JSON-adatában szerepel.
A 10-es játékmentés checkpointja az aktív modellt is rögzíti. A függőben lévő szabályváltás
betöltés után is függőben marad. Checkpoint nélküli visszajátszáskor a modellváltások a napló
megfelelő pontján történnek. A 4–9-es mentések továbbra is betölthetők; modell nélkül az alapgráf él.

## Ellenőrzés

```powershell
dotnet test ForesTycoon.sln --no-restore
dotnet run --project ForesTycoon.Editor -- --smoke-test
```

A grafikus próba ellenőrzi a módosított gráf térképi hatását, a mentés/betöltést, a függő parancsot,
a napló-visszajátszást, egy ténylegesen mozgó és rakott jármű áthaladási callbackjét, valamint
a szerkesztő kirajzolását, minden katalógusmodul nézetét és az önálló editorablak indulását. Képek:
`artifacts/rule-editor/editor.png`, `artifacts/rule-editor/standalone.png`.
A modulnézetek az `artifacts/rule-editor/current-rules` könyvtárban találhatók.
A katalógustesztek ellenőrzik a forráskódhivatkozásokat, fontos visszacsatolásokat,
a profilparaméterek átvételét, a JSON-visszaolvasást és a verziózott alapállapot frissességét.

## Kódbázis-áttekintés és optimalizálás – 2026-10-09

A projektfüggőségek, a fő szimulációs és mentési útvonalak, valamint az editor ismétlődő
számításainak áttekintése alapján a mostani módosítások a szabályrendszer tervezését és
futtatását támogatják. A következő táblázat az áttekintés hatókörét és a további korlátokat rögzíti.

| Terület | Jelenlegi helyzet és döntés |
|---|---|
| Engine | Fix tick, parancs előtti publikálás, korlátozott háttérpárhuzamosság; a futtatási sorrend megmarad. A háttérfeladatok teljes sorhossza továbbra sincs korlátozva. |
| Ecology | Grafika nélküli többütemű folyamatfuttató, újrahasznált raszterek és havi előkészítés. Az összeadási sorrendet, hónaphatárokat és véletlengenerátorokat nem módosítjuk. |
| Map | Külön térképmodell, út/nyom-kapcsolatok és súlyozott útkeresés. Nincs útvonal-cache: a hálózat és nyomvályú változását így nem fedheti el elavult gyorsítótár. |
| GameWorld és logisztika | Közös útkopási paraméterforrás a jármű, a világadapter és a katalógus számára; a diagnosztika csak olvasáskor formáz szöveget. |
| Mentés és parancsnapló | Független szabálypillanatkép, naplózott modellváltás és izolált betöltés. A pillanatkép most közvetlen objektummásolás; a mentési JSON és verzió változatlan. |
| Rules | Indexelt mezőkapcsolat-építés, változatlan sorrend; közvetlen mélymásolat; hibás és érvényes gráfok fordítási eredményének gyorsítótára. |
| Editor | Betöltéskor készülő modul-, olvasó-, író- és keresési indexek. A keresési találatok csak kereséskor/modulváltáskor készülnek újra. A node-pozíciók képenként egyszer számolódnak. |
| Megfigyelések | Az editor összesített világadatai legfeljebb másodpercenként négyszer frissülnek; a szabály kiválasztásakor azonnal. Ez a megjelenítést korlátozza, nem a szimulációt. |
| Rendering és Effects | Backendhatárok, renderfázisok és vizuális időjárás elkülönítése megmarad. A szabályindex nem kerül a GPU-meneten belül újraszámításra. |
| Models és TreeModels | Modellbetöltés és seedelt fagenerálás a meglévő rétegekben marad. A nagy erdők LOD-cache-e és a sok modell kirajzolása külön renderbenchmarkot igénylő skálázási feladat. |

Az útkopási grafikus panel és a játék F12-es panelje az aktív modell nevéhez már nem másolja
a teljes dokumentumot. A fordítási cache figyeli a képletet meghatározó mezőket és a pozíciók
érvényességét. A node húzása nem indít új fordítást; érték, kapcsolat, művelet vagy kimenet
módosítása igen. Hibás módosítás törli a korábbi futtatható eredményt, így nem alkalmazható
véletlenül az előző érvényes képlet.

A `GameRuleIndex` a betöltött katalógus szerkezetére épül. Elrendezés és megjegyzés módosítható
mellette; azonosító, modul, kereshető szöveg vagy állapotmező-kötés módosítása után újra kell építeni.
Az index nem futtatási ütemező. Az eddigi 74 szabály és 553 kapcsolat sorrendjének megőrzését
a korábbi algoritmussal összehasonlító teszt ellenőrzi.

CPU-mérés az editor Debug buildjével, .NET 8 alatt, öt váltott mérési kör mediánja:

| Művelet | Referencia → optimalizált idő | Foglalás műveletenként |
|---|---:|---:|
| Katalógus mezőkapcsolatainak építése | 894,41 → 119,39 µs | 118 544 → 92 304 B |
| Modulkapcsolatok lekérése meleg indexből | 513,24 → 0,01 µs | 127 784 → 0 B |
| Független gráfpillanatkép | 43,33 → 0,43 µs | 4 593 → 616 B |
| Változatlan gráf ellenőrzése, cache nélkül → cache-sel | 4,06 → 0,30 µs | 3 056 → 0 B |

Az első három referencia a korábbi kapcsolat-, modulháló- és JSON-másolási algoritmus.
A negyedik sor ugyanazt a jelenlegi fordítót hasonlítja össze cache nélkül és cache-sel.
A meleg index/cache sorok nem tartalmazzák az egyszeri felépítés költségét. A mérés nem teljes
editor-képkocka, GPU-idő vagy játék-FPS; az eredményben lehet gép- és futásfüggő zaj.

```powershell
dotnet run --project ForesTycoon.Editor --no-build -- --rules-benchmark
```

Részletes eredmény: `artifacts/rule-editor/optimization-benchmark.json`. Az ellenőrzéskor 1489
teszt sikeres; a grafikus próba külön kirajzolja a gyorsítótárazott útkopási nézetet, a teljes
katalógust és moduljait, ellenőrzi a gráfmódosítás katalógusban történő megőrzését, valamint a
játékba kötést és mentési visszajátszást. A tesztprojekt meglévő `TreePreviewDump.cs` diagnosztikája
nullable figyelmeztetéseket ad; a módosított editor buildje figyelmeztetés nélkül fordul.

A natív szabályképletek szövege továbbra is kézzel felmért leírás; a forráshivatkozások és
paraméterek ellenőrzése nem bizonyítja minden képlet automatikus azonosságát. A teljesen
adatvezérelt szimulációhoz a következő lépés az egyes folyamatok típusos világkötése és
fokozatos kiváltása, az eredeti eredményeket összehasonlító tesztekkel.

## Következő bővítések

Az útkopás az első végig bekötött, szerkeszthető modell. Az erdő, víz, talaj, pénzügy és épületek
meglévő szabályai már a közös katalógusban vizsgálhatók. Következő lépés ezek fokozatos átvitele
futtatható világkötésekre, külön folyamatütemekkel, készlet–áramlás csomópontokkal és görbékkel.
További munkák: kijelölt világobjektum
állapotának vizsgálata, idősoros kísérletek és undo/redo. A jelenlegi gráf képleteket szerkeszt;
új állapotmező vagy tetszőleges C# folyamat létrehozását még nem támogatja.
