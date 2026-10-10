# Saját fagenerátor kiváltási terve

A DendroKit és Arbaro eredetű ágvázgenerátort később önálló megoldás váltja ki, miközben a jelenlegi fafajformák, részletes közeli modellek, tömör koronák és dioráma megmaradnak. **Most kizárólag tervezés történik: a jelenlegi generátor és algoritmus továbbra is aktív.** A nem kereskedelmi licenc tervezete ettől még nem lép hatályba.

## A kiváltás határa

A jelenlegi megjelenítési lánc különválasztja az ágvázat, a fa alakítását és a poligonhálót. A saját korona- és faanyag-háló megtartható, ha az eredetének ellenőrzése nem mutat átvett megvalósítást. A low-poly háló újraírása önmagában nem távolítja el a mögötte használt GPL-es ágvázgenerátort.

A 2026. október 10-i forrásállapotban a `Generation/Dendro` könyvtár 20 követett C# fájlt és 2680 sort tartalmaz. A repó teljes követett C# állománya 413 fájl és 48 783 sor. Ezek fájl- és sorszámok, nem szerzőségi százalékok. A [beépítés eredetleírása](../ForesTycoon.TreeModels/Generation/Dendro/NOTICE.md) átvett generátormagot és 16 származtatott fajpresetet rögzít. A módosítások nagy aránya nem váltja ki a még átvett vagy azokból származó részek feltételeit; a GPL 2. verziójának 2. és 6. pontja a származtatott mű és további korlátozások kérdését szabályozza. [Licencszöveg](https://opensource.org/license/GPL-2.0).

| Réteg | Jelenlegi kapcsolódás | Tervezett kezelés |
| --- | --- | --- |
| `Generation/Dendro` | `TreeImpl`, `TreeParams`, geometriai és bejárási típusok | Önálló ágvázgenerátorral kiváltani |
| `Architecture/Presets` | 16 beágyazott, Arbaro eredetű fajparaméter-készlet | Saját fajleírásokat készíteni, eredetük dokumentálásával |
| `TreeArchitecture` | GPL-generátor indítása, presetbetöltés, életfázis, fény és cache | Külső hívási szerződését megtartani; generátor- és presetkapcsolatait lecserélni |
| `TreeSkeleton` | Saját köztes vázadat és `From` adapter GPL-típusokkal | Adatmodellt és igazoltan saját ellenőrzéseket megtartani; a GPL-adaptert kiváltani |
| `TreeForm`, `TreeShapeModel` | Vázméretezés, termőhely, deformáció, színek és alakmérés | Megtartani és az új vázzal ellenőrizni |
| `TreeWoodMesh`, `LobeCrownMesh`, `DendroCrownMesh` | Törzs, ágak, zárt koronaburkok és LOD | Megtartani; a `Dendro` elnevezés önmagában nem bizonyít kóderedetet |
| GPU és dioráma | Növekedés, élő évszak, textúrák, árnyék, kontúr, utófeldolgozás | A kiváltás során nem újratervezni |

A forrásból vagy részletes átdolgozásából megtartott részekhez külön eredetvizsgálat kell. A DLL-be mozgatás, névcsere vagy alacsonyabb poligonszám nem helyettesíti ezt. Az [aktuális licencállapot](../LICENSING.md) a kiváltás elfogadásáig érvényben marad.

## Megőrzendő látvány és viselkedés

A viszonyítás a jelenlegi [nyári](../images/preview/current-summer.png), [őszi](../images/preview/current-autumn.png) és [téli](../images/preview/current-winter.png) játékbeli látvány. A teljes referenciafelvételt az első megvalósítási lépésben, még a generátor cseréje előtt kell elkészíteni.

- A 16 faj és mind a hat életfázis saját sziluettet, ágazást és változatosságot kapjon. A korona magassága, szélessége, kezdőmagassága és a törzs aránya kövesse a szimulált egyedet.
- A `LobeCrownMesh` közeli nagy koronáinak jelenlegi 384, kisebb fák 192, cserjék 128 mintapontos hálója maradjon. A gyűrűkből képzett fenyőkoronák Near körirányú felbontása továbbra is 12–24 oldal; a luc örvszintjei méretfüggők, legfeljebb nyolc szinttel. Ezek nem azonosak a teljes fa háromszögszámával.
- A tavaszi és őszi egészséges korona összefüggő maradjon. Az évszakos lombváltás nem használhat lyukasztó maszkot; a későbbi kártevőkárosodás külön funkció.
- Télen részletes lombtalan váz látszódjon a lombhullatókon. Az örökzöldek és a lombhullató vörösfenyő jelenlegi különbsége, a fajonkénti őszi színek, textúrák és hófedés maradjon.
- A világméretezés, paletta, megvilágítás, kontaktárnyék, vetett árnyék, kontúr, tilt-shift és vignetta maradjon a referencia beállításain. A generátor alakhibáját nem lehet erősebb elmosással elrejteni.
- Közelről, 256× időben is Near modell maradjon látható. A készülő modellre várakozás, előtöltés vagy évszakváltás nem okozhat durvább koronát.

Az új implementációtól azonos művészeti karaktert és összevethető alakarányokat várunk. Az azonos seedhez tartozó ágcsomópontok vagy képek bitazonosságát nem ígérjük: az új ágazási megvalósítás belső szerkezete eltérhet.

## Önálló paraméteres ágvázgenerátor

A választott irány determinisztikus, paraméteres és rekurzív ágvázépítés. A Weber–Penn modell magas szintű elvei továbbra is használhatók szakirodalmi kiindulásként; a DendroKit vagy Arbaro kódját, belső osztályrendszerét és presetértékeit nem kell átültetni. Az implementáció saját adatmodellt, generálási lépéseket és fajleírásokat kap. [Weber és Penn 1995](https://doi.org/10.1145/218380.218427).

Az új generátor a meglévő vázszerződésre illeszkedik: szülőág, szülő-csatlakozási csúcspont, elágazási rend, villásodás, ponttartomány, visszaszáradási rang, ágtengelyek, csúcsonkénti áramlási érték, lombminták és azok ágazonosítói. A `TreeSkeleton.Validate()` követelményei megmaradnak. Minden gyermekág első pontja a szülő valódi csatlakozási pontjával egyezzen; a vastagság a csúcs felé csökkenjen, a gyermek ne legyen vastagabb a csatlakozó szülőnél.

Az építés tervezett menete:

1. **Faj és életfázis feloldása.** Saját fajprofilból meghatározni a vezérhajtást, törzsek számát, korona arányát, vázágak számát és elhelyezését. A fizikai magasságot és átmérőt továbbra is az ökológiai szimuláció szolgáltatja.
2. **Törzs és főágak.** Paraméteres tengelygörbéket létrehozni, botanikai ágelhelyezéssel, örvökkel vagy villásodással. A csatlakozási pontokat még a tengelyegyszerűsítés előtt rögzíteni kell.
3. **Másodlagos ágak és gallyak.** Korlátozott mélységű rekurzióval, fajra jellemző görbülettel, lehajlással és fényválasszal felépíteni a részletes téli szerkezetet. Végtelen rekurziót kizáró csúcs-, ág- és munkakorlát szükséges.
4. **Vastagság.** A végponti terhelést visszafelé összegezni, majd az ellenőrzött saját pipe-model számítással meghatározni a sugárarányokat. A világméretre hozás a jelenlegi `TreeForm` feladata marad.
5. **Lombtámaszpontok.** A végágakhoz kötött, térben eloszló mintákat előállítani; legfeljebb a jelenlegi 160 pontot átadni a koronaképzésnek. Ezek nem kirajzolt levelek és nem a korona hálópontjai.
6. **Normalizálás és ellenőrzés.** Előállítani a `Height`, `LeafRadius`, `LeafTotal` adatokat, a visszaszáradási rangot, majd validálni és cache-be tenni az elkészült vázat.

A lombminták több rétegben és szögirányban fedjék a kívánt koronaburkot. Az ágváz módosulása nem szűkítheti a tömör koronát néhány véletlen levélpont köré. A `LeafTotal` jelentését rögzíteni kell: becsült teljes mintasokaság, amelyből a legfeljebb 160 átadott pontot képezzük; nem GPU-levélszám.

Az azonos bemenet eredménye legyen ismételhető és független a háttérfeladatok sorrendjétől. A véletlen csatornák a fa seedjéből, stabil ágútvonalból és funkcióazonosítóból származzanak; egy új gally ne változtassa meg az összes korábbi ág véletlen sorozatát. Az életfázisok közös főágazonosítókat tartsanak, ahol az alakfejlődés ezt megengedi.

## Saját fajprofilok

A profilok kezdetben kódban tárolt, típusos adatként készüljenek, például `SpeciesArchitectureProfile` néven. Így nem kell új általános XML-szabványt vagy teljes Arbaro-kompatibilitást fejleszteni. A fajadatok forrása botanikai leírás és saját művészeti hangolás legyen; a jelenlegi képek az arányok összehasonlítását szolgálják. Az új értékeket nem a régi XML-ek átnevezésével vagy mechanikus konvertálásával állítjuk elő.

| Faj | Megőrzendő szerkezet és sziluett |
| --- | --- |
| Lucfenyő | Egyenes vezérhajtás, karcsú kúpos forma, elkülönülő örvök, lefelé hajló alsó ágak |
| Jegenyefenyő | Szabályos vezérhajtás, szélesebb és tömörebb emeletek, laposabb oldalágak |
| Erdeifenyő | Fiatalon örvös és kúpos; idősen magas törzsön szabálytalan, lapuló lombtömegek |
| Vörösfenyő | Karcsú, emeletes szerkezet, finom lehajló gallyak, télen csupasz váz |
| Nyír | Karcsú törzs, nyúlánk koronatömegek, ívelt és lecsüngő végágak |
| Bükk | Sima kupola, felfelé nyíló főágak, sűrű és réteges korona |
| Kocsányos tölgy | Széles, szabálytalan karéjos tömegek, tekervényes főágak és idős villásodás |
| Kocsánytalan tölgy | A tölgyjelleg megtartása mellett fajra hangolt karcsúság és ágeloszlás |
| Csertölgy | Önálló tölgysziluett és koronaarány, külön életfázis- és fényválasz |
| Hegyi juhar | Kerekebb, felfelé nyíló koronatömegek és villás főágak |
| Kőris | Magasabb, nyitottabb ágszerkezet, jól elkülönülő vázágak; a lombhéj tömör |
| Mogyoró | Több tőből induló vessző, széles, alacsony bokortömeg |
| Galagonya | Rövid, villásodó törzs, tömör és szabálytalan bokorkorona |
| Kökény | Alacsony, sűrű, többtörzsű szerkezet, eltérő bokorsziluett |
| Bodza | Laza, széles ágeloszlás, nagyobb összefüggő lombtömegek |
| Boróka | Tömör örökzöld cserje, kúpos vagy szétterülő egyedi változatok |

A hat életfázis és három fénysáv meglévő rendszeréhez kell illeszteni a profilokat. Az erős árnyék főként az ágak számát, irányát és a korona kezdőmagasságát alakítsa; az évszak a generátor cache-kulcsát továbbra se módosítsa.

## Integráció és erőforrások

A `TreeArchitecture.Skeleton(species, seed, phase, lightBand)` hívás megmarad. A vázgyártást egy új `ProceduralSkeletonBuilder` végzi, a GPL-típusokat használó adapter helyett pedig saját `SkeletonDataBuilder` készíti a köztes adatokat. A `TreeForm` és a három saját meshépítő ezt a vázat kapja továbbra is.

A fejlesztői összehasonlító nézet a már meglévő `DendroTreeGenerator.Generate(spec, lod, skeleton)` befecskendezési pontot használhatja. A nyári és téli megjelenítés ugyanazt a vázat kapja. Az összehasonlító nézetben a régi generátor csak referenciát szolgáltat; az új alapértelmezett módra váltás külön elfogadási lépés.

A cache kulcsa egészüljön ki a generátor és a fajprofil verziójával. A jelenlegi 16 alapváltozat, 2048-as bejegyzéskorlát és 160 lombtámaszpont maradjon kezdeti keret. A LOD, az évszak és a napi időjárás ne eredményezzen új kanonikus vázat. Az elkészült adat legyen a publikálás után változatlan, háttérszálon is biztonságosan megosztható.

Az új generátor nem emelheti automatikusan a CPU/GPU memóriakeretet vagy a chunképítés időkeretét. A jelenlegi korlátokat és előtöltési prioritást meg kell tartani. A mérés az adott cache-kulcsra készülő teljes váz költségét is tartalmazza, ne csak a poligonháló idejét. A megszakítás legkésőbb két ág építése között legyen ellenőrizhető; egy félkész váz nem kerülhet cache-be.

## Átállási lépések

| Lépés | Eredmény | Továbblépés feltétele |
| --- | --- | --- |
| 1. Referencia és eredet | Rögzített bemenetek, képek, alakmérések, saját és átvett részek jegyzéke | A megtartott kód és új fajadatok eredete tisztázott |
| 2. Semleges vázépítés | Saját bemenetből készíthető `TreeSkeleton`, a meglévő mesherekkel | Topológiai, sugarazási és determinisztikus tesztek sikeresek |
| 3. Első négy faj | Luc, nyír, tölgy, bükk új vázai, minden életfázisban | Nyári tömeg és téli ágrendszer megfelel a referencia karakterének |
| 4. Teljes fajkészlet | Mind a 16 faj, életfázis, fényválasz és stabil változatosság | A teljes geometriai és képi mátrix elfogadott |
| 5. Játékbeli integráció | Valós világ, háttérépítés, mentés és 256× évszakváltás | A részletesség, szimulációs állapot és erőforráskorlátok megmaradnak |
| 6. Függőség eltávolítása | GPL-mag, presetek és runtime-kapcsolataik kikerülnek az új kiadásból | Tiszta checkoutból nincs rejtett GPL-forrás-, adat- vagy buildfüggőség |
| 7. Licenc rendezése | Friss eredetjegyzék és a saját kód tényleges licence | Minden megtartott forrás és kiadott asset felhasználási joga rendezett |

A jelenlegi generátor az első öt lépés alatt a meglévő alapértelmezett megjelenítést biztosítja. Az új mód nem lesz automatikusan alapértelmezett attól, hogy lefordul vagy gyorsabb. A váltást a művészeti és műszaki feltételek együttes teljesítése indokolja.

A történeti commitokból nem törlünk licencnyilatkozatokat, és nem nyilvánítjuk visszamenőleg saját eredetűnek az átvett kódot. A régi revíziók eredetleírása megmarad; az új kiadás külön, ellenőrizhető forrásállapotból épül. Előre generált GPL-eredetű vázak vagy átcsomagolt XML-ek becsomagolása nem elfogadott kiváltási bizonyíték.

## Elfogadási vizsgálatok

**Geometria.** Mind a 16 faj, hat életfázis, három fénysáv és mind a 16 cache-változat: 4608 kanonikus váz. Ellenőrzendő a véges koordináta, pontos szülőcsatlakozás, aciklikus hierarchia, sugárfolytonosság, lombminták érvényes ágazonosítója, determinisztikus eredmény és munkakorlát. A jelenlegi `TreeSkeletonTests`, `TreeShapeTests` és koronafedettségi tesztek invariánsai maradnak; a régi XML-importteszteket az új profilok szerkezeti és fajazonossági tesztjei váltják.

**Alakmérés.** Azonos `TreeShapeSpec` mellett összevetni a magasságot, koronasugarat, koronakezdést, szektoronkénti lombkiterjedést, törzs- és faanyagtérfogatot. A `TreeShapeModel` jelenleg tesztelt geometriai mérő; a forráskeresésben nem szerepel aktív ökológiai fogyasztója. A kiváltás ezért nem indokol gazdasági vagy növekedési szabályváltást.

**Képi összevetés.** Rögzített kamera, fény, időjárás és utófeldolgozás mellett fajgaléria és vegyes erdő, nyári, őszi és téli állapotban. Kötelező éles, dioráma nélküli ellenőrző kép is: a geometria hibája ne bújhasson a fókuszhatásba. A három LOD ugyanazon kanonikus vázból épüljön. A végső elfogadás külön vizsgálja a törzset, téli gallyakat, fenyőemeleteket és a tömör koronatömeget.

A referenciafelvétel után rögzítendő kezdeti műszaki célok: magasság és cél-koronasugár eltérése legfeljebb 1%; korona kezdőmagasságának eltérése legfeljebb a famagasság 5%-a; összes lombos sziluettterület eltérése legfeljebb 8%, a külön faképek maszkjának IoU értéke legalább 0,90. Ezek tervezett célok, nem már elért eredmények. A téli vázra az eltérő ágcsomópontok miatt külön alak- és részletvizsgálat kell. A számszerű egyezés nem pótolja a fajjelleg és a dioráma képi megítélését.

**Valódi 256× játék.** A meglévő `--capture-seasons` próba bővítésével legalább két teljes évet vizsgálni minden képkockán, majd olyan fiatal és érett állományt is futtatni, amelyben tényleges új hálópublikálás és életfázisváltás történik. Legyen közeli hideg betöltés, közelítés, forgatás és rövid eltávolodás utáni visszatérés is. A korábbi 1633 képkockás referencia végig Near volt, de nulla újraépítést jelzett: ez önmagában nem bizonyítja az új generátor modellváltásának helyességét.

**Mentés és kompatibilitás.** Betöltés után az egyedazonosító, seed, méret, kor, egészség, naptár és faanyag változatlan maradjon. Ha csak a megjelenítés cserélődik, nem kell automatikusan szimulációs runtime- vagy mentésverziót emelni; a rendercache kapjon külön generátorverziót. Ha később a vázmérés szimulációs bemenetté válik, ahhoz külön mentési és visszajátszási terv szükséges.

**Platform és teljesítmény.** A teljes headless és Windows/Linux natív csomag, fajtextúra, egyszerű/textúrás passz, kontúr és árnyék, téli lombtalanság és tavaszi/őszi tömör fedettség sikeres legyen. A teljesítménykapuk változatlan küszöbökkel, elkülönített GPU-terhelés mellett fussanak; a látvány megőrzését nem helyettesíti a jobb sebesség.

## Megvalósítási döntés

Az első későbbi fejlesztési csomag a referencia, az eredetjegyzék, a semleges vázépítő és a négy alapfaj összehasonlító nézete legyen. A jelenlegi runtime algoritmus és jóváhagyott látvány addig megmarad. A licenc bevezetése csak a teljes kiváltás és a megtartott részek eredetének ellenőrzése után következik.
