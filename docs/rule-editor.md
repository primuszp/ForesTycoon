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
elárasztott földút nehezen járható.

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

Az elöntés kirajzolása a 3D nézetben (ma csak az editor térképén látszik); a generált
folyók forrásai; klímák és járművek a Típusokba; utak és telephelyek a Pályára; további szabályok (víz,
növekedés, egészség, útkopás) átköltöztetése képletként, a rendszerdinamikai modell
szerint; misszió (célok, kezdőállapot, események).
