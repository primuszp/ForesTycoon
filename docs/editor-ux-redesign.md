# A szabályeditor új használati terve

Állapot: az első gyakorlati szakasz megvalósult, 2026-10-11. Elkészült a tematikus
munkatér, a négy közös lépés, a szűkített adatkapcsolati áttekintés, a beágyazott
gráfszerkesztők, az elkülönített munkapéldány-mentés, a checkpointból végzett
összehasonlító próba és a játék átvételi visszaigazolása. A képletgráfokhoz szorzó
hozzáadásával induló segédművelet készült. Az aktuális használat az
[editor útmutatóban](rule-editor.md) található.

Második megvalósítás: elkészült a belső algoritmusok veszteség nélküli
műveletnézete, a blokkon belüli műveletbeszúrás és saját eljárás létrehozása,
a tematikus készlet-/áramláskezelés, a készlet–szabály kötés, az aktív kötések és
sablonok szabálytára, az import/export, az egyedi objektummunkaciklusok és az
alapértelmezéshez visszatérés. Van közös objektumkeresés és vezetett indulás.
A közös szerkesztési metaadatok, verzióelőzmény, helyreállítási mentés és külső
fájlmódosítás-védelem a felhasználó és az AI munkáját egy projektben kezeli.

A dokumentum további része az átfogó célterv. Nem minden belső szintaktikai elem
kapott külön szemantikus adaptert: az ismeretlen szerkezet elérhető részletes
gráfként. Faj- és területszintű univerzális kötési lánc, automatikus egységkövetés,
háromirányú AI-összeolvasztás és grafikus 3D objektumkijelölés nincs kész;
ezeket a felület nem jelzi támogatott műveletként.

## Cél

A felhasználó játékmeneti szándékból induljon: „lassabban kopjanak az utak”, „mitől nem nőnek a fák?”, „ez a teherautó várjon teljes rakományra”. Ehhez ne kelljen előre tudnia, hogy hangolás, képlet, állapotgép, készlet–áramlás vagy belső algoritmus módosítása szükséges.

A jelenlegi erdei megjelenés marad: sötét zöld felületek, világos pergamenszínű szöveg, visszafogott olívazöld gombok, meleg kiemelések. A változás az információ szervezése és a munkafolyamat. Minden játékszabály továbbra is gráfból szerkeszthető; az egyszerűbb belépési pont nem korlátozza az elérhető műveleteket.

## Mi okozza most a nehézséget?

| Jelenlegi megoldás | Használati következmény |
| --- | --- |
| Áttekintés, Hangolás, Útkopás · gráf, Viselkedések, Dinamika, Objektumok, Algoritmusok külön lap | Előre ismerni kell a belső megvalósítás típusát. Egy játékmeneti téma több lapon szétesik. |
| A felső projektsáv egyszerre mutat fájlokat, publikálást, szerkesztési mentést, tesztvilágot, tickeket és mentésbeolvasást | Nincs egyértelmű következő lépés, a hasonló feliratú műveletek eltérő világokra hatnak. |
| A teljes rendszertérképen nagyon sok adatkapcsolat látszik | Nehéz kiválasztani a saját kérdéshez tartozó összefüggést. |
| Az algoritmusszerkesztő fájlokat, szintaktikai csomópontokat és számozott gyermekportokat mutat | A gráf vizuális, de használatához még a belső kód szerkezetét kell érteni. |
| Az objektumkiválasztás a támogatott hookok listájához vezet; egyes objektumoknál kézzel kell azonosítóra szűrni az algoritmusban | Nem derül ki azonnal, mi az alapértelmezés, mit ír felül a helyi szabály, és mire van tényleges támogatás. |
| A mentés, a teszt és a játékban futó verzió nem közös, hangsúlyos állapotként jelenik meg | Könnyű azt hinni, hogy egy elmentett vagy tesztelt változás már a játékban fut. |

A meglévő `CurrentRulesView` témacsoportjai, a `GameRuleCatalog` bemenetei/kimenetei és a forráshivatkozásai jó kiindulópontok. Nem szükséges új szimulációs motort készíteni a felület rendbetételéhez. A kényelmes belső algoritmusgráf azonban külön szemantikus megjelenítési réteget igényel: a jelenlegi szintaxisgráf átnevezése önmagában nem elég.

## A főablak

**Felső sáv:** projekt neve, a módosítás állapota, a játék kapcsolata; visszafogott Projekt menü. A fájlútvonal, import/export, mentésválasztás és fordítási részletek itt nyithatók meg. A kapcsolat mindig megmondja, hogy élő objektumkatalógust, régi katalógust vagy példavilágot látunk.

**Bal oldal:** témák. A kiválasztott témában név szerint rendezett szabályok. Egy közös keresés szabályt, fogalmat és objektumot találhat; a találat ugyanahhoz a szabályhoz vezet, mint a témaböngésző.

**Közép:** az aktuális szabály munkaterülete. Négy állomás: Megértés, Szerkesztés, Próba, Alkalmazás. Bármikor vissza lehet lépni; ez nem kötelező varázsló. A fő művelet az aktuális állapothoz igazodik.

**Részletek:** alapból zárt. A kijelölt elem további kapcsolatai, belső azonosítója, forrásgráfja, porttípusai és diagnosztikája itt érhető el. Nem állandó harmadik oszlop.

**Alsó helyi sáv:** egyértelmű következő művelet, rövid állapot, visszavonás. A globális projekteszközök nem ismétlődnek minden szabálynál.

## Tematikus felosztás

| Téma | Tartalom, példák |
| --- | --- |
| Erdő és élővilág | Fafajok, induló erdő, termőhely, versengés, növekedés, egészség, elhalás, újulat, vadak |
| Időjárás, talaj és víz | Klíma, időjárási események, talajprofilok, hó, beszivárgás, párolgás, felvétel, lefolyás, vízmérleg |
| Kitermelés és szállítás | Kijelölés, kivágás, gépi munkaciklus, rakodás, sarangok, készletek, célpontok, feldolgozás |
| Utak és járművek | Hálózat, útépítés, útkopás, nyomok, útvonal, sebesség, ellenállás, járművek |
| Gazdaság és fenntartás | Árak, bevételek, üzemanyag, működési költség, kopás, javítás, meghibásodás |
| Világ és idő | Terepgenerálás, időlépések, naptár, parancsok, induló állapot, mentett állapot kapcsolata |
| Látvány | Fák, járművek, víz, időjárás, évszakok, animáció, megjelenítési algoritmusok |

Egy szabálynak egyetlen elsődleges helye és több kapcsolódó témája lehet. Például az útvonalválasztás elsődlegesen az Utak és járművek témában van, de a Szállításból is megnyitható. Mindkét belépési pont ugyanazt a szerkesztési dokumentumot mutatja.

A saját szabályok, készletek és folyamatok a választott témában jelennek meg. A Dinamika nem külön főmenü: a vízkészlet a Víznél, az üzemanyagkészlet a Gazdaságnál található. Új készlet/áramlás létrehozásakor célmezőt és fogyasztó szabályt is be lehet kötni; egy saját készlet létrehozása önmagában nem módosítja a meglévő játékrendszereket.

## Megértés: a szabály lapja

A szabály neve és egy rövid, hétköznapi leírás után négy kérdésre ad választ:

1. **Miből számol?** A közvetlen bemeneti mezők magyar neve, forrása és mértékegysége. Az érték csak akkor jelenik meg, ha az adott objektumhoz és világforráshoz ténylegesen elérhető.
2. **Mit változtat?** A kimenet és a közvetlen fogyasztók. A távolabbi gazdasági hatás külön kapcsolat, nem garantált eredmény.
3. **Kikre vonatkozik?** Globális, típus-, faj-, terület- vagy objektumszint, a tényleges képességek szerint. A nem támogatott szinteket nem lehet működőként kiválasztani.
4. **Mikor fut?** Emberi megnevezés: áthaladáskor, havonta, munkaciklusonként, új világ létrehozásakor stb. Külön jelölés arra, mikor lép életbe a módosítás.

A közvetlen kapcsolatokat egy kis térkép mutatja: **bemenetek → kiválasztott szabály → közvetlen eredmények**. Kattintással lehet egy kapcsolatra továbbhaladni, a visszaút megmarad. Az összes rendszert tartalmazó térkép másodlagos „Teljes összefüggéstérkép” művelet.

A kapcsolatfelirat kötelező: adatot ad át / állapotátmenet / készletáramlás / következő időlépés / felülírás. A katalógus `Reads/Writes` kapcsolata adatfüggőség, nem bizonyítja a végrehajtási sorrendet vagy a hatás előjelét. A bemutatott láncoknak ezt tisztázniuk kell. Következő lépéses visszacsatolás külön jelölést kap.

### Példa: fanövekedés

`forest.growth` a termőhelyet, fény- és térviszonyokat, vízválaszt, egészséget, időt és fafajt olvassa. Méretet és fatérfogatot módosít. A havi ráták mellett a méret folytonosan interpolálódik.

Az első nézet a „Vízválasz → Fanövekedés → Faméret / fatérfogat” kapcsolatot emeli ki; a többi tényező kinyitható. A jelenlegi képlet összetevői megmaradnak: alkalmasság × fényválasz × vízválasz × sqrt(tér) × egészség × évszak. A kattintható felülettervben szereplő extra szorzó javasolt szerkesztési példa, nem már meglévő hangolási paraméter.

## Szerkesztés: érthető gráf, teljes hozzáféréssel

Az editor automatikusan az adott szabályhoz illő gráfot nyitja meg. A felhasználónak nem kell külön Algoritmusok vagy Viselkedések lapot választania.

| Szabály jellege | Alapnézet |
| --- | --- |
| Képlet | Bemenet → számítás → eredmény, névvel ellátott portok |
| Döntés | Ha / különben ágak, az ág feltétele és következménye |
| Munkaciklus | Állapotok és átmenetek, az átmenet feltételével |
| Készlet és folyamat | Készlet, be-/kiáramlás, ráta, időlépés és megmaradási korlát |
| Adatdefiníció | Gráfban szerkeszthető faj-/talaj-/objektumdefiníciók, mezőkkel és örökléssel, ahol létezik |
| Belső algoritmus | Elnevezett műveleti blokkok, elágazás, ciklus, hívás és állapotírás; algráfok kibonthatók |

A helyi eszköztár alapműveletei: **Elem hozzáadása, Kapcsolat létrehozása, Visszavonás, Ellenőrzés**. Egy kimenetből indított hozzáadás csak a kompatibilis műveleteket ajánlja. A típus, mértékegység, tartomány és portnév segít az összekötésben, ahol ezek biztosan ismertek; ismeretlen egységet nem találunk ki.

A csomópont kijelölése a munkaterületen egy kompakt szerkesztőt nyit: mit csinál, milyen értéket/feltételt használ, bemenetek/kimenet. A számértékek módosítása is a gráf megfelelő értékcsomópontját írja, nem egy külön, második igazságforrást.

Az **Összefoglaló gráf / Teljes gráf** nézetváltó ugyanannak a gráfnak két megjelenítése. Az ismeretlen szerkezet „Részletes művelet” blokként jelenik meg és teljes gráfként megnyitható. Általános sablonra nem alakítjuk át veszteségesen. Az összecsukott blokk belseje és hatásai továbbra is ellenőrizhetők.

A szintaktikai gráf képességei megmaradnak a teljes nézetben. A szemantikus alapnézethez felismerő és visszaíró adapterek szükségesek. Egy algoritmusnév katalogizálása nem jelenti automatikusan, hogy annak minden belső művelete már érthető blokkokra van leképezve.

## Objektum felől induló munka

Az **Objektum kiválasztása** második belépési pont; nem külön szabályrendszer. A futó játék katalógusából választott objektum lapja az azt érintő szabályokat ugyanabba a tematikus munkatérbe vezeti.

Minden szabálynál látszik:

- az effektív alapértelmezés forrása;
- van-e típus- vagy egyedi felülírás;
- mit módosítana az új helyi szabály;
- támogatott-e ez a kötés, vagy közös algoritmusban kell explicit szűrőt szerkeszteni.

Az egyedi felülírás létrehozása explicit művelet. Egy objektum megnyitása nem hoz létre csendben egy gráfmásolatot. A „Vissza az alapértelmezéshez” eltávolítja a helyi kötést, és előtte megmutatja az effektív változást. A prioritásokat a tényleges futtató határozza meg; az editor nem ígér univerzális globális → faj → terület → egyed láncot, ha az adott szabály ezt nem támogatja.

A jelenlegi katalógus objektumlista és részleges megfigyelés, nem teljes élő világállapot. A próba explicit mentésből/pillanatképből dolgozik. Egy objektum hiánya vagy régi katalógus nem eredményez csendes átállást másik világra.

## Próba és alkalmazás

A verzióállapotok külön jelentést kapnak:

- **Szerkesztett:** a munkapéldány, menthető akkor is, ha még érvénytelen.
- **Próbált:** a pontos dokumentumverzió, az induló világ és a próba időtartama együtt.
- **Játékban fut:** a játék által visszaigazolt projekt- és algoritmusverzió.

Új módosítás után a régi próba megmarad az előzményben, de **korábbi változathoz tartozik**. Egy publikált fájl nem egyenlő azzal, hogy a játék átvette.

**Mentés / Ctrl+S:** csak a szerkesztési munkapéldányt menti. Nem publikálja a játék által figyelt projektfájlt. A munkapéldány és az aktív dokumentum tárolását ehhez ténylegesen szét kell választani; a jelenlegi automatikusan figyelt publikációs útvonalra nem szabad a draftot írni.

**Próba:** elsőként megmutatja a forrást (példavilág / választott játékmentés), a pontos világverziót, futási időt és használt algoritmusverziót. A gyors képletellenőrzés külön „Helyi ellenőrzés” feliratot kap; nem állíthatja, hogy a teljes módosított játékot futtatta. Belső algoritmusmódosítás teljes fordított gráfpróbára kerül, mert a beépített tesztvilág az editor algoritmusverzióját használja.

Az összehasonlító próba azonos kiinduló mentést, seedet, parancsokat és időhorizontot használ az alapverzióhoz és a munkapéldányhoz. Először rövid, majd a szabály időskálájához megfelelő futás következik. Egy havi növekedési szabályhoz egy tetszőleges 300 tickes futás nem feltétlenül informatív. A naptári időt a tényleges világkonverzióból kell számolni.

A próbalap csak a szabályhoz kapcsolódó mérőszámokat mutatja, az elérhető megfigyelések alapján. Fanövekedés: méret/térfogat és érintett egészség-/vízmutatók; útkopás: útállapot és közvetlen forgalmi hatás. A jelenlegi teljes próba főként ticket és egyenleget összegző eredményéhez ehhez új mintavételi és összehasonlítási támogatás kell. Hiányzó mérés esetén „Nincs mérési adat”, nem kitalált hatásszám.

**Alkalmazás:** tömör változáslista, hatókör, aktiválás időpontja és szükséges teendő. A szokásos gráfcsere a támogatott alkalmazási ponton kerül be. Belső algoritmuscsere fordítást és mentett állapotból új runtime-indítást igényel. Az újvilág-paraméterek meglévő világra nem feltétlenül hatnak; ezt minden ilyen szabály megmondja. A világ állapotának visszaforgatása és a szabályverzió visszaállítása két eltérő művelet.

A fő gomb a valós állapotot tükrözi: **Gráf szerkesztése → Ellenőrzés és próba → Alkalmazás áttekintése → Alkalmazás a játékban**. Érvénytelen gráf nem alkalmazható. A próba hasznos, de önmagában nem bizonyít minden szimulációs tulajdonságot; a felület nem ad automatikus „biztonságos” minősítést.

## AI-val közös szerkesztés

A felhasználó és az AI ugyanazt a szabályazonosítót, gráfot, hatókört és projektverziót használja. A nézet nem hoz létre külön AI-szabályokat.

Az AI módosítása ugyanabban a változáslistában jelenik meg: mely szabály, mely csomópont, milyen kötés változott, milyen próba futott. A felhasználó visszavonhatja, módosíthatja vagy alkalmazhatja. Egy szöveges magyarázat nem helyettesíti a tényleges gráfváltozást. Az AI-hoz új kommunikációs szolgáltatás nincs a terv részeként feltételezve; a meglévő projekt- és CLI-formátum használható.

## Megvalósítás a jelenlegi alapokon

### Első szakasz: egy közös munkatér

1. A `RuleEditorView` kap tematikus navigációt és négy munkafázist; a backend szerinti fülek eltűnnek a fő navigációból.
2. A `CurrentRulesView` katalógusa adja a szabálylistát és a közvetlen kapcsolatokat. A globális projektsáv a Projekt menübe és a Próba/Alkalmazás panelekbe költözik.
3. Egy szabálykiválasztás a megfelelő meglévő gráfszerkesztőt ágyazza a munkatérbe. Már ekkor megszűnik a kézi lapváltási kényszer, de a még technikai belső gráfok ezt jelzik.
4. Közös draftmentés és módosításjelzés; külön aktív publikáció. A Ctrl+S jelentése egységes. A játék kapcsolata és az objektumlista forrása mindig látható.

### Második szakasz: szemantikus gráfok és pontos kötés

1. Szerkesztői szabályregiszter: stabil szabály-ID, téma, cím/leírás, belépési pontok, bemenet/kimenet, ütemezés, támogatott scope, alkalmazási stratégia és tesztmegfigyelések.
2. A jelenlegi `Sources` fájl/szimbólum mutató nem mindig egyetlen szerkeszthető műveletet azonosít. A regiszter tároljon több belépési pontot és ellenőrizze az aktuális gráfhoz tartozást; kódváltozáskor elavult kötés ne szerkesszen véletlen másik metódust.
3. Képlet-, állapotgép-, készlet/áramlás- és belső algoritmusadapterek egy közös kiválasztás/visszavonás/ellenőrzés szerződéssel. Átnevezés és portmagyarázat külön metaadat, a futó gráf jelentése változatlan.
4. Az első szemantikus adapterek a konkrét, könnyen ellenőrizhető útkopás-, rakodás-, növekedési és vízválasz-szabályokhoz készülnek. Nem kell megvárni az összes belső szintaktikai elem leképezését; a maradék teljes gráfja elérhető.
5. Az egyedi objektumkötés létrehozása explicit; a támogatás és az effektív alapérték a meglévő hook/futtató alapján jelenik meg. Ahol új scope-támogatás szükséges, azt a futtatóban is meg kell valósítani.

### Harmadik szakasz: összehasonlítás és visszaigazolt aktiválás

1. A `RuleProjectCli`/`AlgorithmBuild` próbahívásai háttérfeladatként, a teljes munkapéldányból futnak. Eredményük verzióhoz és induló checkpointhez kötött.
2. Alapverzió és módosított verzió összehasonlító trace-e, tematikus mérőszámokkal. A teljes runtime-verzió minden próbaeredményben szerepel.
3. A `RuleProjectLink`/`GraphRuntimeHost` publikálási és újraindítási állapotai emberi nyelven megjelennek. A „játékban fut” állapothoz projektverziót is igazoló átvételi üzenet szükséges; egy friss objektumkatalógus időbélyege nem elég.
4. A közös változáslista és gráfazonosítók a CLI/AI módosításokat is összevonják az editor állapotával; ütköző verziót nem írunk felül csendben.

## Ellenőrizhető használati célok

- Egy új felhasználó megtalálja a fanövekedést és megmutatja a vízválasz kapcsolatát anélkül, hogy fájlt vagy backend-lapot kellene választania.
- Egy útkopási számot megváltoztat, elmenti a munkapéldányt és helyesen megmondja, hogy a játékban még az előző szabály fut.
- Egy teherautó lapján felismeri az effektív típusszabályt és explicit módon készít egyedi felülírást; a változást nem keveri az egész flottára ható módosítással.
- A próbalapról megmondja, melyik világból, melyik szabályverzióval és mennyi ideig futott a teszt.
- Egy belső algoritmus módosítása előtt látja, hogy fordítás/újraindítás szükséges. Sikertelen fordításnál a munkapéldány megmarad, a játékban futó verzió nem lesz hibásnak publikálva.
- Összecsukott műveletből eljut a teljes gráfig; visszatérve nincs elveszett csomópont, kapcsolat vagy hatókör.

## Kattintható terv

Két belépési mód mutatja ugyanazt a javasolt rendszert: **Tematikus műhely** (ajánlott, mindennapi munkához) és **Vezetett indulás** (szándékból induló segítség). A vezetett mód később átadja a munkát ugyanannak a műhelynek; nem külön editor.

A bemutató nem kapcsolódik a játékhoz. A képletszorzó és a választható képernyők szerkesztési minták. A próbát és az alkalmazást csak megjelenítési szinten szemlélteti; nem állít ténylegesen mért játékhatást vagy végrehajtott alkalmazást.
