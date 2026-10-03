# Járművek, rakodás és környezeti szimuláció – terv

Állapot: tervezés, 2026. október 3. Ez a dokumentum nem kapcsol be új játékmechanikát. A következő megvalósítás a rakodás láthatóságára, a járműanyagokra és a kanyarodásra koncentrál; a csúszós út, fagy, aszály és tűz későbbi szakasz.

## A jelenlegi működésből igazolt megállapítások

- A `TimberCargoSystem.Available` kezdetben nulla. A kitermelés hozzáadja a faanyagot a közös készlethez.
- A `VehicleSystem.Spawn` azonnal rakodik, ha van készlet. A célvégponton lerakodik, a kezdővégpontra visszaérve újra rakodik. Nem vesz fel fát tetszőleges erdő mellett elhaladva.
- Nincs rakodási állapot, időtartam vagy felrakodási animáció. A rönkök a töltöttség alapján egyszerre jelennek meg/tűnnek el, hat látható rakományrészben. Kis pozitív töltöttségnél is legalább egy rész jelenik meg.
- A játék egy közös készletet használ, nem helyi farakásokat és kiszolgáló rakodóhelyeket. Ez a kapcsolat a térképen jelenleg nem látható.
- A modell teljes és üres változatának grafikai ellenőrzése korábban sikerült. Ez igazolja a megjelenítési út létezését, de nem bizonyítja, hogy a felhasználó adott járműve valóban kapott rakományt. A konkrét futó játék állapotát most nem olvastuk ki.
- A jármű árnyalása deriváltból számított háromszögnormált használ. A modellből betöltött simított normált csak a lombozat választja ki. A jármű és rakománya ugyanazt a felületfajtát használja; nincs önálló festék/gumi/fém/fa anyagrendszer.
- A 90 fokos csempekanyar már körívként mintavételezett. Ennek sugara körülbelül fél csempe. A megnövelt kamion tengelytávjához ez szűk; a belső kerék az Ackermann-kormányzás miatt még jobban elfordul. A jelenlegi szögkorlát körülbelül 58 fok.

## 1. Rakodás: követhető játékmenet

Első lépésként a kiválasztott jármű paneljén jelenjen meg a rakomány/kapacitás, az induló készlet, a célpont és a feladatállapot. Külön állapot legyen az üres visszaút és a „várakozás faanyagra”. Így eldönthető, hogy a hiányzó rönk készlethiány vagy grafikai hiba.

A következő állapotgép:

`Várakozik → Rakodik → Rakott menet → Lerakodik → Üres visszaút`

Rakodáskor a jármű álljon a kijelölt kezdővégponton. Az átvett mennyiséget a szimuláció foglalja le a készletből, és a rakodási folyamat egyszer, megőrzött állapottal adja át a járműnek. A hat rakományrész rövid, egymást követő animációval kerüljön a platóra. Lerakodáskor fordított folyamat történjen; a leszállított fa csak a sikeres átadáskor növekedjen. Útvonaltörlés/megszakítás és mentés/betöltés esetén a lefoglalt mennyiség sem veszhet el vagy duplázódhat meg.

Első verzióban a meglévő közös készlethez tartozó rakodóhely egyértelmű térképi jelölést kap. Később ezt helyi farakások és tényleges szállítási megbízások válthatják fel. Üres készletet ne pótoljunk láthatatlan tesztrönkökkel: a kezelőfelület mutassa a várakozás okát.

Ellenőrzés: készlet nélkül üres jármű és magyarázat; kitermelés után visszaérkezéskor felrakodás; részleges és teljes rakomány látható; többszörös járműnél anyagmérleg; mentés rakodás közben; útvonal megszakítása; egyszeri leszállítás.

## 2. Járműrajzolás: sötét sziluett és olvasható részletek

A cél a terepasztalhoz illő, enyhén illusztratív kamion. A teljes háromszöghálót nem rajzoljuk drótvázzal: az a karosszérián zajos lenne.

1. A karosszériához és kerekekhez a betöltött csúcspontnormálokat használjuk. A valóban éles anyag-/geometriahatárok élessége megmarad. Külön állítható, mérsékelt szórt és közvetlen fény biztosítsa az árnyékos oldal olvashatóságát.
2. Festék, sötét gumi, visszafogott fényű fém, sötét üveg és fa külön anyagjelleg legyen. A rönkök kapjanak világosabb vágott véget és sötétebb kérget; a rakomány ne a karosszéria anyagát használja. A választott GLB egyszerű csúcspontszínes importját ehhez anyagjelölésekkel kell kiegészíteni, nem teljes FBX/PBR importtal kezdeni.
3. Közelről sötét szürkésbarna, nagyjából 0,7–1 képpont vastag sziluettvonal, a modell kiterjesztett hátsó felületeinek rajzolásával. Mélységteszt megakadályozza, hogy a kontúr átüssön a fákon vagy a talajon. A vonalvastagság a kameranagyításhoz igazodik.
4. A fontos belső részleteket célzottan emeljük ki: ablakkeret, ajtóhatár, sárvédő, plató, rönkvégek. Anyagkontraszt az alap; külön élvonal csak a kijelölt/közeli modellnél. Az élek előre számolhatók a meshből, a kerekek saját transzformációját követve.
5. Távolról csak anyagkontraszt és egyszerű fényelés maradjon. A kontúr és részletvonalak költsége képkockánként korlátozott legyen, ne minden 100 jármű kapjon automatikusan több extra menetet.

Az eredeti színalapú mód megmarad. Az új járműkontúr kapcsolható, és a meglévő effektminőséghez igazodik. A stencil/inverted-hull megoldást kerék- és rakományillesztéseknél képen ellenőrizni kell: nem zárt mesh esetén hibás vastagodás jelentkezhet.

Ellenőrzés: rakott/üres kamion, napos/borult/viharos fény, kanyarban kormányzott kerék, közeli/távoli kamera, terep általi takarás, eredeti mód. 1/25/100 járműves benchmarkban külön mérjük az extra kontúrmenetek költségét.

## 3. Kanyarodás: csempés úthálózat, folytonos járműpálya

A csemperendszer a hálózat kapcsolatát határozza meg; nem kell meghatároznia a jármű pillanatnyi irányát. A 90 fokos topológiai fordulót nagyobb sugarú, folytonos pályaszakasszá alakítjuk.

- A minimális fordulósugár a tengelytávból, nyomtávból és megengedett belső kerékszögből származzon. A belső és külső kerék továbbra is külön szöget kapjon.
- Kanyar előtt fokozatosan épüljön fel a görbület, a kanyar után fokozatosan csökkenjen. Több csempét átfogó lekerekített átmenet legyen; a sebességet a pálya ívhossza és görbülete vezérelje.
- Ugyanaz a pályaleírás szolgálja az út grafikai ívét, a jármű pozícióját, tengelyeit és a kerék kormányzását. A jármű ne forduljon más íven, mint amit az út mutat.
- A teljes karosszéria és a hátsó kerekek által bejárt sávot ellenőrizzük az útfelülethez. A nagyobb ív nem vághat át automatikusan erdőn, vízen vagy szomszédos úton.
- Ha a meglévő útkanyar ténylegesen túl szűk, az útfelületet ki kell szélesíteni a rendelkezésre álló folyosón, vagy a nagy járműnek más útvonalat kell keresni. Önmagában a kerék rajzolt szögének levágása nem oldja meg a geometriai problémát.
- A következő útvonalverzió ívhosszal paraméterezett legyen, a változó sugarú kanyarokon is egyenletes sebességgel. Ez játékmeneti és mentésverzió-változás: a régi visszajátszást verziózott kompatibilitás védi.

Ellenőrzés: bal/jobb kanyar, S-kanyar, egymást követő szűk fordulók, emelkedő, teljes rakomány, mindkét menetirány. Kerékszögkorlát, sebességfolytonosság, útsávban maradás és kamerától független determinisztikus mozgás.

## 4. Későbbi időjárás: a környezet állapotát módosítja

A jelenlegi `WeatherVisualState` látványállapot. Később a fix időlépéses szimulációhoz tartozó `WeatherSystem`/`EnvironmentSystem` legyen a hiteles forrás. A rajzoló ennek interpolált másolatát olvassa; a grafikai időjárás kikapcsolása vagy a részecskeszám csökkentése nem szünteti meg az esőt a szimulációban.

Közös világidő és naptár szükséges: a mostani 30 másodperces erdőév és a külön vizuális időjárási ciklus nincs fizikailag összehangolva. A csapadék mennyiségét játékidőre számolt mm-ben, a hőmérsékletet Celsiusban, a vízraktárakat szintén következetes mértékegységgel tároljuk. Az időgyorsítás, szünet, mentés és visszajátszás ugyanazt az állapotot adja.

| Állapot | Következmény |
|---|---|
| Csapadék, hőmérséklet, szél, páratartalom | A környezeti változások bemenete; a vizuális intenzitás ebből képződik |
| Lombkorona által felfogott víz, felszíni víz, gyökérzóna nedvessége | Párolgás, beszivárgás, növényi vízfelvétel és lefolyás külön folyamata |
| Víztelítettség | Pangó víz, lassabb/járhatatlan földút, fafajfüggő egészségromlás |
| Tartós vízhiány | Növekedési stressz, később egészségromlás; nem egyetlen száraz képkocka váltja ki az aszályt |
| Felületi víz, hideg, fagyott víz aránya | Jégképződés/olvadás és csökkent tapadás; hideg önmagában nem jelent mindenhol jeges utat |
| Avar és holt fa mennyisége/nedvessége | Tűzveszély és rendelkezésre álló éghető anyag |

A vízmérleg egyszerű alapja: tárolt víz változása = beérkező víz − párolgás/növényi felvétel − lefolyás − mélyebb elszivárgás. A gyökérzóna és a felszíni víz külön tároló legyen. A FAO vízmérlege ehhez szakmai támpont; az erdő fafajfüggő paraméterei külön kalibrációt igényelnek, a mezőgazdasági értékeket nem másoljuk át automatikusan. [FAO: vízmérleg és vízstressz](https://www.fao.org/4/x0490e/x0490e0e.htm).

A talajmodell összekapcsolódik a meglévő hidrológiával: csapadék nem pusztán színváltozás, hanem utánpótlás a víztárolókhoz; a lefolyás szomszédos csempékhez kerül. Az anyagmérleg és a terepmódosítás utáni újraelosztás tesztelendő.

### Úttapadás – későbbi megvalósítás

Útszakaszonként a burkolat, vízfilm, jég és sár határozza meg a tapadást. Ebből vezetjük le a gyorsítás, fékezés és kanyarodás korlátait. A rakomány a tehetetlenséget és a vezetési tartalékot módosítja; egyszerű Coulomb-modellben a tömeg nem közvetlen szorzója a maximális oldalgyorsulásnak.

Első változatban a jármű előre érzékeli a csökkent tapadást, korábban fékez és kisebb ívsebességet választ. Később lehet a korlát túllépésekor tényleges oldalcsúszás, az útsáv és ütközések megfelelő kezelésével. Az esőeffektből közvetlenül számolt, mindenhol azonos csúszás kerülendő.

### Erdőtűz – a vízmodell után

Az aszály növeli a veszélyt, de nem indít automatikusan tüzet. Külön gyújtóesemény kell, például szimulációs villám. A mostani dekoratív „Villám most” gomb maradjon vizuális teszt; ne váljon véletlenül játékmeneti gyújtóeseménnyé.

A terjedés függjön az éghető anyagtól, annak nedvességétől, széltől és lejtéstől. Aktív tűzcsempéket és terjedési frontot frissítsünk, ne az egész világot minden képkockán. Égéskor csökkenjen az éghető anyag, sérüljön az erdő; az eső a nedvességen keresztül csökkentse a terjedést, ne feltétlenül oltson el azonnal minden intenzitású tüzet. A koronatűz, parázsszórás és oltási játékmenet további, külön szakasz.

Szakmai alapként a Rothermel-féle felszíni tűzterjedési modell használható egy játékra egyszerűsített változathoz. Ez nem általános koronatűz- vagy teljes légkörszimuláció. [US Forest Service: Rothermel-modell magyarázata](https://research.fs.usda.gov/treesearch/55928).

## Megvalósítási sorrend és skálázás

1. Rakománydiagnosztika, látható állapotgép és rakodás/lerakodás.
2. Járműanyagok, helyes normálok, közelről finom sötét sziluett és fontos részletek.
3. Járműmérethez illesztett útív, fokozatos kormányzás, ívhossz szerinti sebesség.
4. Közös időjárási/naptári szimuláció és menthető környezeti állapot; vízmérleg és hidrológiai kapcsolat.
5. Úttapadás, vízhiány/víztöbblet, fagy és erdőstressz.
6. Éghető anyag és nedvesség, gyújtóesemények, felszíni tűzterjedés, később oltás és regeneráció.

A környezetet tömbökben, szimulációs csempénként tároljuk. A tűz külön aktívhalmazt használ. Az időjárási események seedelt, menthető állapotúak; a változások fix szimulációs időből származnak. A renderer minőségi keretei csak a megjelenítést szabályozzák. Minden szakaszhoz anyagmérleg-, determinisztikus visszajátszás- és nagy pályás terhelési teszt tartozzon; a jelenlegi 100 teherautós benchmark a vizuális részletek költségét is mérje.

## Első megvalósított szakasz

Elkészült a három másodperces rakodás/lerakodás, a készlethiány miatti várakozás, a lépcsőzetesen megjelenő hat rakományrész, és az első nyolc jármű állapotának/rakományának kijelzése. Az új működés a közös kitermelt készletet használja; külön térképi rakodóhely és helyi farakás még nincs. Útvonaltörléskor a lefoglalt rakomány visszakerül a készletbe. Mentéskor a 2-es járműfizikai verzió rögzíti az új időzítést; régi, 0/1 verziós mentések visszajátszása a korábbi szabályt őrzi. Új világban/újrageneráláskor az új rakodás aktív.

A jármű és a kerekek a modell simított normáljait használják; külön gumi- és rönkanyag, barna kéreg és világosabb vágott végek, valamint mérsékelt festékcsillanás került be. A Nézet menüben kapcsolható Járműkontúrok közelről, magas minőségen legfeljebb nyolc, közepesen négy járműnél rajzol sötét kontúrt képenként; távolról és alacsony minőségen nincs külön kontúrmenet.

A kerék kormányzása a tengelyekből számolt karosszériairány görbületét követi, így az egyenes és a körív találkozásánál fokozatosabb. Ez vizuális finomítás; a félcsempés útív sugara még változatlan. A többcsempés nagy sugarú ív és az útgeometria közös átalakítása továbbra is a következő szakasz.

Ellenőrzés: 133 sikeres teszt; grafikai smoke, várakozó/félig rakott/teljesen rakott teherautó és kanyarbeli rajzolás képi ellenőrzése. Készlethiány, késleltetett leszállítás, részleges rakomány, útvonaltörléskor visszatérített készlet és fokozatos kormányzás külön tesztelve.
