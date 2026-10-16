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

Aszfalt, makadám, földút és közelítőnyom egy táblázatban: építési költség,
időjárási romlás szárazon és esőben, gördülési ellenállás, javítási arány,
benövési idő. Az alkalmazás után a futó játékra is azonnal hatnak. A földút járhatóságát
a Szabályok fül képlete adja a talaj és a talajvíz alapján.
A forgalmi kopás burkolati hatása a közös áthaladási gráfon szerkeszthető.

## Szabályok

### Gráfos hatásszerkesztő

Az áthaladási kopás is a közös szabálykönyvből fut:
**Szabályok → Forgalmi út- és nyomkopás**. Az **Áthaladás** objektumhoz kapcsolt
Jármű, Út, Nyom és Csempe mezői választhatók; a hatás egy belépésre értendő.
A tömeg, rakomány, burkolat, vályúmélység és a pihenőidő nullázása ugyanazon
gráfon szerkeszthető, képletírás nélkül. Az előnézet mellett naplózott áthaladás
is indítható a tesztvilágban. A külön útkopásgráf és viselkedésfelülírás megszűnt.
Részletek: [forgalmi szabályok](traffic-rules.md).

**Szabályok → Időjárási útromlás**. Egyetlen szerkesztő van: balra a szabályok,
középen a kapcsolatok gráfja, jobbra a kijelölt hatás beállításai. Az általános
hangolási számok a **Típusok → Játékparaméterek** alatt találhatók; nincs külön
Képletek, Objektumok, Ok-okozati térkép vagy Gazdaság szabályszerkesztő lap.

**A szabálykönyv minden szabálya a projekt saját szabálya.** Az ide már átvezetett
folyamatokhoz a játék csak kezdő mintákat ad (vízáramlások, útromlás, nyomok, tönkök, aszálykár). Egy új
projekt ezekkel indul; ugyanúgy szerkeszthetők, kikapcsolhatók, másolhatók és **törölhetők**,
mint bármely más szabály. A törölt mintát a projekt megjegyzi (`removedStarters`), így betöltéskor
nem kerül vissza. Az **+ Új szabály** üres szabályt vagy egy mintából készült szabályt ad
(egy törölt minta így tehető vissza). A mintából készült szabály a **Visszaállítás a mintára**
gombbal állítható vissza. A hiányzó, még nem törölt minták hozzáadódnak.
A játék több más folyamata még natív kódból vagy külön felülírási pontból fut;
a teljes gráfos lefedettség és a szétosztott végrehajtás hiányait a
[2026-10-16-i audit](rule-runtime-audit.md) részletezi.

**A szabálysémában feltárt mezők elérhetők.** Egy szabály forrásként olvashatja
a hatókör objektumának és a hozzá kapcsolt objektumoknak (például a csempének) minden mezőjét,
a hatókör saját mezőit (`saját.…`), a világ mezőit (**Világ · Eső**, `world.rain`), a
rendszermodell készleteit (`stock.…`) és a játékparamétereket. Célként minden állapotmező
írható: a csempe felszíni, gyökérzóna- és mélyvize (a vízmérleg könyveli a változást), a fa
magassága és átmérője (innen nő tovább), az egészség, a stresszévek, az út állapota, a nyom és
a tönk állapota, valamint minden saját mező. Csak olvashatók maradnak a játék által minden
lépésben újraszámolt értékek (például a fa fénye és vize, a vízellátottság, a lombborítás, az
időjárás); ezeket egy szabály úgysem tudná tartósan megváltoztatni. A választók mindezt a
kódból felderített sémából listázzák: új mező vagy objektum a kód bővítésével magától megjelenik.

**Sok objektum egyszerre: összesítések.** Egy objektum gyűjteményeit (a csempén álló fák és
tönkök, a négy szomszéd csempe) a szabály összesítve olvassa: **darabszám**, valamint minden
számmezőből **átlagos**, **legkisebb**, **legnagyobb** és **összes** érték. Például
`tile.trees.avg.height` (**Csempe › Fák a csempén: átlagos magasság**) egy fa szabályában az
állomány átlagmagassága, `neighbours.avg.rootWater` egy csempe szabályában a szomszédok átlagos
gyökérzóna-vize. A választó ezeket külön csoportban kínálja. A kódban egy
`[RuleCollection]` jelölésű `IEnumerable<…>` tulajdonság elég ahhoz, hogy egy új gyűjtemény
összes összesítése megjelenjen.

**Áramlások: készletek és folyamok.** A szimuláció rendszerdinamikai modell: a készletek
(például a csempe felszíni, gyökérzóna- és mélyvize, a fa mérete, saját mezők) között
**áramlások** visznek át mennyiséget. Egy célnál a **Hogyan hasson a célra?** alatt az
**Áramoltat egyik készletből a másikba** választásával a hatás áramlás lesz:

- **Honnan?** egy írható készlet (a sajátja vagy egy kapcsolt objektumé, például egy fa
  szabályában a **Csempe · Gyökérzóna vize**), vagy *kívülről* (beáramlás, például eső).
- **Hová?** egy másik készlet, a *rendszeren kívülre* (elnyelő, például párolgás), vagy egy
  gyűjtemény tagjaiba (**Szomszéd csempék › Felszíni víz**), **egyenlően** vagy **lejtés
  szerint** (a **Vízszint** szerint alacsonyabb szomszédok kapnak, annál többet, minél lejjebb vannak).
- Az erősséget ugyanúgy a kapcsolatok és görbék adják, mint bármely hatásnál (évente vagy lépésenként).
- A forrásból legfeljebb annyi fogy, amennyi van, a célba legfeljebb annyi kerül, amennyi elfér;
  ami az egyikből elfogy, a másikba kerül, így a zárt modell megőrzi a mennyiséget. A vízkészletek
  írását a vízmérleg könyveli.
- Minden áramlásnak neve van; a játék összegzi, mennyit vitt át (a panel mutatja), és a mentés megőrzi.

**A vízháztartás áramlásokkal.** A csempe vízkészletei (lombkorona-víz, felszíni víz, hó,
gyökérzóna- és mélyvíz) csak a projekt induló szabályainak áramlásain keresztül változnak; a
natív vízlépés már nem mozgat vizet. Átállított szabályok (`ForesTycoon.Rules/WaterRules.cs`):

| Szabály | Áramlások |
| --- | --- |
| `water.precipitation` Csapadék és források | kívülről → lombkorona (borítás × telítetlenség), → felszín (a többi eső), → hó (hóesés), → felszín (forrás) |
| `water.snowmelt` Hóolvadás | hó → felszín, 0,5 mm/óra fagypont fölötti fokonként |
| `water.infiltration` Beszivárgás | felszín → gyökérzóna a talaj beszivárgási képessége szerint (burkolva 0,5 mm/óra), telítettségig |
| `water.evaporation` Párolgás | lombkorona, felszín → ki; gyökérzóna → ki: E × vízellátottság × 0,65 × (1 − borítás) |
| `water.uptake` Fák vízfelvétele | gyökérzóna → ki: E × 0,8 × LAI × vízellátottság (burkolatlan csempén) |
| `water.drainage` Mélybe szivárgás és alaplefolyás | gyökérzóna → mélyvíz a többletvíz és a drénezés szerint; mélyvíz → ki 0,15 mm/óra |
| `water.runoff` Felszíni lefolyás | felszín → szomszédok felszíne lejtés szerint (vízszint); kifolyóknál felszín → ki |

Az erősségek óránkéntiek (a környezet órája). Ezek a számok játékbeli értékek: a korábbi natív
kód sorrendjét (eseményhatárokra bontott időlépés, cellánkénti egymásutániság) a szabálylépés nem
követi, ezért az eredmény nem bitre azonos vele. A vízmérleg zárt marad: minden kívülről érkező
és kilépő áramlást a szabálycsere könyvel; ezt a `WaterRulesTests` ellenőrzi. Natív kódban marad a
szél általi hóátrendezés (`water.snowdrift`) és a növekedési vízválasz (`water.stress`, képletként
felülírható), mert az utóbbi egy fogyasztó által kért származtatott érték, nem készlet.

**Járművek.** A **Típusok › Járművek** lapon szerkeszthetők a rönkszállító, a processzor és a
forwarder műszaki adatai (tömeg, teljesítmény, vonóerő, menetellenállás, fogyasztás,
kapacitás, sebesség) és a karbantartás számai. A szabályok járművenként is olvassák őket:
a **Rönkszállító** objektum műszaki adatai, az **Erdészeti gép** objektum típusa, állapota,
rakománya és (írható) kopása.

A szerkesztő mindent magyarul, a tervező szavaival mutat: objektum (**Út**, **Közelítőnyom**,
**Tönk**, **Fa**) és tulajdonság (**Állapot**, **Vályúmélység**, **Lebomlás**). A kódbeli név
(`road.condition`) csak a buborékban, másodlagos jelzésként látszik. A nevek és súgók a
`ForesTycoon/World/RuleEntityTexts.cs` táblából jönnek (kulcs: `objektum.mező`,
választós mezőnél `objektum.mező.érték`); ha egy új mezőnek még nincs sora, az angol kódnév látszik.

1. **Mely objektumokon fusson?** Válaszd ki, például **Út** vagy **Fa**. Mellette látszik,
   hány ilyen objektum van most a tesztvilágban.
2. A **+ Forrás, cél vagy művelet hozzáadása** alatt a **Cél vagy művelet** választóval
   jelöld ki, mit változtasson a szabály, például **Állapot**. Bármely állapotmező vagy saját mező lehet cél.
3. A **Forrás (azonnal bekötve)** választó a kiválasztott forrást rögtön bekötik:
   *új kapcsolatként* a kijelölt célra, vagy *szorzóként* a kijelölt kapcsolathoz.
   Bekötetlen, elvesző forrásdoboz nincs.
4. A gráfon is köthetsz: **húzd** a forrás dobozát a célra (új kapcsolat) vagy egy
   kapcsolat dobozára (szorzó). Húzás helyett **kattints** a forrásra (sárga keret),
   majd kattints a célra vagy a kapcsolatra. Esc vagy üres területre kattintás: mégse.
5. Kattints egy kapcsolat dobozára; jobbra állíthatod az irányt, az erősséget, a görbét
   és a feltételeket. Egy kapcsolat törlése a **Kapcsolat törlése**, egy célé az összes
   kapcsolatával a **Cél törlése**, egy forrásé a **Forrás eltávolítása** gomb.

**Szövegesen.** A gráf fölött a szabály magyar mondatokban olvasható, például:
„Minden út esetén az állapot évente „Aszfalt romlása szárazon” (most 0,05) értékkel csökken,
minél nagyobb a nedvesség, annál erősebben (0: 1×, 1: 2×), ha a burkolat aszfalt.”
A mondatok a mentett adatból készülnek, minden szerkesztéssel azonnal frissülnek; a
kijelölt kapcsolat mondata világos, és a jobb oldali panel tetején is látszik.

**A gráf olvasása.**

- *Forrás* doboz: felül az objektum és a tartomány (**Csempe · 0–1**, **Út · aszfalt / makadám**),
  középen a tulajdonság, alul a hatás iránya (**+ Növeli**, **- Csökkenti**, **+ Gyorsítja**,
  **Feltétel**). A források a bekötött kapcsolatok sorrendjében állnak, így kevesebb a keresztező vonal.
- *Kapcsolat* doboz: neve, erőssége mértékegységgel és időalappal (**– 0,05 / év**,
  **+ 1 erdőév / év**, paraméternél a mostani értékkel), alatta a feltételek és szorzók száma.
- *Cél* doboz: az objektum, a tulajdonság, a hatás módja és a tartomány
  (**Út · Állapot · változtatja / év · 0–1**).
- Folytonos vonal: szorzó (zöld növel, borostyán csökkent). **Kék szaggatott vonal, kék
  csatlakozó: feltétel** – a kapcsolat csak akkor hat, ha teljesül; az erősségét nem skálázza.
- Minden vonalnak saját csatlakozója van: egy kapcsolat dobozán forrásonként egy bemenet,
  a célon kapcsolatonként egy. A vonalak sötét kontúrral futnak, így a kereszteződésben is
  követhetők. Ha egérrel egy doboz fölé állsz, a hozzá tartozó vonalak kiemelődnek, a többi
  elhalványul; húzás közben a célba vehető doboz kerete kigyullad.

**A jobb oldali panel.**

- **Hogyan hasson a célra?** *Hozzáad vagy levon*: a kapcsolatok eredménye a jelenlegi
  értékhez adódik. *Felülírja*: az új érték a kapcsolatok összege, a régi elveszik.
- **Mennyi idő alatt?** *Egy erdőév alatt* (ajánlott): az erősség egy teljes erdőévre
  vonatkozik, a játék minden lépésben az eltelt idővel arányos részét alkalmazza, így a
  játéksebesség nem változtat a hatáson. *Minden lépésben*: lépésenként (0,5 játékmásodperc)
  a teljes erősség hat; számlálókhoz, kapcsolókhoz.
- **Erősség:** *Saját szám* vagy *Játékparaméterből*. A játékparaméter a **Típusok →
  Játékparaméterek** lapon hangolható, több szabály is használhatja; a panel mutatja a mostani
  értékét. 0–1 tartományú célnál gyorsgombok adják meg, hány év alatt fusson végig a teljes skála
  (20, 10, 5, 2, 1 év).
- Ha az objektum csak egyféle (nincs kapcsolódó csempe vagy paraméter), az objektumválasztó
  rejtve marad, csak a tulajdonságot kell kiválasztani.

Az útromlás induló mintája négy kapcsolatra van bontva: aszfalt-nedvesség,
aszfalt-eső, makadám-nedvesség és makadám-eső/nedvesség. Ezek törölhetők, a forrásuk
lecserélhető, az irányuk megfordítható. Nincs rögzített bemenetlista az útromláshoz:
a választó a mindenkori objektumsémából épül. Az új szabály üres, kikapcsolt
állapotból indul. Meglévő szabályból kikapcsolt saját másolat is készíthető.

A forrásgörbe szorzót ad:
0 = nincs hatás, 1 = teljes erősség, 2 = kétszeres hatás. Az eredményt a célmező
saját tartományára korlátozza a játék. A kapcsolatok és célok sorrendje rögzített;
a későbbi célhatás már a korábbi írást látja.

A gráf alatti **Próba** az aktív tesztvilág egy kiválasztott objektumán, írás nélkül
mutatja a cél előtte/utána értékét és minden kapcsolat hozzájárulását, kerekítve és
mértékegységgel, a részek arányával. Az objektumot a leíró listából (sorszám, csempe
koordinátája, főbb tulajdonságai, például „Út 3 · (12; 40) · Állapot 0,78 · Burkolat makadám”)
vagy a mellette lévő kis térképen kattintva választod. A környezetet állandónak tekinti; a
külön forgalmi kopást és más szabályokat nem számolja hozzá.
A szerkesztés lezárásakor a projekt a tesztvilágba kerül; **Mentés** és
**Alkalmazás a játékban** viszi tovább a jelenetbe, illetve a játékba. A visszavonás
és újraalkalmazás a gráfkapcsolatokra is működik.

**Áttekintés: ki mit ír és olvas.** A szabálylista alatti bejegyzés táblázatban mutatja
minden objektum minden érintett tulajdonságát: mely szabályok írják (futási sorrendben,
a mód megjelölésével), melyek olvassák, és mit számol a játék maga (a kódbeli
`[RuleCause]` jelölésekből, például „forest.growth: nő a Fa · Egészség hatására”). Ha két
bekapcsolt szabály ugyanazt a mezőt írja, a sor (!) jelet kap: a lista szerinti későbbi
szabály a korábbi eredményét látja, felülírásnál pedig a korábbit eltünteti. Ugyanez a
figyelmeztetés a szabály mondatai alatt is megjelenik; rákattintva az áttekintés nyílik meg.

A gráf és az oldalsó panel közös `RuleEffect.Influence` adatot szerkeszt.
A `RuleInfluence` fordító ebből állítja elő a meglévő `RuleEngine` által futtatott
kifejezést. A mentett adatnak nincs külön, elcsúszható képletmásolata.
Az útromlás közvetlenül a `RoadWeatherRule` kezdő gráfjából származik. Nincs külön
paraméterkártya, útromlási képletcsatlakozás vagy natív időjárási számítás a gráf mellett.

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

Képletként szerkeszthető: **földút járhatósága** és **növekedési vízválasz** (bemenete a
`vízellátottság`, a `pangóvíz` és a `faj.szárazságérzékenység`). A vízháztartás többi része
(beszivárgás, párolgás, vízfelvétel, mélybe szivárgás, alaplefolyás, hóolvadás) már nem képlet,
hanem áramlásszabály (lásd fent); a megszűnt vízkötések nem részei a projekt formátumának.
Függvény még: `exp(x)`, `hatvány(alap; kitevő)`. A többi szabálynál a `beépített` a kódban futó
számítás, amit a képlet felülírhat vagy szorozhat.

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
majd a teljes lebomlás kiváltja a `Remove` műveletet. A lebomlás a mentett állapotból és a projekt szabályaiból fut; külön életkor-alapú tartalék számítás nincs.

A hatás módja lehet **Állítsa erre az értékre** vagy **Változtassa az értéket**. Utóbbival és éves ütemmel
felhalmozódó lebomlás építhető, például a `Tile → Moisture` görbéjéből. A lebomlási állapot a tönkkel együtt mentődik.
A művelet akkor fut, ha az összesített feltételérték pozitív; eltávolítás után az adott objektumon nem fut további hatás.
Az előnézet az eltűnést csak jelzi, nem hajtja végre.

A mezőválasztók először objektumot, majd annak tulajdonságát kínálják. Az olvasható megjelenítési név
az automatikusan felderített sémából származik; az eredeti angol mezőazonosító a súgóban megmarad.

### Kipróbálható példa: nedvesség és tönkátmérő

A nedvesség–átmérő szabály a játék alapértelmezett tönklebomlása. A **Tönkök lebomlása** szabálynál a **Visszaállítás a mintára** gomb
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
A mentett, egyedileg szerkesztett szabályokat a játék nem cseréli le kezdő mintákra.

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

A nyomszabály közvetlenül a projekt gráfos adataiból fut. A mezők a kódban felderített angol neveket használják; nincs korábbi mezőneveket átíró vagy képleteket átalakító betöltési lépés.
A **Visszaállítás a mintára** gomb a teljes nyomszabályt visszaállítja a kezdő mintára.
Az előnézet nem módosítja a nyomot; a tesztvilág és a játék a mentett gráf hatásait futtatja.

A művelet feltételei külön lenyitható összefoglaló sorokban jelennek meg. A kapcsolat súlya és iránya
szintén lenyitható, így az eltűnés feltételei kerülnek előtérbe. Új kiváltó kapcsolat kezdetben inaktív:
összeállítás után a **Kapcsolat aktív** jelölővel indítható el.

### A korábbi szabályalakok kivezetése – 2026-10-16

Az útromlási kártya, a régi `road.weather` formula-kötés, a nyom- és tönkszabályok
átvezetése, az egyfeltételes régi hatásalak és a korábbi mezőnév-álnevek kezelése megszűnt.
Csak a jelenlegi szabályadatok, feltétellisták és kódból felderített mezőnevek futnak.
A nyitójelenet és a helyi induló projekt, a bennük lévő tömörített állapotokkal együtt,
már ebből a formátumból áll. A vízállapot csak a használt készletek és származtatott állapot rastereit tárolja.
A korábbi alakú, még át nem tisztított projekt- és mentésfájlokhoz nincs automatikus szabályátvezetés.

### Folytatható tesztvilág és mentési határ – 2026-10-16

A játék és a tesztvilág a teljes szimuláció nagy munkáit képkockák között
folytatja: a szabályok mellett a környezetet, az erdőhónapot és a logisztikát is.
Egy tick műveleteinek sorrendje megmarad, csak a vezérlést adja vissza közben.
A szabálylista fa-, tönk-, út- és gépszabályai is megállhatnak munkaszelet végén.

Az **Egy tick**, **1 óra előre** és **1 nap előre** gomb munkát kér az ütemezőtől;
nem futtatja végig szinkron módon a teljes kért időt. Amíg egy tick tart, a
gráfos előnézet várakozik. A mentési kérés a következő kész ticknél teljesül;
a motor nem sietteti korlátlan befejezéssel. A szünet a folyamatban lévő tick
után állítja meg a további lépéseket. Nagy terhelésnél az időhátralék megmarad.

A szabályprojekt cseréje után szükséges teljes erdőráta-frissítés is szeletelt;
a még el nem indult frissítést a mentés megőrzi. Az új futtató azonosítója
`forestycoon-simulation/2026-10-16.3`. Régi futtatóhoz tartozó puszta parancsnaplót
vagy történeti naplóvéget nem játszunk vissza új műveleti sorrenddel. A korábbi
engedélyezett teljes checkpointok is csak támogatott szabálykötésekkel tölthetők
be: a megszűnt külön útkopási modell és kötések nem futnak tartalék útvonalon.

A pályát módosító nagy műveletek és a tesztvilág újraépítése is folytatható.
Közben a szerkesztő a munkafázist mutatja; a világ cseréje előtt a pályaépítés
megszakítható. A jelenet ellenőrzése/mentése háttérben készül. A mentési kérés
félkész ecset vagy útépítés esetén is a teljes művelet végéig várakozik.


## Fák egészsége, stressze és elhalása

A Szabályok listában a **Fák egészsége**, **Fák tartós stressze**, **Fák elhalása**
és **Elhalt fák eltűnése** ugyanazzal a gráfos felülettel szerkeszthető. Az időzítés
„Erdőhónaponként”. A forrásválasztóban először az objektumot, utána annak
olvasható tulajdonságát válaszd; a kódnevek angolok, a feliratok magyarok.

Az egészségnél a kapcsolatok a célértéket adják, a „Célértékhez közelít” művelet
pedig megadott éves sebességgel mozgatja az állapotot. A feltételküszöb lehet
szám, másik tulajdonság vagy annak görbéje. Így például az árnyéktűrésből
állítható elő a még elfogadható erőforrásellátás. A görbék, mennyiségek,
küszöbök és a közelítési sebesség csúszkával és számbevitellel is állíthatók.
A próba előnézete külön jelzi a célérték részeit és a tényleges állapotváltozást.

A [faéletciklus leírása](tree-vitality-rules.md) tartalmazza az alapértékeket és
a havi futtatás sorrendjét. A nyers képletes nézet nem szükséges ezekhez a szabályokhoz.


## Közös szabálylista és fanövekedés

A **Fák növekedése** gráf külön átmérő-, magasság- és koronanövekedési célt
mutat. Az erősség forrása tetszőleges olvasható tulajdonság lehet, külön
állítható szorzóval. Az összesítés összeadás, maximum vagy minimum lehet;
így a korona a két növekedési kapcsolat közül az erősebbet is követheti.
Az új gyök- és inverz görbét ugyanaz a görbeszerkesztő és fordító kezeli.

A Szabályok oldalon közös listából nyílnak a világobjektumok szabályai,
a folyamatkapcsolatok, a gépek munkaciklusai, a saját készletek és áramlások,
valamint a sablonok. A korábbi képletszöveges szabálypanelt eltávolítottuk:
a meglévő kifejezések számítási csomópontokként szerkeszthetők. Puszta
megnyitásuk nem írja át az adatot; az első módosítás után a szerkesztett
gráf marad a kanonikus, teljes pontosságú tárolt program. A hiányzó kapcsolatok
javíthatók, a hibás gráf alkalmazását a közös validálás elutasítja.

A teljes szabály feltétele is csomópontokkal szerkeszthető. Az objektummezők
és a világforrások objektum → tulajdonság választóból választhatók.
A kapcsolatos szerkesztés az egyszerű hatásokra, a számítási csomópontok az
összetett aritmetikára és választásra valók; ugyanabban a szabálymunkaterületben
élnek. A folyamatkapcsolatok és globális készletáramlások futási jelentése
megmaradt: a felület egyesítése nem változtatta őket tárgyi készletátadássá.

A felső Vissza / Újra a folyamatkapcsolatokat, munkaciklusokat, készleteket
és áramlásokat is a szabályokkal együtt őrzi. A jelenet mentése és alkalmazása
minden fenti adatot egyetlen projektként kezel. Részletek:
[fanövekedés és ellenőrzés](tree-growth-rules.md).

## Termőhely és vízstressz – 2026-10-16

A Szabályok közös listájában közvetlenül választható a Termőhelyi alkalmasság,
Fafaj vízválasza, Aszály követése és Pangóvíz követése. A kapcsolatok és
műveletek számítási gráfként szerkeszthetők, képletírás nélkül. A forrásválasztó
Csempe / Fafaj / Időlépés objektumot és azon belül felderített nevű mezőt kínál;
a teljes hálót a Teljes gráf gomb mutatja meg. A játék ezeket a gráfokat futtatja,
a korábbi natív számítások törölve. Részletek: [termőhely és vízstressz](site-water-response-rules.md).

## Fény- és térverseny – 2026-10-16

A közös Szabályok listájából elérhető a szomszéd árnyékoló és térfoglaló
hatása, a fához jutó fény, a szabad növekedési tér, a fafaj fényválasza és
az erőforráskorlát. Forrást objektum és mező választásával lehet bekötni:
Vizsgált fa, Szomszéd fa, Fapár, Verseny, Környezet, Erőforrások, Fafaj.
A motor a gráfokat fapáronként is megszakítható munkában futtatja;
a régi párhuzamos képletek és a külön gyökérverseny megszűnt.
[Részletek és ellenőrzések](tree-competition-rules.md).


## Természetes erdőmegújulás – 2026-10-16

A magadás, a magadó fafaj kiválasztása, a lombterhelés válasza, a csírázás,
valamint az új csemeték száma, kezdő életkora és egészsége hét szerkeszthető
válaszgráf a közös Szabályok listában. Előbb objektumot, majd mezőt választasz;
a feltételeket és hatásokat a közös csomópontokkal építed össze. A futó játék
ugyanezeket a projektgráfokat értékeli ki, nincs külön újulatszerkesztő.
A csempe- és szomszédvizsgálat, valamint az új fák erőforrás-frissítése szeletelt.
A terjedési tér most a négy közvetlen szomszéd; a kezdő fajméret-inicializáló
még közös motorkód. [Részletek és határok](natural-regeneration-rules.md).


## Fák kezdő méretgörbéi – 2026-10-16

A kezdő magasság, törzsátmérő és koronasugár három közös válaszgráf.
Az Új fa életkora, seedelt változatossága és ültetett jelzője, a fafaj méretadatai,
valamint a korábban kiértékelt kezdő méretek kapcsolhatók be. A játék ugyanezt
futtatja a generált, festett, ültetett és természetes úton létrejövő fákra.
A meglévő fák tárolt méretét a gráfcsere nem alakítja át. A létrehozás és a
területültetés egyedenként is folytatható. [Részletek](tree-initial-size-rules.md).


## Egységes szabálykártyák – 2026-10-16

Az állapotot változtató és értéket számoló szabályok tömör, egysoros listát és
közös grafikai elemeket használnak, saját működésük megtartásával. A kiválasztott
szabály összecsukható részleteiben látszik a futási idő, a forrás, a cél/eredmény
és annak felhasználása. A válaszgráf kikapcsolásához előbb egyértelmű fix eredményt
választasz; a kártya annak következményét is kiírja, a gráf pedig megmarad.
[Részletek és ellenőrzések](unified-rule-cards.md).


## A közös szerkesztő lezárása

A Folyamatkapcsolatok ugyanazt a forrás–számítás–eredmény vásznat használja,
mint a válaszgráfok. A beépített alapfolyamatot olvasó kötés ezt külön kiírja;
a törlés a típuskötésre vagy a natív alapra áll vissza, nem állítja le a gépet.
Ezért a leltár ezeket részleges felülírásként tartja nyilván.

A **Hatás hozzáadása → Görbe az eredményhez** egy bemenettel bekötött,
szerkeszthető görbecsomópontot ad. A csomópont műveletválasztóból is felvehető.
Ugyanaz a görbecsalád-választó, pontmozgatás, számbemenet és csúszka működik,
mint az állapotszabályok hatásgörbéinél. A válasz-, feltétel-, hatás- és
globális áramlásgráfok ugyanazt a kezelőfelületet kapják. A görbe a projektben
megmarad; fordításkor a meglévő RuleFormula görbefordító bontja aritmetikai
csomópontokra. Nincs külön képlet, amelyet a felhasználónak kell szinkronban tartania.
A kibontott programra is érvényes a 256 műveletes korlát.

A globális készletáramlás előző tickből, arányos korlátozással számol.
Az objektumkészletek átadása a szabályok sorrendjében, az addig megmaradt
készletből történik. Ezt mindkét nézet kiírja; az egységes megjelenítés nem
változtatja meg észrevétlenül az átadások jelentését.

Ellenőrzés: `BehaviorCurveTests`, `ResponseRuleCardTests`, `RuleCoverageTests`,
`RuleFlowTests`, `DynamicsRuntimeTests` és az editor füsttesztje.
A füstteszt `common-response-curve.png` képe a közös görbeszerkesztőt mutatja,
és azt is ellenőrzi, hogy a gráf a játékba kerül, majd visszavonható.
