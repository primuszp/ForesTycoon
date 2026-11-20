# Fagenerálás: irodalmi áttekintés és fajmodellek

Ez a leírás a DendroKit (Weber–Penn) alapú generátor fajparamétereinek és
poligonkeretének hátterét foglalja össze. A kód: `ForesTycoon.TreeModels/Meshing/DendroTreeGenerator.cs`
és `DendroCrownMesh.cs`.

## Idős erdeifenyő fényben és árnyékban

Az idős erdeifenyő ágvázához a Glen Affric állományfotóját vettük alapul. A modellben a hosszú, ágtiszta törzs fölött néhány erős vázág oldalra nyúlik, majd felfelé fordul. A lombcsoportok az élő hajtásvégekhez kötődnek. Méretük attól is függ, hány hajtásvég esik egymás közelébe. A világos állású fák jobban szétterülnek, míg az árnyalt állásúak koronája keskenyebb. [Glen Affric, Európai erdei fafajok atlasza, 132. oldal](https://ies-ows.jrc.ec.europa.eu/efdac/download/Atlas/pdf/Pinus_sylvestris.pdf)

Az [előtte–utána képen](references/tree-crowns/idos-erdeifenyo.png) az első két oszlop ugyanazt a fát mutatja két irányból, a harmadik a nyitópálya saját koronabeállításával készült, a negyedik egy árnyalt állású példány. Minden képpár azonos nagyítású.

Hat generálási magot, két fényviszonyt és három részletességet mértünk az idős fázis előző változatához képest. Az értékek bemelegített ágváz-gyorsítótár mellett, három független Release folyamat 15 mérési körének mediánjaiból származnak.

| 36 modell egy körben | Generálási idő, korábbi → új | Lefoglalt memória, korábbi → új | Háromszög, korábbi → új |
|---|---:|---:|---:|
| Alapbeállítás | 15,51 → 10,26 ms | 17,43 → 14,11 MB | 89 934 → 77 737 |
| Nyitópálya koronabeállítása | 22,16 → 15,95 ms | 21,40 → 19,09 MB | 113 943 → 111 372 |

Mind a 72 páros összevetésben csökkent a háromszögszám. A képek a játék háromszöghálóinak szoftveres rajzai. GPU-képkockaidőt ez a mérés nem vizsgált.

## Jegenyefenyő és erdeifenyő a hat életfázisban

Az [azonos nézetű összehasonlításon](references/tree-crowns/jegenyefenyo-es-erdeifenyo.png) minden képpár ugyanazt az egyedet és méretarányt használja. A korábbi változat a külön magonc- és csemetemodellek bevezetése utáni állapot. Az ábra a játék nyári háromszöghálóit mutatja.

| Életfázis | Jegenyefenyő (*Abies alba*) | Erdeifenyő (*Pinus sylvestris*) |
|---|---|---|
| Magonc | Szélesebb, közel vízszintesen szétterülő sziklevelek, fölöttük az első tűk. | A nyúlánk szár tetején sugarasan álló karcsú sziklevelek, középen rövid, egyesével álló első tűk. |
| Csemete | Lapos oldalhajtások, a közeli nézetben oldalra fésült tűkkel. | Emelkedő oldalhajtások, a külső hajtásszakaszon tömörülő tűpárok. |
| Fiatal | Keskeny, vezérhajtásban végződő korona, oldalirányban kinyúló ágörvökkel. | Kúpos korona, felszálló ágvégekkel és szabálytalanabb körvonallal. |
| Érett | Tompuló csúcs, a felső örvök szélesedni kezdenek. | Magasan elágazó törzs, telt ágvégi lombcsoportok, a felső törzsszakaszon narancsos kéreg. |
| Idős | A korona peremén álló oldalhajtás eléri a fa legnagyobb magasságát, kialakul a gólyafészekkorona. | A felső vázágak oldalra terülnek, a lombpárnák laposabbak és az erősebb ágakon összefüggőbbek. |
| Szeneszcens | Egyenetlenebb felső perem, mélyebben ülő csúcs, felszakadozó koronaszél. | Kevesebb lombpárna, nyitottabb ágváz és a törzs tengelyétől eltolódó felső korona. |

A képi ellenőrzéshez a jegenyefenyő csíranövényét és a tátrai idős jegenyefenyők koronáit, az erdeifenyőnél négy- és húszhetes növényeket, továbbá természetes állományban álló, kifejlett fákat vettünk alapul. Az egyes fázisok jellemző formai átmeneteket jelenítenek meg. [Jegenyefenyő, szlovák dendrológiai atlasz](https://www.atlasdrevin.sk/en/druh/33-abies-alba-mill) · [Erdeifenyő, korjelzett csemetefotók](https://www.foreststart.com/gallery) · [Erdeifenyő, Európai Bizottság JRC, 132–133. oldal](https://ies-ows.jrc.ec.europa.eu/efdac/download/Atlas/pdf/Pinus_sylvestris.pdf)

A két fenyőfaj első átdolgozásakor, az idős erdeifenyő fenti finomítása előtt egy kör 216 szezonális modellből állt (két faj, hat fázis, hat generálási mag, három részletesség). A Release CPU-mérésnél azonos bemeneteket és bemelegített ágváz-gyorsítótárat használtunk. Verziónként három folyamat 15 mért körének mediánjait vetettük össze.

| 216 modell egy körben | Korábbi | Javított |
|---|---:|---:|
| Generálási idő | 38,13 ms | 32,41 ms |
| Lefoglalt memória a generáló szálon | 35,93 MB | 35,04 MB |
| Háromszögek | 182 491 | 176 656 |

Egyetlen mintánál sem nőtt a háromszögek száma. A képkockaidőre ez a CPU-mérés nem ad eredményt.

## A magonc és a csemete hajtásrendszere

A magonc főtengelyén sziklevelek, majd az első lomblevelek vagy tűk látszanak. A bükk két széles sziklevelet kapott. A tölgyeknél és a mogyorónál a sziklevelek a talajban maradnak, ezért a modelljükön a föld feletti első lomblevelek láthatók. A fenyőmagoncok szikleveleinek száma és állása fajonként különbözik. A részletekhez a szász állami erdészet csíranövény-fotósorozatát és a bochumi botanikai gyűjteményt használtuk. [Sachsenforst](https://www.sbs.sachsen.de/download/Jeder_faengt_mal_klein_an.pdf) · [Bochumi magoncfotók](https://botanik-bochum.de/pflanzenbilder_gehoelze_keimlinge.htm)

A csemete vezérhajtásán már oldalhajtások fejlődnek. A bükk levelei az oldalhajtások mentén terülnek szét, a tölgyekéi a hajtásvégek felé tömörülnek. A kőris és a bodza átellenes, összetett leveleket visel. A jegenyefenyő tűi oldalra fésültek, a vörösfenyő rövidhajtásain csomók látszanak. Közelről a levelek kétoldalról és élük felől is látható, zárt testek. Közepes és távoli nézetben a generátor néhány felülettel foglalja össze a hajtásokat. A fiatal fa kialakult koronáját a következő életfázis modellezi. [Kor szerint feliratozott fenyőfotók](https://www.foreststart.com/gallery) · [Pilisi kísérlet, természetes újulat](https://piliskiserlet.ecolres.hu/sites/default/files/OBI-seminar_2019_02_28_TinyaF_0.pdf) · [Két tölgyfaj levélalakja csemetekorban](https://www.afs-journal.org/articles/forest/pdf/2004/01/F4108.pdf)

A [négy faj előtte–utána képe](references/tree-crowns/magonc-csemete-fiatal.png) egy nézetből mutatja a magoncot, a csemetét és a fiatal fát. A [16 faj részletes tablója](references/tree-crowns/magonc-es-csemete.png) a két legkorábbi fázist mutatja. A méretarányt minden egyedhez külön választottuk, hogy egy levél vagy tűcsomó is látszódjon. Az ábrák a játék háromszöghálóiból készültek.

Azonos bemenettel 192 szezonális modellt generáltunk egy körben (16 faj, két korai fázis, két mag, három részletesség). A korábbi állapot az első koronaátdolgozás utáni verzió. A CPU- és memóriaérték a bemelegített generátor 15 ismétlésének mediánja, három folyamatpár mediánjára összesítve, macOS arm64 Release fordításban.

| 192 magonc- és csemetemodell | Korábbi | Javított |
|---|---:|---:|
| CPU-generálási idő | 12,05 ms | 4,97 ms |
| Lefoglalt memória a generáló szálon | 30,30 MB | 9,87 MB |
| Kiírt háromszögek | 92 382 | 29 800 |

A 192 hálót egyenként is összevetettük. Egyiknél sem nőtt a háromszögszám. A mind a hat életfázist tartalmazó 576 modelles benchmarkban ugyanebben az összevetésben a CPU-idő 58,63 ms-ról 49,63 ms-ra, a háromszögszám 450 447-ről 388 490-re csökkent. A mérés a modellépítés költségét mutatja. Játékbeli GPU-képkockaidőt ezen a gépen a gyorsított OpenGL-kontextus hiányában nem mértünk.

## Faj- és koronaellenőrzés, 2026. november

A [fajtabló](references/tree-crowns/fafajok-eletfazisai.png) a játék közeli, nyári modelljeit mutatja egyedenként nagyítva. Az [elegyes állomány két képe](references/tree-crowns/elegyes-allomany.png) ugyanazt a 41 egyedet mutatja közös méretarányban, azonos kamerával és fényiránnyal. Mindkettő a generált háromszögháló szoftveres rajza. A játék anyagai, textúrái és időjárási effektjei külön renderelési lépést alkotnak.

A fotókat a fajok habitusának összevetésére használtuk. A hat oszlop a játék növekedési fázisait követi. A fényképek között természetes állományok, szabadon álló arborétumi példányok és magoncok is szerepelnek, eltérő termőhelyeken. Dokumentált korú idős példányok különösen a jegenyefenyőről és a vörösfenyőről állnak rendelkezésre. [Magoncok és fiatal növények fotói](https://botanik-bochum.de/pflanzenbilder_gehoelze_keimlinge.htm) · [Európai erdei fafajok atlasza](https://forest.jrc.ec.europa.eu/en/european-atlas/atlas-download-page/)

| Faj | Mit ellenőriztünk a modellen? | Képi referencia |
|---|---|---|
| Lucfenyő | A magonc sziklevélörve fölött megjelennek az első tűk. A csemete már tűs oldalhajtásokat visel. A fiatal fa kúpos koronáján elkülönülnek az ágemeletek. | [Picea abies](https://www.atlasdrevin.sk/en/druh/36-picea-abies-l-h-karst) · [Magoncok](https://commons.wikimedia.org/wiki/Category:Picea_abies_(seedlings)) |
| Jegenyefenyő | Fiatalon kúpos, később szélesedik a felső koronaharmad. Idősen a felső ágörvek megközelítik a csúcs magasságát. | [Korona és állomány](https://www.atlasdrevin.sk/en/druh/33-abies-alba-mill) · [Dawyck és Loch Fyne](https://www.treesandshrubsonline.org/articles/abies/abies-alba/) |
| Európai vörösfenyő | A sugarasan kinyúló ágcsoportok mélyebben tagolják a körvonalat, az ágemeletek magassága körben változik. Télen a teljes ágváz jelenik meg. | [Alpesi és idős példányok](https://www.treesandshrubsonline.org/articles/larix/larix-decidua/) · [Arboretum Wespelaar](https://www.arboretumwespelaar.be/userfiles/file/pdf/180225_Coniferen_Awouters_BDB.pdf) |
| Erdeifenyő | A fiatal kúp és a magas törzsön ülő, idős korban is elkülönülő ágvégi lombpárnák közötti átmenet. | [Glen Affric állományképe](https://ies-ows.jrc.ec.europa.eu/efdac/download/Atlas/pdf/Pinus_sylvestris.pdf) |
| Közönséges boróka | A felálló változat csúcsa határozottabban keskenyedik. A faj terülő habitusa külön alakváltozat volna. | [Juniperus communis](https://www.atlasdrevin.sk/en/druh/41-juniperus-communis-l) |
| Kocsányos tölgy | Idős korban laposabb, kijjebb nyúló ágvégi lombtömegek tagolják a koronát. | [Kifejlett korona](https://www.atlasdrevin.sk/en/druh/43-quercus-robur-l) · [Bochumi fajgaléria](https://botanik-bochum.de/pflanzenbilder/Quercus_robur.htm) |
| Kocsánytalan tölgy | Az érett korona tojásdadabb, összefüggőbb felülete követi az egyenesebb ágrendszert. | [Quercus petraea](https://www.treesandshrubsonline.org/articles/quercus/quercus-petraea/) |
| Csertölgy | Az érett példányok lombtömegei kevésbé simulnak össze, kirajzolódik a szabálytalan ágrendszer. | [Quercus cerris](https://www.atlasdrevin.sk/en/druh/47-quercus-cerris-l) · [Két- és hároméves cserújulat, 4–6. ábra](https://journal.pmf.ni.ac.rs/bionys/index.php/bionys/article/download/322/253) |
| Közönséges nyír | A kifejlett korona alsó lombcsoportjai jobban követik a lecsüngő hajtásvégeket. | [Betula pendula](https://www.atlasdrevin.sk/en/druh/7-betula-pendula-l) |
| Bükk | Zárt, simább korona, oldalirányban terjeszkedő lombtömegek. | [Bükk különböző életkorokban](https://botanik-bochum.de/pflanzenbilder/Fagus_sylvatica.htm) |
| Hegyi juhar | Tömött, tojásdad vagy kupolás korona. | [Acer pseudoplatanus](https://www.atlasdrevin.sk/en/druh/89-acer-pseudoplatanus-l) |
| Magas kőris | Fiatalon keskenyebb, idősebb korban lazább, elkülönülő lombcsoportok. | [Fraxinus excelsior](https://www.atlasdrevin.sk/sk/druh/103-jasen-stihly) |
| Mogyoró | Több tősarj, felfelé szétnyíló ágrendszer. | [Corylus avellana](https://www.atlasdrevin.sk/en/druh/4-corylus-avellana-l) |
| Egybibés galagonya | Széles tojásdad, tömött cserje- vagy kisfakorona. | [Crataegus monogyna](https://www.atlasdrevin.sk/en/druh/71-crataegus-monogyna-jacq) |
| Kökény | A sűrű ágrendszert teltebb koronaközép és alacsonyabban kezdődő lomb jelzi. | [Prunus spinosa](https://www.atlasdrevin.sk/en/druh/67-prunus-spinosa-l) |
| Fekete bodza | A lombtömegek a korábbi galagonyaprofilnál kijjebb és magasabbra kerülnek a széthajló ágakon. | [Sambucus nigra](https://www.atlasdrevin.sk/sk/druh/109-baza-cierna) |

A szezonális modellépítés az évszakváltáshoz szükséges faanyagot egy menetben készíti. A lombcsoportok keresése megőrzi a legközelebbi kiindulópont távolságát. A koronafelület mintavétele a sugarat metsző lombtömegekre korlátozódik. A szín a korona meglévő felületi változatosságát követi, a háló egyes csúcspontjain egyszer számítjuk ki.

A CPU-mérés parancsa `dotnet run -c Release --project ForesTycoon -- --tree-generation-benchmark`. Egy kör 16 faj × 6 életfázis × 2 mag × 3 részletesség. Hat bemelegítő kör után 15 kör mediánja kerül a kimenetre. Az ágváz-gyorsítótár ekkor már meleg. A játékbeli GPU-képkockaidőt külön a `--vegetation-benchmark` méri, működő OpenGL-környezetben.

Az első koronaátdolgozás mérése a magonc és a csemete külön modellezése előtt, macOS arm64-en, Release fordításban, a `fd1d6c6` verzióhoz viszonyítva. A két generátort ugyanaz a mérőprogram futtatta, verziónként három külön folyamatban, váltott sorrendben. A táblázat a három futás mediánját adja.

| 576 szezonális modell egy körben | Korábbi | Átdolgozott |
|---|---:|---:|
| CPU-generálási idő | 69,89 ms | 59,54 ms |
| Lefoglalt memória a generáló szálon | 174,90 MB | 132,65 MB |
| Kiírt háromszögek összesen | 452 751 | 450 447 |

A közeli, közepes és távoli modellek mintájában egyetlen háló háromszögszáma sem nőtt. Az OpenGL-illesztőprogram ebben a mérési környezetben nem adott gyorsított rajzolási kontextust, így játékbeli képkockaidő ehhez a méréshez nem tartozik.

## 1. Eljárásos famodellek – áttekintés

| Irány | Fő művek | Lényeg | Alkalmazhatóság itt |
|---|---|---|---|
| Paraméteres, rekurzív geometria | Weber & Penn 1995; Arbaro 1.9.9 (W. Diestel); Blender *Sapling* | Szintenkénti ágszám, hossz, lehajlás, görbület, hasadás; alakfüggvény (kúpos, gömb, láng…) | **Ezt használjuk.** Gyors, determinisztikus, kevés paraméterrel fajjellegű vázat ad. |
| Átíró rendszerek (L-rendszerek) | Prusinkiewicz & Lindenmayer 1990 | Nyelvtani szabályok a növekedésre | Rugalmas, de fajhoz hangolása munkaigényes; nem nyújt többet a Weber–Penn-nél a mi felbontásunkon. |
| Botanikai, architektúra-alapú | de Reffye et al. 1988 (AMAP); Hallé, Oldeman & Tomlinson 1978 | Rügysorsok, ritmikus növekedés, 23 architektúramodell | A paraméterek **botanikai igazolására** használjuk (lásd 2. pont). |
| Térkolonizáció | Runions, Lane & Prusinkiewicz 2007 | Ágak a koronaburokban szórt „attraktor” pontok felé nőnek | Jó burokkövetés, de lassabb; a tömör koronánál a burok úgyis ránk van bízva. |
| Önszerveződő, fényvezérelt modellek | Palubicki et al. 2009; Pirk et al. 2012 (*Plastic trees*) | Rügyek közti fényverseny, árnyék- és akadálykerülés | A fényfüggés **következő lépése** lehet (irányfüggő fototropizmus a szomszédok felől). |
| Inverz modellezés | Stava et al. 2014 | Paraméterek illesztése mintafához | Későbbi kalibrációhoz (fotók / LiDAR → Weber–Penn paraméterek). |
| Lombkorona mint burok/lebenyek | Livny et al. 2011 (*Texture-lobes*) | A korona néhány zárt lebenyként írható le, apró levélgeometria nélkül | **Ezt követi a tömör korona**: levélpontokból képzett, karéjos, zárt háló. |
| Lomb-egyszerűsítés, LOD | Remolar et al. 2002; Garland & Heckbert 1997; Luebke et al. 2003 | Lomb- és felületegyszerűsítés, képernyőtérbeli hibamérték | A poligonkeretet képernyőtérbeli hibából számoljuk (3. pont). |
| Erdészeti alakmodellek | Horn 1971; Pretzsch 2009 | Egy- és többrétegű korona, árnyéktűrés; koronaméret-allometria | Fényválasz iránya és a koronaarány-ellenőrzés. |

## 2. Fajok: architektúra → Weber–Penn paraméterek

A kiinduló értékek az Arbaro presetjei (`european_larch`, `tamarack`, `ca_black_oak`,
`quaking_aspen`, `black_tupelo`, `desert_bush`). Ezeket az európai fajok
architektúrájához igazítottuk.

### Lucfenyő (*Picea abies*) – Massart-modell
Egyenes, monopodiális törzs, szabályos örvökben álló, vízszintes (plagiotróp) ágak.
Az alsó ágak lehajlanak, a csúcsuk felfelé fordul, a másodrendű gallyak lecsüngnek
(„fésűs” luc). Árnyéktűrő: árnyékban az ágak még laposabbak.
- `Shape=Conical`, egyenes törzs (`0CurveV=3`).
- `1DownAngle≈78°`, `1DownAngleV=-32` (lent lehajló, fent felálló ágak), `1CurveBack=-50` (felfelé hajló csúcs).
- `AttractionUp` negatív (−1,2 … −1,8 kor szerint): lecsüngő gallyak.
- Korona: kúpos alap, rajta **emeletek** (örvszintek), lásd 3. pont.

### Kocsánytalan/kocsányos tölgy (*Quercus petraea/robur*) – Rauh-modell, de tekervényes
A csúcsrügyek elhalása miatt a törzs a koronában néhány vastag vázágra bomlik;
széles ágszög, cikcakkos gallyak, széles, szabálytalan, idősen lapos tetejű korona.
Fényigényes: árnyékban kevés, rövid, meredek ág.
- `Shape=Hemispherical` (fiatalon `Spherical`), `0SegSplits=0,3/0,55` (érett/idős villásodás).
- **Kevés, hosszú, egyenlőtlen** vázág: `1Branches≈11`, `1Length=0,85`, `1LengthV=0,32`.
- Erős görbület-variancia (`1CurveV=120`): tekervényes ágak.
- Korona: leveles lebenyek mély karéjokkal (szűk szögablak), lapos alj és tető.

### Közönséges nyír (*Betula pendula*)
Karcsú, excurrens törzs; keskeny, tojásdad, csúcsos korona; meredek főágak, ívben
kifelé hajló, hosszú, lecsüngő vesszők. Pionír, nagyon fényigényes: árnyékban rövid,
magasan kezdődő, keskeny korona. (Az architektúramodell-besorolás a szakirodalomban
nem egységes; itt Rauh-szerű, excurrens vázként kezeljük.)
- `Shape=TendFlame` (mint az Arbaro `quaking_aspen`), `1DownAngle≈42°`.
- `2Length=0,55–0,70`, `2Curve=-50`, negatív `AttractionUp`: lecsüngő vesszők.
- Korona: hegyes csúcs, alacsonyan fekvő legszélesebb pont.

### Bükk (*Fagus sylvatica*) – Troll-modell
Sima kérgű, meredeken felfelé törő vázágak (idősen gyakran V-villás törzs), ezeken
lapos, emeletes hajtásrendszerek, amelyek sűrű, sima kupolát töltenek ki.
Nagyon árnyéktűrő: árnyékban laposabb, szélesebb, „egyrétegű” korona (Horn 1971).
- `Shape=Spherical` (fiatalon `TendFlame`), idősen `0SegSplits=0,35`.
- `1DownAngle≈38°` (fényben meredek), árnyékban +22°; `2DownAngle=72°` (lapos hajtások).
- Korona: sima, egyenletes kupola (széles szögablak).

### Cserjék
- **Mogyoró (*Corylus avellana*)**: 5–8 tőből induló, ferdén felfelé törő vessző,
  nyitott váza (`0Branches=5–8`, `0DownAngle=22°`). Korona: legszélesebb fent.
- **Egybibés galagonya (*Crataegus monogyna*)**: rövid, gyakran villás törzs,
  sűrű, kusza, gömbölyded korona (`0Branches=2–3`, `1CurveV=140`).

A `ShrubForm` választja ki; a cserje továbbra is generátor- és előnézeti funkció,
nem ültethető szimulációs faj.

### Fény
A három fénysáv a vázat módosítja (ágszám, ághossz, lehajlás, felfelé vonzás).
A fényigényes fajok (tölgy, nyír) árnyékban felfelé törnek és rövidülnek, az
árnyéktűrők (luc, bükk) ellaposodnak. Ez alakmodell, nem irányfüggő fototropizmus.

## 3. Optimális poligonszám

A korona oldalszámát a **sziluett húrhibájából** számoljuk. Egy *r* pixel sugarú kör
*n*-szöggel közelítve legfeljebb *r(1−cos(π/n))* pixelt téved; a legkisebb *n*, amelynél
ez ≤ 1 px:

    n = ⌈ π / arccos(1 − 1/r_px) ⌉

ahol *r_px* = koronasugár × a LOD finom végének nagyítása (távol 3,5, közepes 9,
közel 24 px/világegység). A gyűrűk száma a korona magasság/szélesség arányával
arányos (függőlegesen a profil simább). A luc emeleteit csak akkor rajzoljuk, ha
egy emelet ≥ 5 px magas. A törzs oldalszáma ugyanígy, 0,5 px tűréssel; a 0,6 px-nél
vékonyabb vázágak elmaradnak. Így a **kis fák kevesebb háromszöget kapnak**, a nagy
fák kerete pedig felülről korlátos.

| Fa (érett, jó fény) | Közel | Közepes | Távol |
|---|---:|---:|---:|
| Luc | 176 + 30 | 84 + 12 | 20 |
| Tölgy | 144 + 124 | 90 + 24 | 20 |
| Nyír | 176 + 32 | 84 + 12 | 20 |
| Bükk | 192 + 76 | 108 + 12 | 20 |
| Mogyoró | 60 + 60…84 | 36 + 30…42 | 16 |

(korona + fa háromszögek; csemeték: 30–108 közel.)

Mérés, 2026-10-07, Intel UHD Graphics, `--vegetation-benchmark`, 1304 fás jelenet:

| | Előtte | Utána |
|---|---:|---:|
| Átlagos háromszög/fa (128 minta, közel) | 190 | 231 |
| Jelenet előkészítése | 802 ms | 788 ms |
| Medián képkockaidő | 11,56 ms | 11,75 ms |
| 95. percentilis | 12,27 ms | 13,27 ms |
| 128 fa ismételt generálása | 10 ms | 27 ms |

A többlet a közeli koronák sziluettjére megy; a képkockaidő +2%. A mintacache most
minden lehetséges kulcsot (4 faj × 4 kor × 3 fénysáv × 32 seed + cserjék) megtart,
bejegyzésenként ≤160 levélponttal, így nagy vegyes erdőben sincs újragenerálás.

## 4. Következő lépések

1. Irányfüggő fényválasz (Palubicki 2009 / Pirk 2012): a szomszédok felőli
   árnyék a korona aszimmetriáját és dőlését is alakítsa.
2. Koronaarány-kalibráció erdészeti allometriával (Pretzsch 2009): a szimuláció
   `CrownRatio` értékei jelenleg fajonként állandók, kortól és záródástól függetlenek.
3. Lombhullató fák téli állapota (csak ágváz) és őszi színezés.
4. Inverz paraméterillesztés mintafákra (Stava 2014).

## Hivatkozások

- Weber, J., Penn, J. (1995). Creation and Rendering of Realistic Trees. *SIGGRAPH '95*, 119–128.
- Diestel, W. Arbaro – tree generation for POV-Ray, 1.9.9. http://arbaro.sourceforge.net/
- Prusinkiewicz, P., Lindenmayer, A. (1990). *The Algorithmic Beauty of Plants*. Springer.
- de Reffye, P., Edelin, C., Françon, J., Jaeger, M., Puech, C. (1988). Plant models faithful to botanical structure and development. *SIGGRAPH '88*.
- Hallé, F., Oldeman, R. A. A., Tomlinson, P. B. (1978). *Tropical Trees and Forests: An Architectural Analysis*. Springer.
- Runions, A., Lane, B., Prusinkiewicz, P. (2007). Modeling Trees with a Space Colonization Algorithm. *Eurographics Workshop on Natural Phenomena*.
- Palubicki, W. et al. (2009). Self-organizing tree models for image synthesis. *ACM TOG* 28(3).
- Pirk, S. et al. (2012). Plastic trees: interactive self-adapting botanical tree models. *ACM TOG* 31(4).
- Livny, Y. et al. (2011). Texture-lobes for tree modelling. *ACM TOG* 30(4).
- Stava, O. et al. (2014). Inverse procedural modelling of trees. *Computer Graphics Forum* 33(6).
- Deussen, O., Lintermann, B. (2005). *Digital Design of Nature*. Springer.
- Remolar, I. et al. (2002). Geometric simplification of foliage. *Eurographics 2002 Short Papers*.
- Garland, M., Heckbert, P. (1997). Surface simplification using quadric error metrics. *SIGGRAPH '97*.
- Luebke, D. et al. (2003). *Level of Detail for 3D Graphics*. Morgan Kaufmann.
- Horn, H. S. (1971). *The Adaptive Geometry of Trees*. Princeton University Press.
- Pretzsch, H. (2009). *Forest Dynamics, Growth and Yield*. Springer.
