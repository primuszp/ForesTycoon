# Világszerkesztő

A ForesTycoon világát az editorban építjük fel, a játék ezt futtatja. A világ csempékből
áll; a szabályok viszont a világéi: egyszer írod meg őket, és minden csempén ugyanúgy
futnak. Egy csempe csak annyit mond: milyen a talaja, magassága, vize, mi áll rajta.

```powershell
dotnet run --project ForesTycoon.Editor
dotnet run --project ForesTycoon.Editor -- --project projekt.json
```

A cél egy rendszerdinamikai modell: a **Típusok** a modell állandói (talaj, fafaj, burkolat),
a **Pálya** a kezdőállapota (domborzat, víz, talaj, erdő), a **Szabályok** az áramlásai
(mi mit változtat, miből). A játék szabályai fokozatosan költöznek ide a kódból.

Négy munkatér van.

## Pálya

A világ térképe, felülnézetben. A bal oldalon választod a réteget: **Talaj**, **Domborzat**,
**Víz**, **Talajvíz**, **Erdő**, **Utak és nyomok**, **Járhatóság**. Az első négy festhető
(a Talajvíz, az utak és a járhatóság a szimuláció eredménye).

- **Generálás:** a pálya seedből készül; az **Új seed** és a **Generálás** új térképet ad.
  A festés megmarad.
- **Méret:** 32–128 csempe oldalanként (a játék alapértéke 64). Méretváltáskor a festés
  törlődik, mert a csempék újraszámozódnak.
- **Festés:** jobb oldalt választod az ecsetet és a méretét, a térképen bal gombbal húzva
  festesz. Elengedéskor a tesztvilág újraépül a pályából.
  - *Talaj:* talajtípus.
  - *Domborzat:* emelés, süllyesztés (egy lépcső húzásonként), simítás az átlaghoz. A szomszédos
    sarkok legfeljebb egy lépcsőnyire térnek el.
  - *Víz:* a **Tó** ecset a csempéket a vízszintre süllyeszti és partot emel köréjük; a
    **Szárazföld** a vízszint fölé emel. A **Forrás** folyamatosan vizet ad a csempe
    felszínéhez (hozam mm/óra, mellette m³/óra); a **Forrás törlése** megszünteti.
    A Víz réteg a mozgó felszíni víz mélységét is mutatja, a forrásokat fehér pont jelöli.
  - *Erdő:* kezdő erdő a választott fajból (vegyes korú, természetes állomány), vagy irtás.
- **Szimuláció:** **Indítás / Szünet**, **1 óra előre**, **1 nap előre**: így látod, merre folyik
  a forrás vize, hol áll meg, hol önt ki.
- **Csempe:** rámutatva vagy kattintva jobb oldalt látszanak a csempe rétegei: növényzet,
  járhatóság, talaj, teherbírás, gyökérzóna- és mélyvíz, magasság.

### Hogyan folyik a víz?

A felszíni víz a vízfelszín szintje (terep + vízmélység) szerint folyik minden alacsonyabb
szomszéd felé: lejtőn le, sík terepen szétterül, a kiásott árokban (süllyesztett csempesor)
vízszintesen is végigfolyik, a mélyedést a pereméig tölti, a túltöltött mederből kiönt, és
felfelé soha nem folyik. A térfogat megmarad; víz csak a pálya szélén és állóvízbe távozik,
közben beszivárog és párolog. A folyók mederként viselkednek, nem nyelik el a vizet. Az
elárasztott földút nehezen járható. A játék 3D nézete a 3 mm-nél mélyebb felszíni vizet vízfelületként
rajzolja: a sekély víz világos, átlátszóbb, a mély sötétkék.

## Típusok

Három táblázat: **Talajtípusok**, **Fafajok**, **Burkolatok**. Minden paraméter mellett ott
áll, hol hat.

### Talajtípusok


| Paraméter | Hatás |
| --- | --- |
| Teherbírás (0–1) | Átázva mennyire járható rajta a földút és a közelítőnyom (`talaj.teherbírás`). |
| Termékenység (0–1) | Termőhelyi alkalmasság: a fák növekedése és egészsége. |
| Telítettség, szabadföldi vízkapacitás, hervadáspont (mm) | A talaj víztartása, elszivárgás, aszály. |
| Beszivárgás, drénezés (mm/óra) | Mennyi csapadékot nyel el és milyen gyorsan vezeti a mélybe. |
| Generálás: nedvesség, textúra | Hová kerül a típus a generált térképen. |

Új típus másolással készül; festett típus nem törölhető. A talajtípusok új világ
létrehozásakor kerülnek a játékba; a futó játék világa a régi talajjal fut tovább.

### Fafajok

A 16 játszható faj: faanyagár (eFt/m³, a malomban), növekedési ráta, legnagyobb fatömeg,
érett és legnagyobb kor, kedvelt nedvesség és magasság tűréssel, árnyéktűrés,
szárazságérzékenység, érett és legnagyobb magasság, egyedszám csempénként, fasűrűség.
A futó világra is hatnak: a fák ezután ezekkel nőnek, a fa ezen az áron kel el. Az
**Alapértékek visszaállítása** a beépített számokat adja vissza. Új fajhoz famodell is kell,
ezért egyelőre a meglévő fajok paraméterei szerkeszthetők.

### Burkolatok

Aszfalt, makadám, földút és közelítőnyom egy táblázatban: építési költség, forgalmi kopás
szorzója, időjárási romlás szárazon és esőben, gördülési ellenállás, javítási arány,
benövési idő. Az alkalmazás után a futó játékra is azonnal hatnak. A földút járhatóságát
a Szabályok fül képlete adja a talaj és a talajvíz alapján.

## Szabályok

### Gráfos hatásszerkesztő

**Szabályok → Időjárási útromlás**. Egyetlen szerkesztő van: balra a szabályok,
középen a kapcsolatok gráfja, jobbra a kijelölt hatás beállításai. Az általános
hangolási számok a **Típusok → Játékparaméterek** alatt találhatók; nincs külön
Képletek, Objektumok, Ok-okozati térkép vagy Gazdaság szabályszerkesztő lap.

1. Válassz objektumtípust, például `road` vagy `tree`. A kódból felderített mezők közül
   választhatsz; csak írható mező lehet cél.
2. A **+ Forrásmező** választóval tegyél egy mezőt a gráfra. A **+ Célmező** választóval
   jelöld ki, mit változtasson a szabály, például `condition`.
3. Húzd a forrás dobozát a célra. Létrejön egy új kapcsolat; nem kell képletet írnod.
4. Kattints a középső hatásdobozra. Jobbra válaszd a növelést vagy csökkentést,
   az erősséget, az egyenes/lágy/küszöbös görbét és szükség esetén a feltételt.
   A görbe pontjai húzhatók, értékei számként is megadhatók.
5. Egy másik forrást a **hatásdobozra** húzva új szorzót adhatsz hozzá. Például
   az eső hatását a nedvesség is erősítheti. Az azonos célra vezető hatások összeadódnak.
6. Egy forrás a **Forrás leválasztása**, egy kapcsolat a **Kapcsolat törlése**,
   egy cél az összes hatásával a **Cél törlése** gombbal távolítható el.

Az útromlás induló mintája négy kapcsolatra van bontva: aszfalt-nedvesség,
aszfalt-eső, makadám-nedvesség és makadám-eső/nedvesség. Ezek törölhetők, a forrásuk
lecserélhető, az irányuk megfordítható. Nincs rögzített bemenetlista az útromláshoz:
a választó a mindenkori objektumsémából épül. Az új szabály üres, kikapcsolt
állapotból indul. Meglévő szabályból kikapcsolt saját másolat is készíthető.

Az **Éves változási ütem** kapcsolóval az erősség erdőévenként értendő;
kikapcsolva minden szabálylépésben ennyi a változás. A forrásgörbe szorzót ad:
0 = nincs hatás, 1 = teljes erősség, 2 = kétszeres hatás. Az eredményt a célmező
saját tartományára korlátozza a játék. A kapcsolatok és célok sorrendje rögzített;
a későbbi célhatás már a korábbi írást látja.

A gráf alatti **Próba** az aktív tesztvilág egy kiválasztott objektumán, írás nélkül
mutatja a cél előtte/utána értékét és minden kapcsolat hozzájárulását. A környezetet
állandónak tekinti. A külön forgalmi kopást és más szabályokat nem számolja hozzá.
A szerkesztés lezárásakor a projekt a tesztvilágba kerül; **Mentés** és
**Alkalmazás a játékban** viszi tovább a jelenetbe, illetve a játékba. A visszavonás
és újraalkalmazás a gráfkapcsolatokra is működik.

Régi projekt egyedi `road.weather` felülírása megmarad. Ilyenkor látható jelzés
figyelmeztet, hogy a korábbi képlet is hat, és a **Csak az itt összeállított hatások
fussanak** gombbal kikapcsolható. Nem törlődik a régi projektadat.

A gráf és az oldalsó panel közös `RuleEffect.Influence` adatot szerkeszt.
A `RuleInfluence` fordító ebből állítja elő a meglévő `RuleEngine` által futtatott
kifejezést. A mentett adatnak nincs külön, elcsúszható képletmásolata.
A régi `RoadWeatherCard` csak kompatibilitási beolvasásra és az induló minta
előállítására szolgál; külön paraméterkártya már nincs. A natív útromlási ciklus
nem fut a gráf mellett.

### Még át nem alakított szabályok

A jelenlegi gráf numerikus állapotváltozásokat kezel, a sémában elérhető mezőkkel és
egy szint mély kapcsolatokkal. Az események és műveletek (például objektum törlése)
még nem gráfelemek. A korábbi szabálykönyv ilyen szabályai ugyanabban a listában,
a meglévő szerkesztési formájukkal maradnak elérhetők; az összes játékszabály
átköltöztetése külön, további munka.

### Képletes szabályok

A világ szabályai olvasható képletként. Bal oldalt a szabályok témánként; `•` jelöli, ahol
saját képlet fut. A kiválasztott szabálynál látszik, hol és mikor fut, mi az eredménye.

```
# Átázott, gyenge teherbírású talajon a földút nehezen járható.
1 - átnedvesedés * (1 - talaj.teherbírás)
```

- Műveletek: `+ - * /`, összehasonlítás `<` `>`, zárójel.
- Függvények: `min(a; b)`, `max(a; b)`, `abs(x)`, `ha(feltétel; akkor; különben)`,
  `korlát(x; alsó; felső)`. Az argumentumokat pontosvessző választja el.
- Tizedesvessző vagy pont; `#` után megjegyzés; `név = érték` köztes lépés. Az utolsó sor
  az eredmény.
- `beépített` a játék eddigi számítása. A szabály saját bemenetei (például `átnedvesedés`,
  `talaj.teherbírás`), valamint `idő`, `lépés`, `világ.csapadék`, `világ.egyenleg`,
  `készlet.<azonosító>` használható. A lista a képlet alatt látszik; kattintva a név a
  képlet végére kerül.

Hiba esetén a sor és az oszlop jelenik meg, elírt névnél javaslat is. A **Képlet
használata** a képletet teszi a szabállyá a projektben és a tesztvilágban. A **Vissza a
beépítettre** a játék eredeti számítását állítja vissza. Jobb oldalt próbaszámítás: a
Pálya fülön kiválasztott csempe bemeneteivel, és a járhatóságnál a képlet eredménye az
egész térképen.

A kódból már teljesen áthozott szabályok, képletként: **földút járhatósága**, és a teljes
vízháztartás: **beszivárgás**, **talajpárolgás**, **gyökérvíz-felvétel**, **mélybe
szivárgás**, **alaplefolyás**, **hóolvadás**, **növekedési vízválasz**. A képlet pontosan azt
számolja, amit eddig a kód; ezt teszt ellenőrzi. Ezek a szabályok saját bemeneteket kapnak a
csempétől: `talaj.beszivárgás`, `talaj.telítettség`, `talaj.vízkapacitás`,
`talaj.hervadáspont`, `talaj.drénezés`, `lombborítás`, `burkolt`, `óra`,
`faj.szárazságérzékenység`. Függvény még: `exp(x)`, `hatvány(alap; kitevő)`. A többi
szabálynál a `beépített` a kódban futó számítás, amit a képlet felülírhat vagy szorozhat.

## Próba

Két világ épül ugyanabból a pályából: az egyik a játék alapszabályaival, a másik a
tieiddel. Mindkettőre ugyanannyi eső esik (erősség, időtartam; 0 eső mellett csak a
források folynak). Az eredmény: a felszíni víz (elöntés) és a két járhatósági térkép a
próba végén, az átlagos járhatóság, a nehezen járható csempék aránya és az elöntött
csempék száma.

## Mentés és alkalmazás

- **Mentés / Ctrl+S:** külön munkapéldányt ír (`<projekt>.draft.json`), a játékot nem változtatja.
- **Alkalmazás a játékban:** közzéteszi a projektet (`%LOCALAPPDATA%/ForesTycoon/rules/project.json`).
  A futó játék átveszi a szabályokat és képleteket; az **új világ** a játékban ebből a
  pályából épül. A felső sor mutatja, átvette-e a játék.

A projekt egy JSON-fájl: `world` (pálya: seed, méret, talajtípusok, fafajok, talaj-,
domborzat- és erdőfestés), `tuning` (burkolatok és más hangolt számok),
`behaviors` (a lefordított szabálygráfok), `authoring.formulas` (a képletek szövege).
Az AI ugyanezt a fájlt szerkesztheti; a parancssori műveletek a
[rendszerdinamika útmutatóban](system-dynamics.md) vannak.

A projekt nem visz magával másolatot a játék kódjáról. Ha egy régebbi projektben ilyen
elavult algoritmusmásolat van, a játék figyelmen kívül hagyja, és a saját lefordított
kódjával fut.

## Ellenőrzés

```powershell
dotnet test ForesTycoon.Tests
dotnet run --project ForesTycoon.Editor -- --smoke-test
```

Képernyőképek: `artifacts/rule-editor/world-*.png`.

## Következő szeletek

A generált
folyók forrásai; klímák és járművek a Típusokba; utak és telephelyek a Pályára; további szabályok (víz,
növekedés, egészség, útkopás) átköltöztetése képletként, a rendszerdinamikai modell
szerint; misszió (célok, kezdőállapot, események).

### Közös görbeszerkesztő

A tönkök lebomlásánál és a gráf forráskapcsolatainál ugyanaz a görbeszerkesztő működik.
Mindkettőben elérhető a lineáris, küszöb-, S-, telítődő, lecsengő, optimum- és többpontos görbe.
A paramétereket pontos számbevitellel, a mellette lévő csúszkával vagy a görbe pontjainak húzásával lehet módosítani.
A csúszka a kijelzett tartományban dolgozik; számbevitellel ezen túl is megadható érték.
A többpontos görbéhez 2–16 pont adható. A bemeneteknek szigorúan növekvő sorrendben kell következniük.
A telítődő és lecsengő görbék a végpont után is közelítenek a határértékükhöz; az optimumgörbe mindkét irányban csökken.
A tönkök alapértelmezett szabálya a nedvesség és az átmérő alapján halmozza a lebomlást; teljes lebomláskor távolítja el a tönköt.

### Tönkök és műveletek a gráfban

A tönkszabály alapértelmezésben két lépésből áll: `Tile → Moisture` és `Stump → Diameter` módosítja a `Decay progress` éves növekedését,
majd a teljes lebomlás kiváltja a `Remove` műveletet. A régi, életkor-küszöböt használó szabályok
átvezetése megtartja a megadott időtartamot. Más, kézzel írt összetett régi képleteket nem írunk felül automatikusan.

A hatás módja lehet **Állítsa erre az értékre** vagy **Változtassa az értéket**. Utóbbival és éves ütemmel
felhalmozódó lebomlás építhető, például a `Tile → Moisture` görbéjéből. A lebomlási állapot a tönkkel együtt mentődik.
A művelet akkor fut, ha az összesített feltételérték pozitív; eltávolítás után az adott objektumon nem fut további hatás.
Az előnézet az eltűnést csak jelzi, nem hajtja végre.

A mezőválasztók először objektumot, majd annak tulajdonságát kínálják. Az olvasható megjelenítési név
az automatikusan felderített sémából származik; az eredeti angol mezőazonosító a súgóban megmarad.

### Kipróbálható példa: nedvesség és tönkátmérő

A nedvesség–átmérő szabály a játék alapértelmezett tönklebomlása. A **Tönkök lebomlása** szabálynál az **Alapértelmezett szabály visszaállítása** gomb
visszaállítja ezeket a szerkeszthető kapcsolatokat. A módosítás visszavonható.

- A `Decay progress` cél változást kap, éves ütemmel. Az alaperősség 1/6 teljes lebomlás / erdőév.
- A `Tile → Moisture` lineáris görbéje 0–100% nedvességhez 0,5–1,5 közötti szorzót rendel.
- A `Stump → Diameter` görbéje 15–60 cm átmérőhöz 1,5–0,5 közötti szorzót rendel. E tartományon kívül a szélső szorzó marad.
- A két szorzó ugyanahhoz a kapcsolathoz tartozik, ezért együtt módosítják a lebomlási ütemet.
- A `Decay progress → Remove` küszöbgörbe 1, azaz 100% lebomlásnál vált 0-ról 1-re.

A gráf alatti **Összehasonlítás futtatása** négy valódi tönköt helyez egy külön tesztvilágba:
20% és 80% nedvességet, valamint 15 és 60 cm átmérőt kombinálva. A kiválasztott tönkszabályt
bekapcsolva futtatja a játék szabálymotorjával, rögzített környezettel, 0,05 erdőéves lépésekben.
A táblázat az egy év utáni lebomlást és a tényleges eltávolítás idejét mutatja.
A görbék szerkesztése után a gombbal megismételhető a próba; az előző változat eredményét külön jelzés mutatja.
Ezek szemléltető paraméterek, nem mért biológiai adatok.

Az Editor smoke-tesztje a `artifacts/rule-editor/stump-decay-example.project.json` projektet,
a `stump-decay-example.world.json` kezdeti tesztvilágot, a `stump-decay-comparison.json` eredményeket
és a `stump-decay-example.png` képernyőképet is előállítja.

A gráf forrásdobozai külön megmutatják az irányt: a `Moisture` **+ Gyorsítja**, a `Diameter` **− Lassítja**
jelölést és eltérő nyílszínt kap. A jelölést az aktuális görbe határozza meg, így szerkesztés után is a tényleges irányt követi.
Az összetett, irányt váltó görbe tartományfüggő jelölést kap. A két forrás szorzói továbbra is ugyanazt az éves ütemet módosítják.
A korábban mentett, kifejezetten megadott egyedi szabályokat a játék nem írja felül; a régi életkor-küszöb szerkeszthető alakra történő átvezetése megőrzi a korábbi időtartamot.

### Nyom regenerációja és benövése

A **Nyom regenerációja és benövése** ugyanazt a gráfos szerkesztőt használja, mint az útromlás és a tönkök.
A kódból felderített objektum a `Trail`; tulajdonságai `wear` (**Rut depth**, vályúmélység) és
`idleYears` (**Idle years**, használat nélküli erdőévek). A `Tile` kapcsolat környezeti mezői is elérhetők.

Az alapértelmezett gráf három szerkeszthető hatásból áll:

- **Vályú regenerációja:** a `wear` értéke évenként 0,4-del csökken, 0 alá nem megy.
- **Használat nélküli idő:** az `idleYears` évente 1-gyel nő. A gép áthaladása továbbra is nullázza ezt az időt és mélyíti a vályút.
- **Benövés és eltűnés:** a `Remove` művelet akkor fut, ha az `idleYears` legalább a **Tuning → Benövési idő**, és a `wear` legfeljebb 0,001.

A regeneráció hatására ráhúzott forrás új görbeszorzó lesz. Például a `Tile → Moisture` csökkenő
szorzógörbéjével a nagyobb nedvesség lassítja a kisimulást; növekvő görbével gyorsítja.
A forrást, a görbét és az éves erősséget szabadon meg lehet változtatni vagy törölni.

A **Feltételek** szerkesztőben legfeljebb nyolc feltétel adható egy kapcsolathoz; mindegyiknek teljesülnie kell.
A választék: **kisebb, mint / legfeljebb / nagyobb, mint / legalább**. A határérték lehet beírt szám,
csúszkával állított érték, projektparaméter vagy másik tulajdonság. A gráf a feltételek bemeneteit is megmutatja.
Másik kiváltó okhoz külön kapcsolat adható ugyanahhoz a művelethez.

A korábbi beépített nyomszabály automatikusan átkerül a gráfos alakra, változatlan alapműködéssel.
Az eltérő kézzel írt régi képletek az angol mezőnevekre átvezetve megmaradnak; a program nem találgatja azok új jelentését.
Az **Alapértelmezett szabály visszaállítása** gomb a teljes nyomszabályt visszaállítja a közös alapgráfra.
Az előnézet nem módosítja a nyomot; a tesztvilág és a játék a mentett gráf hatásait futtatja.

A művelet feltételei külön lenyitható összefoglaló sorokban jelennek meg. A kapcsolat súlya és iránya
szintén lenyitható, így az eltűnés feltételei kerülnek előtérbe. Új kiváltó kapcsolat kezdetben inaktív:
összeállítás után a **Kapcsolat aktív** jelölővel indítható el.
