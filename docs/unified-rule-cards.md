# Egységes szabálykártyák és gráfmegjelenítés – 2026-10-16

A pipás állapotszabályok és a korábban külön, egyszerű szövegsorokként látható
válaszgráfok közös, egysoros jelölőnégyzetes bejegyzést kapnak a Szabályok listában.
Nincs külön magas listakártya. A kiválasztott szabály összecsukható részletei
négy kérdésre válaszolnak:

- Mikor fut?
- Miből számol?
- Mire hat?
- Ki használja az eredményt?

A grafikai egységesítés nem teszi azonos működésűvé a két szabálytípust.
Az állapotszabály továbbra is hatásokat hajt végre. A válaszgráf továbbra is
a saját számítási kapcsolataiból ad egy értéket. Nincs hozzá második,
párhuzamos hatásmodell vagy külön szerkesztő.

## Közös vizuális nyelv

A két gráfnézet az `EditorStyle.GraphBox`, `Wire` és `Port` közös rajzolóit
használja: azonos háttér, árnyék, lekerekítés, keret, csatlakozó és nyílillesztés.
A válaszgráfoknál a forrás zöld, a számítás világos, az eredmény arany jelölést
kap. A technikai csomópontazonosítók nem jelennek meg a kártyák tartalmaként.
A bekötetlen forrás halvány és „Nincs bekötve” jelölésű. A kikapcsolt gráf
kapcsolatai halványak és szaggatottak, az eredménykártya a tényleges fix értéket mutatja.

A **Folyamatnézet** az élekből számított oszlopokba rendezi a gráfot:
források balra, számítások középen, eredmény jobbra. Ez a megjelenítés a
Folyamatkapcsolatok alatt található gráfokra is érvényes. Az elrendezés nem
írja át a szabályt és nem készít párhuzamos számítási modellt. A dobozok
kisebbek, a források zöldek, az eredmény aranyszínű. A gráf közvetlenül
szerkeszthető; a csomópont húzása átveszi a látható elrendezést szabad
elrendezésként, egy visszavonható szerkesztési lépésben.

A **Szabad elrendezés** a mentett pozíciókat mutatja, a **Teljes gráf** a teljes
aktuális nézetet keretezi. A görgő nagyít, a jobb gomb mozgat. A részletes
információk alapból csukva maradnak, így a gráfnak jut hely.

A listában a kiválasztott sorhoz odagördül a nézet. Az egyes listakártyáknak
nincs külön görgetősávjuk. Az állapotszabályok neve, futási ideje és egyéb
részletes beállításai, valamint szöveges magyarázata összecsukható; a gráf
alapból nagyobb helyet kap. A nulla méretű, kivágott vásznat nem rajzoljuk,
ezzel elkerüljük az ImGui natív assertjét kis vagy változó méretű panelekben.

## A leírások forrása

A források és mezőnevek a meglévő annotált objektumsémából származnak.
A válaszgráfok bemenetei csak a kimenethez ténylegesen bekötött csomópontokból
kerülnek a leírásba. A válaszgráfok közötti kapcsolatok szintén a tényleges
bemenetekből készülnek; a kikapcsolt fogyasztógráfokat nem nevezzük aktív
bekötésnek. Az állapotszabályoknál az áttekintés olvasó/író adatai mellett
közvetlenül az összekötött hatások, feltételek és számítási gráfok forrásait is
feltüntetjük, beleértve az egyedi mezőket.

A motor által szolgáltatott válasz kimenetének jelentése és használati helye
külön szerződésleírás a `ResponseRuleDescription` osztályban. A kártya
megkülönbözteti a bekötött saját szabályokat a játékbeli használati helyektől.
Ez leíró metaadat, nem második végrehajtási szabály vagy azonos nevű új számítás.
Minden jelenlegi válaszkötéshez kötelező ilyen jelentés; ezt teszt ellenőrzi.
Az állapotszabályok költségesebb olvasó/író térképe a szabályadat változásáig
újrahasználható, nem épül újra minden képkockán.

## Kikapcsolás: különböző, de egyértelmű következmény

**Állapotot változtató szabály:** a hatásai nem hajtódnak végre. Az eddig
végrehajtott változtatások nem fordulnak vissza, más szabályok tovább hathatnak.
Ezt a kártya és a kapcsoló súgója is kiírja.

**Értéket számoló szabály:** kikapcsolás előtt a felhasználó megadja a
helyettesítő fix eredményt. A felület kiírja, mit jelent az adott kimenetnél,
és az érték mértékegységét, valamint megengedett tartományát is kezeli.
A **Mégsem** nem módosítja az aktív gráfot. Csak a „Kikapcsolás ezzel az
 eredménnyel” gomb kapcsolja ki. Példák:

- Árnyékolásnál 0: egyik vizsgált szomszéd sem ad árnyékterhelést.
- Termőhelyi alkalmasságnál 1: minden vizsgált faj és termőhely teljes
  alkalmasságot kap.
- Magadásnál vagy csírázásnál 0: nincs természetes újulat.
- Kezdő magasságnál 1 m: minden ezután létrejövő fa 1 m magas, a már élő fák
  tárolt mérete megmarad.

Az ajánlott érték csak kitölti a választómezőt, nem kapcsolja ki automatikusan
a gráfot. A felhasználó más megengedett értéket adhat meg. A fix eredmény
kikapcsolt állapotban is módosítható. Visszakapcsoláskor a megőrzött eredeti
gráf fut tovább, a csomópontokat és kapcsolatokat nem írjuk át.

A mentett `BehaviorGraph` két opcionális mezője: `disabled` és `disabledValue`.
Az alapértelmezett, aktív gráfok JSON-ja változatlan. A közös `Program.Evaluate`
a letiltott gráfnál a validált fix értéket adja vissza, még a bemenetek olvasása
és az értelmezett vagy fordított csomópontok futtatása előtt. Nem ad vissza
régi natív alapképletet. A kikapcsolásnak nincs rejtett, mindenhova alkalmazott
nulla vagy egy értéke. A hiányzó, nem véges vagy tartományon kívüli eredmény
érvénytelen projektet jelent. Ezt a mechanizmust csak a válaszgráfok használják;
a motor natív adapterei nem kapnak félreérthető, általános kikapcsolót.

A futtató verziója `forestycoon-simulation/2026-10-16.10`, így az új kikapcsolási
állapotot egy korábbi futtató nem hagyhatja észrevétlenül figyelmen kívül.
A szállított, referencia- és helyi projektek beágyazott állapotának verziójelét
frissítettük, a gráfjaikat és faállapotukat megőriztük. A kikapcsolási felület
magától egyetlen jelenetszabályt sem kapcsolt ki. A külön újrarögzített nyitóvilág
**minden referencia-lenyomata egyezik**; nincs új alapértelmezett modellváltozás.
A helyi projekt másolata: `artifacts/rule-cards-project-backup/project.before-cards.json`.

## Ellenőrzések

- Release editor build: 0 hiba, 0 figyelmeztetés.
- Teljes tesztcsomag: **1868/1868**, ebből 9 új célzott próba a kártyák
  jelentésére, a tényleges bemenetekre, a fogyasztói kapcsolatokra, a fix
  eredményre, a megengedett tartományokra, a mentésre, a szeletelésre és a naplós
  visszajátszásra (`artifacts/rule-cards-tests.txt`).
- A natív editorfüstteszt tényleges egérművelettel ellenőrzi a bekötést,
  a kikapcsolási választó megszakítását, az elfogadott fix érték alkalmazását,
  a csomópontok megőrzését, a mentést és a visszakapcsolást. Az önálló ablak
  füsttesztje is sikeres (`artifacts/rule-cards-smoke-final.txt`).
- A rajzolt hatás- és válaszgráf képeit szemrevételeztük. Példák:
  `artifacts/rule-editor/world-rule-influences.png`,
  `artifacts/rule-editor/initial-size-crown-graph.png`,
  `artifacts/rule-editor/response-card-off-choice.png`,
  `artifacts/rule-editor/response-card-off.png`.


A Folyamatkapcsolatok a tényleges forrásokat és a beépített alapfolyamattól
való függést is jelzi. Az alapfolyamat felülírása nem azonos annak teljes
gráfos átvezetésével. A közös görbecsomópont és kezelése a
[rule-editor.md](rule-editor.md#a-közös-szerkesztő-lezárása) leírásában szerepel.
