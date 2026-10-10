# Szabályrendszer editor

Az editor külön indítható projektben mutatja a jelenlegi játék szabályrendszerét és összefüggéseit,
valamint szerkeszti a játékba kötött útkopási és viselkedési egyenleteket.

## Viselkedésgráfok és objektumkötések – 2026-10-10

A **Viselkedések** fül külön futtatható dokumentumot kezel (`forest-behaviors`, 3. verzió; az 1–2. verzió is betölthető).
A szabálykatalógus inspectorából a támogatott folyamatok és paraméterek gráfja közvetlenül
megnyitható. A világkatalógus jelenleg **82 szabályt és 716 mezőkapcsolatot** ír le.

1. Válassz folyamatot, és hozz létre típusszintű gráfot. A kezdőcsomópont a beépített eredményt
   adja tovább, így az eredeti viselkedés változatlan.
2. Adj hozzá állandót, számítást vagy feltételt, majd jelöld ki a kimenetet. A kapcsolatokat
   a kimeneti és bemeneti pontokra kattintva, vagy az inspector A/B/C mezőiben szerkesztheted.
   A csomópont húzható, jobb húzás mozgatja a vásznat, a görgő nagyít. Van visszavonás/újra.
3. Az **Egyedi objektum** kapcsolóval stabil azonosítóhoz kötheted a gráfot. Az elem a
   tesztvilág listájából is választható (az első 200 találat), illetve az azonosító beírható.
   Az egyedi gráf a típusszintű gráf helyére lép, nem egymás után futtatjuk őket.
4. A próbaszámítás a valódi fordítót/futtatót használja. Az alkalmazás naplózott `SetBehaviors`
   parancs. A tesztvilág indítható, szüneteltethető és egy fix lépéssel előreléptethető.
5. A viselkedésfájl a játék **F12 → Szabálymodell** paneljén tölthető be. A katalógus és az
   útkopási fájl továbbra is külön formátum; a viselkedésfájlt a Viselkedések saját Mentés gombja írja.

| Világkötés | Cél és azonosító | Szerkesztett eredmény |
|---|---|---|
| `road.trafficWear` | út, csempeazonosító | Az útkopási gráf után keletkező állapotveszteség |
| `water.snowmelt` | csempe | Olvadék vízegyenértéke; legfeljebb a tényleges hókészlet |
| `water.infiltration` | csempe | Beszivárgás; legfeljebb a felszíni víz és a talaj szabad kapacitása |
| `forest.growth` | fa, tartós egyedazonosító | Növekedési tényező, 0–10 |
| `forest.health` | fa, tartós egyedazonosító | Egészségi célérték, 0–1 |
| `machine.pace` | forwarder vagy processzor, gépazonosító | Az al-lépésben elvégezhető munka ideje |
| `mill.process` | malom, alapcsempe-azonosító | Feldolgozott térfogat; legfeljebb a tényleges készlet |
| `tuning.<kulcs>` | teljes világ | A 48 meglévő hangolási paraméter képlete a saját érvényes tartományában |
| `route.cost`, `route.allow` | forwarder, processzor, teherautó; flotta-ID | Útszakasz költsége és engedélyezése |
| `loading.amount`, `unloading.amount` | forwarder és teherautó; processzornál csak lerakodás | Átadott térfogat a készlet és kapacitás korlátai között |
| `loading.depart` | forwarder, teherautó | Részrakománnyal indulás feltétele |
| `loading.cycleSeconds`, `loading.gripPhase`, `loading.releasePhase` | forwarder | Rönkciklus ideje, megfogási és elengedési pillanat |

A folyamatbemenetek: beépített eredmény, időlépés, szimulációs idő, folyamatfüggő állapot és
mennyiség. Vízfolyamatnál az idő másodperc, fánál erdőév; gépnél/malomnál az időlépés jármű-s,
az abszolút idő erdőév. A hangolási gráfok csak az alapértéket és állandókat használják;
egyes járműparaméterek alkalmazáskor épülnek be a fizikai specifikációba.
Az objektumazonosítók a célvilágra vonatkoznak, nem univerzális azonosítók külön világok között.

A fordító ellenőrzi a világkötést, a hiányzó bemeneteket, a köröket, az ismétlődő kötéseket és
a méretkorlátokat (512 gráf, gráfonként 256 csomópont). A feltételes csomópontnak csak a kiválasztott
ága fut. Futás közbeni nullával osztás vagy túlcsordulás a beépített eredményt hagyja érvényben,
és az editor/játék hibajelzést mutat. A kimenetekhez rögzített tartomány és készletkorlát tartozik;
a belső számítások mértékegység-ellenőrzése még nem teljes típusellenőrzés.

A 16-os mentés a viselkedési dokumentumot, a kötéseket, a gép- és teherautó-vezérlés futó állapotát,
a rönkciklus rögzített időzítését és a függő parancsokat is megőrzi;
a korábbi mentések üres viselkedési felülírással tölthetők be. A renderelő és az editor nem
ír közvetlenül világállapotot: ugyanazok a szimulációs folyamatok hívják a tiszta gráffuttatót.

**A teljes gráfos játéklogika még nincs kész.** Ez a verzió folyamatkimeneteket, paramétereket
és a gépek, teherautók szállítási vezérlését szerkeszti. A rakodás mennyisége és időzítése,
valamint az útkeresés költség- és engedélyezési döntése már gráfból jön. A fizikai készletátadás
és a Dijkstra-algoritmus végrehajtó kód marad. A terepgenerálás, az időjárási eseményválasztás,
a vadak döntései, a parancsok és a megjelenítési folyamatok belseje továbbra is natív C#.
A teljes editorhoz ezek további típusos esemény-, művelet- és állapotcsomópontokká bontása,
a rendszerek futási sorrendjének szerkesztése és grafikus objektumkijelölés is szükséges.
Ezeket a jelenlegi kimeneti felülírások nem helyettesítik.

## Gépvezérlési állapotgráfok

A **Viselkedések → Új forwarder/processzor/truck vezérlés** egy ötmásodperces várakozásból és
munkavégzésből álló mintát nyit. A vezérlés az összes ilyen gépre vagy az **Egyedi gép**
kapcsolóval egyetlen gépazonosítóra vonatkozik. Az egyedi vezérlés teljesen felülírja a típust.

- Húzd az állapotokat a vásznon; a nyilak és sorszámok az átmeneteket jelzik.
- Az inspectorban adj hozzá állapotot, válassz kezdőállapotot és műveletet.
- **Automatikus munkavégzés**: végrehajtja a már kijelölt feladatot.
- **Várakozás**: megállítja a mozgást és munkát, megőrzi a rakományt és a markolóban tartott
  rönköt. Nincs munkakopás vagy üzemanyag-fogyasztás; a megkezdett javítás folytatódik.
- **Hazatérés kérése**: a meglévő biztonságos befejezési folyamatot indítja, a rakomány
  leadása után a telephelyre tér vissza. Nem hoz létre új feladatot vagy célpontot.
- Forwarder és teherautó: **Csak felrakodás**, **Csak lerakodás**, **Csak haladás** és
  **Indulás a rakománnyal**. A fáziskorlátozó állapothoz szerkessz átmenetet, különben a
  jármű a következő munkafázisnál vár. A művelet nem teleportál és nem enged tetszőleges
  helyen készletátadást. Az indulás forwardernél befejezi a már megkezdett rönkciklust.
- Az átmenetek sorrendben vizsgálódnak. Egy átmeneten belül minden feltételnek teljesülnie
  kell; az első teljesülő átmenet nyer. Az **Előrébb** gomb módosítja a prioritást.
- Feltételek: eltelt állapotidő, rakományhatár, gépállapot vagy annak belépési eseménye,
  feladat felvétele/megszűnése, meghibásodás és javítás befejezése.

Lépésenként legfeljebb egy állapotváltás történik, az új állapot művelete az adott lépésben
érvényes. Az időzítő jármű-másodpercben jár, független a munkatempó gráfjától és a javítástól;
a kezdőállapot első géplépésében már elindul, kijelölt feladat nélkül is. Belépéskor nullázódik.
A belépési/feladatesemények egyszeri események; az első észlelés belépésnek számít.
A gráf ciklusai megengedettek, ugyanabban a lépésben nem okozhatnak végtelen átmeneti hurkot.

A vászon jelzi a futó állapotokat, az inspector példányonként mutatja az időzítőt.
Mentés–betöltés megőrzi az állapotot, az időzítőt és az előző eseménybemeneteket. Változatlan
vezérlés újraalkalmazása, képletgráf módosítása, átnevezés vagy elrendezés nem indítja újra.
A vezérlés logikájának módosítása az érintett példányt a kezdőállapotból indítja újra.
A dokumentumonkénti korlát 512 vezérlés, vezérlésenként 128 állapot, állapotonként
64 átmenet és átmenetenként 16 feltétel. Hibás állapotkötés vagy mentett állapot esetén
az ellenőrzés elutasítja a módosítást/betöltést; a meglévő világ megmarad.

### Rakodás, útkeresés és teherautók

A **Teherautó szállítási ciklus** és **Forwarder szállítási ciklus** gomb kész, szerkeszthető
haladás → felrakodás → indulás → lerakodás gráfot hoz létre. A példa 5 m³-nél kér indulást;
a küszöb és minden átmenet módosítható. A telephely, forrás és cél továbbra is a játék
utasításával rendelhető a járműhöz. Az egyedi teherautókötés a flotta stabil azonosítóját
használja, és megmarad a megközelítő, szállító és hazatérő közúti jármű lecserélésekor.
A teherautó vezérlése logisztikai lépésenként, a rakodása legfeljebb 1/30 jármű-s
al-lépésenként fut. A szállítási fázis megváltozásakor a csak egy fázist engedélyező művelet
a következő al-lépést már megállítja. A megkezdett javítás várakozás alatt is folytatódik.

Teherautó-állapotok: 0 telephely, 1 forráshoz tart, 2 felrakodik, 3 rakott fuvar, 4 lerakodik,
5 üres visszaút, 6 hazafelé tart, 7 készletre vár, 8 megszakadt hálózati útvonal.
A megszakadt hálózat eseménye nem azonos egy új út keresésekor a gráf által kizárt szakasszal.

Rakodási képleteknél az **Amount** a rakomány m³-ben, **Capacity** a kapacitás,
**Available** a forrás készlete (forwarder lerakodáskor a rakomány). Teherautónál az
alap átadás `dt × kapacitás / 3`, forwardernél legfeljebb egy fizikai rönk; a gráf ezeket
módosíthatja, de a készlet- és kapacitáskorlátot nem lépheti át. A forwarder mennyiséggráfja
legfeljebb a megfogható rönkmennyiségig érvényes. A processzor lerakását is külön képlet kezeli.
Az indulási képletnél a **State** a telítettség, a kimenet ≥0,5 jelent indulást. Üresen nem
indul; telt forwarder, elfogyott készlet, illetve hazahívás lezárja a rakodást. Teherautónál
a telt rakomány és a hazahívás nem tartható vissza ezzel a képlettel.

A forwarder ciklusideje 0,1–3600 munka-s; megfogási aránya 0,01–0,98, elengedése 0,02–0,99.
Az elengedés legalább 0,01-dal a megfogás után történik. E három érték a ciklus kezdetén
rögzül, mentéskor tárolódik, ezért ciklus közbeni gráfcsere nem ismétli meg a megfogást.
A daru animációja ugyanazokra az időpontokra van átskálázva. A mennyiségképlet megfogáskor
értékelődik ki; a már fogott rönk mennyisége utólag nem változik.

Útvonalképleteknél a **From/To** a szakasz csempeazonosítója, **Surface** a célcsempe
burkolata (0 terep, 1 aszfalt, 2 makadám, 3 nyom), **State** a sérültsége, **Amount** a
rakomány. Az engedély kimenete <0,5 esetén a szakasz kimarad; a költség 0,001–1 000 000.
A gráf nem nyithat meg hiányzó hálózati kapcsolatot. A kiválasztás az összesített költséget
minimalizálja, a teherautó dokkpárjai között is. A gépek alap költsége 1/szakasz, a teherautóé
a burkolat és nyomvályú szerinti meglévő költség. Azonos költségnél determinisztikus a sorrend.

Az útvonalszabály új útkereséskor érvényes; a menet közbeni útvonalat nem cseréli le.
A kiválasztott jármű útvonal-előnézete ugyanazt a gráfot használja. Ha nincs engedélyezett
hazavezető út, a jármű helyben vár, és a szabály/hálózat módosítása után újra próbálkozik.
Az útkeresés gráfból vezérelt szabálya nem általános útkereső algoritmusszerkesztő.

A grafikus smoke-teszt ment egy betölthető mintát:
`artifacts/rule-editor/transport-behaviors.json`. Megnyitható az editorban, majd a játék
F12-es paneljén is. A katalógus rakodási és útkeresési elemeiből közvetlenül megnyithatók
a kapcsolódó képlet- és állapotgráfok. Összesen 72 kimeneti/paraméterkötés érhető el.

## Katalógus frissessége

Fájlmegnyitáskor és a **Frissítés a játék kódjából** műveletnél a jelenlegi implementáció
adja a folyamatleírásokat, forráshivatkozásokat és kapcsolati mezőket. A korábbi dokumentum
hangolásait, útkopási gráfját, megjegyzéseit és az ismert szabályok elrendezését megőrizzük.
Ismeretlen vagy már nem érvényes hangolás hibát ad, nem kerül csendben alkalmazásra.
Az útkopási gráf és a hangolás együtt ellenőrződik a parancssorba helyezés előtt.

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

A forráskódból felmért katalógus 79 szabályt, 13 modult és 636 mezőkapcsolatot tartalmaz.
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
állapotának grafikus kijelölése és idősoros kísérletek. A Viselkedések fül már támogatja
a visszavonást és a gépvezérlési állapotokat; új általános világállapotmező vagy tetszőleges
C# folyamat létrehozását még nem támogatja.
