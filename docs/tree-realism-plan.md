# Valószerűbb low-poly fák – procedurális fejlesztési terv

Kiindulás: `--dendro-tree-preview` (2026-10-08, `artifacts/dendro-trees/`).
A Weber–Penn váz (`TreeSkeleton`) jó, a gyengeség a **korona burkában** és az
**arányokban** van. A cél nem több poligon, hanem jobb sziluett és árnyalás.

## 1. Diagnózis a mostani képekről

| Tünet | Hol látszik | Ok a kódban |
|---|---|---|
| „Nyalóka/lövedék” korona: egyetlen sima, zárt test | bükk, nyír, juhar | `DendroCrownMesh`: egy forgástest-szerű gyűrűháló, `[1 2 1]` vízszintes+függőleges simítás eltünteti a lebenyeket |
| Éles, vízszintes koronaalj („szoknyaszél”) | bükk, nyír | a legalsó gyűrű után a `points[0]` pólus közel van, a korona alja lapos korong |
| A törzs vékony, hosszú, ág nem lép be a koronába | minden lombos | a vázágak a koronán belül rejtve maradnak, a koronaalap magasan kezdődik |
| Az érett fa csemetének tűnik | bükk-, tölgy-Mature | koronasugár/magasság arány kicsi, kevés koronatömeg az oldalakon |
| Lapos, egyszínű zöld | minden faj | csak magasság szerinti `0.76 + 0.27·√t` árnyalás, nincs lebenyenkénti eltérés |
| Túl szabályos luc | luc-Mature | az örvemeletek egyformák, nincs lecsüngés, csúcshajtás, aszimmetria |
| Nyír lecsüngő vesszői nem látszanak | nyír | a koronaburok ezt elsimítja; nincs „függöny” |

Ami **jó** és megtartandó: a tölgy lebenyes `OakCrownMesh` megközelítése, a
képernyőtér-hiba alapú LOD (`Sides()`), a determinisztikus seedek, a GPU-s növekedés.

## 2. Az új eljárás: vázvezérelt lebenykorona („clump crown”)

Az irodalmi alap már a `tree-generation-literature.md`-ben van (Livny 2011
*texture-lobes*, Runions 2007 térkolonizáció, Palubicki 2009). A tölgy-módszert
általánosítjuk minden lombos fajra, fajonkénti paraméterekkel.

### 2.1 Levélpontok → lebenyek (klaszterezés a váz mentén)

1. A `TreeSkeleton.Leaves` élő pontjait (`form.Dead` szűrve) **vázágankénti**
   csoportokba osztjuk: minden pont ahhoz az 1. rendű ághoz (scaffold limb) tartozik,
   amelyből ered (`LeafStem` → szülőlánc felfelé az 1. szintig).
2. Ágcsoporton belül determinisztikus k-means (k = fajonként 1–3, a csoport
   pontszámával arányos), seed = fa seed. Így a lebenyek **az ágak végén ülnek**,
   és a lebenyek közti hézagok természetesen az ágak közé esnek.
3. Lebeny = ellipszoid: középpont = klaszter-súlypont, tengelyek = a pontok
   kovarianciájának sajátvektorai (PCA), sugár = 1,15 × szórás, alsó korláttal.
   A lebeny **alja lapítva** (×0,7 Z-irányban, lefelé), a teteje kerekebb – így
   jön létre a valódi fák „felhőalja” (fényhiány miatt alul ritkább a lomb).

### 2.2 Lebeny-geometria low-poly stílusban

- Alap: ikoszaéder (20 háromszög, Near), oktaéder (8, Medium), Far-on a mostani
  egyetlen burok marad (sziluett-kompatibilis, lásd LOD-konzisztencia teszt).
- **Csúcszaj**: minden csúcs sugarát `1 ± 0,18·noise(seed, irány)` torzítja –
  ez adja a „faceted” low-poly karaktert szabályos gömb helyett.
- Lebenyek véletlenszerű elforgatása (mint most a tölgynél `turn`/`tilt`), hogy
  a facettaélek ne álljanak egy vonalba.
- Átfedő zárt hálók maradnak (nincs boolean unió); átlátszatlan anyagnál ez
  vizuálisan helyes és olcsó.
- Költségkeret: Near ≤ 12 lebeny × 20 háromszög = 240, Medium ≤ 8 × 8 = 64,
  Far egy burok ≈ 40 – a mostani `DendroCrownMesh` nagyságrendjében.

### 2.3 Látható ágak a lebenyek között

- A `TreeWoodMesh` 1–2. rendű ágait a lebeny-középpontig vezetjük (az ág végpontja
  a lebeny belsejébe fusson), és a koronaalapot a legalsó élő lebeny alá
  engedjük. Így látszik, hogy **a lomb az ágakon ül**, és a törzs–korona
  átmenet nem éles.
- A lebenyek közti résekben 1–2 csupasz ágvég kilóghat (idős, gyérülő korona,
  `Dieback` > 0) – ez erősen „valódi fa” jel.

### 2.4 Árnyalás csúcsszínnel (textúra nélkül)

Csúcsonként bake-elt szín, a mostani `Shade()` bővítve:

1. **Önárnyék/AO**: `ao = 1 − 0,35 · mélység`, ahol mélység = a csúcs távolsága a
   korona külső burkától a középtengely felé (0 kint, 1 bent). Belül sötét, kint világos.
2. **Égbolt-fény**: felfelé néző normál +12% világosság, lefelé néző −20% (felhőalj).
3. **Lebenyenkénti tónus**: lebenyenként ±6% fényerő és ±4° árnyalat-eltolás
   (seed alapján) – ez bontja meg az egyszínű foltot.
4. **Évszak és egészség**: a meglévő `form.CrownColor` mellé sárgulás a
   `Health`/`Dieback` szerint lebenyenként eltérő ütemben (nem egyszerre sárgul
   az egész fa); ez közvetlenül szolgálja a menedzsmentnézetet is, mert a beteg
   fa a 3D-ben is felismerhető.

### 2.5 Fajonkénti beállítások (`CrownLobeProfile`)

| Faj | Lebenyszám (Near) | Lebeny alak | Különlegesség |
|---|---|---|---|
| Tölgy (3 faj) | 8–12 | lapos, széles, szabálytalan | erős csúcszaj, idősen lapos tető, tekervényes, látható vázágak |
| Bükk | 6–9 | lapos, réteges (Troll-modell) | vízszintes „emeletek”: lebenyek Z-ben rétegekbe kvantálva, sima tető |
| Nyír | 5–8 | keskeny, megnyúlt lefelé | **függöny-lebenyek**: alsó lebenyek lefelé nyújtva (Z ×1,4), keskeny, laza korona, lyukas |
| Juhar | 6–9 | kerek, tömör | sűrű, kevés rés |
| Kőris | 5–8 | laza, ritkás | nagyobb lebenytávolság, több látható ág |
| Erdeifenyő (idős) | 4–7 | lapos „párnák” | magas, csupasz törzs, ernyőszerű lebenyek a csúcson |
| Mogyoró, galagonya | 4–6 | földig érő | több törzs, a talajig érő lebenyek |

### 2.6 Tűlevelűek (luc, jegenyefenyő, vörösfenyő)

A `SpruceCrownMesh` örvemeletei maradnak, de:

- **Emeletenkénti aszimmetria**: minden örv-„szoknya” 5–7 csúcsa külön sugarat kap
  (±20%), és az emelet kissé elfordul (±15°) – nem lesz „pagoda”.
- **Lecsüngés**: az emelet külső pereme lejjebb, mint a belső (alsó emeleteken
  erősebben) – a luc jellegzetes „fésűs” alakja.
- **Csúcshajtás**: vékony, egyenes, 1–2 emeletnyi keskeny kúp a legtetején.
- **Alsó elhalt ágak**: idős állományban (és sűrűn, alacsony fényben) az alsó
  2–3 emelet helyén csupasz, szürke ágcsonkok – ez a záródás vizuális jele.
- Vörösfenyő: ritkább, lyukasabb emeletek, lombhullatás ősszel (sárga, majd csupasz).

### 2.7 Arányok

- Koronaarány (`CrownRatio`) és `CrownRadius` ellenőrzése a fajok magasság–koronaszélesség
  allometriájával (Pretzsch 2009); a Mature bükk/tölgy korona most kb. 30–40%-kal
  keskenyebb a valósnál a képeken. Szabad állásban szélesebb, zárt állományban
  keskenyebb (a `ForestResources.Space` már adja ezt).
- Törzs: a gyökérnyak-kiszélesedés megvan; a törzs vastagsága a koronasúlyhoz
  igazítva (pipe model: a `TreeSkeleton.PipeExponent` 2,2 helyett fajonként 2,0–2,5).

## 3. Megvalósítási lépések

1. **`CrownLobes` modul** (`ForesTycoon.TreeModels/Meshing/CrownLobes.cs`):
   vázágankénti csoportosítás + k-means + PCA-ellipszoid. Tiszta függvény, tesztelhető.
2. **`LobeCrownMesh`**: az `OakCrownMesh` általánosítása `CrownLobeProfile` alapján;
   a `DendroTreeGenerator.Build` ezt hívja a lombosokra (a tölgy rá költözik).
3. **Csúcsszín-árnyalás** (2.4) közös segédfüggvényben (`CrownShading`).
4. **Ágak bevezetése a lebenyekbe** (`TreeWoodMesh`), koronaalap korrekció.
5. **Luc-finomítás** (`SpruceCrownMesh`).
6. **Arány-hangolás** a fajparaméterekben és a presetekben.

### Ellenőrzés

- Meglévő tesztek: `ForestLodConsistencyTests` (a Far burok sziluettje ne ugorjon),
  `ForestGrowthShapeTests`, `DendroTreeGeneratorTests`, `TreeShapeTests`.
- Új tesztek: a lebenyek determinisztikusak (azonos seed → azonos háló); a lebenyek
  befoglaló doboza a koronasugáron belül marad; háromszögkeret LOD-onként.
- Vizuális: `--dendro-tree-preview` előtte/utána képek, és egy referencia-fotó
  sziluett-összevetés fajonként (`docs/forest-reference-design.md` alapján).
- Teljesítmény: `--forest-benchmark` – a háromszögszám és a hálóépítési idő ne
  nőjön 15%-nál többel.

## 4. Állapot (2026-10-08)

Megvalósítva a 2.1–2.4 pont és a fajprofilok (2.5) első köre:

- `ForesTycoon.TreeModels/Meshing/LobeCrownMesh.cs` váltja az `OakCrownMesh`-t; minden lombos
  faj (tölgyek, bükk, nyír, juhar, kőris, idős erdeifenyő, cserjék) fiatal kortól lebenyes koronát kap.
- A lebenyek világ-izotróp térben klaszterezett levélpontokból jönnek; a vázágankénti csoportosítás
  helyett egy függőleges **gerinc** (a korona belseje, sötétebb árnyalattal) köti össze őket.
  Építéskor ellenőrizzük, hogy minden lebeny beírt teste átfedi a gerincet: nincs lebegő lomb.
- LOD-ok ugyanabból a tömegkészletből: Near = ikoszaéderek (20 △/lebeny, vízszintes facettazaj),
  Medium = oktaéderek (8 △/lebeny), Far = 3 összevont tömeg ötszögű bipiramisként (30 △).
  Kis koronák (néhány pixel) Near/Medium szinten sima, gyűrűs burkot kapnak.
- Árnyalás: belső/alsó rész sötétebb, lebenyenként ±5% tónus, égboltfény a normál Z szerint
  (zárt felületen nulla átlagú, így a LOD-ok átlagszíne nem változik).
- Új diagnosztika: `dotnet run --project ForesTycoon -- --tree-gallery <név>` →
  `artifacts/tree-gallery/<név>-{near,medium,far}.png` (8 faj × 3 életfázis).

2.6 (tűlevelűek) is kész a `DendroCrownMesh` örvemeleteiben, változatlan háromszögkerettel:
emeletenkénti elfordulás, egyenetlen és lecsüngő szoknyák (az alsó emeleteken erősebben),
keskeny csúcshajtás. A luc mellett a fiatal erdeifenyő, a vörös- és a jegenyefenyő is ezt kapja.

2.3 kész: lebenyes koronában az elsőrendű vázágak az ívhossz ~80%-áig futnak, a korona aljánál
nem vágódnak le, így a lebenyek közti résekben látszanak (keret: Near 200, Medium 80 △; cserjéknél
változatlan).

2.7 (arányok): a törzsvastagság szándékosan a szimulált átmérő (teszt őrzi), a valós karcsúság
megmarad. A lebenyes korona legkülső pontja igazodik a koronasugárhoz, így az átlagos körvonala
~18%-kal keskenyebb volt a szimulált koronánál; egy egységes 1,12-es teltségi szorzó ezt pótolja
(minden LOD-on azonos, a sziluett-egyezés megmarad). A szimuláció koronaméretei nem változtak.

Hátravan: a mogyoró csúcsának finomítása; fajonkénti arány-ellenőrzés referenciafotókkal.

### 2026-10-08 – gömbölyű, egybefüggő korona (Tree3D „geometric” mód mintájára)

A külön ikoszaéder-lebenyek túl szögletesek voltak. A lebenyek most **metaballok**: sima uniójuk
szintfelületét egy **Fibonacci-gömbrács** sugarai mentén mintavételezzük (a korona középpontjából,
a korona méretére nyújtva), és a rács konvex-burok háromszögelése adja az egyetlen zárt felületet
(Tree3D: Fibonacci Lattice + Delaunay/Convex Hull, a csomósságot a lebenyek adják).

- Rácsméret: Near 96 pont (188 △), Medium 40 (76 △), Far 17 (pontosan 30 △); kis koronák és cserjék kisebb rácsot kapnak.
  A háromszögelés rácsméretenként egyszer készül és gyorsítótárban marad.
- Metaball-esés (1 − (r/1,3)²)², a küszöb úgy választva, hogy egy magányos lebeny felülete a saját sugarán legyen.
- Sima normálok (20% facetta-fény), lebenyenkénti tónus a legerősebben ható lebenyből, enyhe (≈3%) tüskésség.
- A vázágak az ívhossz 65%-áig futnak, hogy ne döfjék át a zárt felületet.

### 2026-10-08 – kerekebb lombos koronák és állományárnyalás

A levélmintákból és metaballokból számított egyetlen zárt héjat fajonként eltérő mértékben
egy kerek ellipszoidos burkolathoz közelítjük. A bükk folytonosabb, simább tömeget kap,
a tölgy megtartja a nagyobb szabálytalanságokat; a nyír és kőris kisebb simítást kap.
A közelítés minden LOD-ban ugyanazt a függvényt használja, a korona teteje az eredeti
famagasságon marad. A lap- és csúcsnormálok keverése megőrzi a low-poly karaktert.

Az oldalirányú égbolt-kitettség a koronatérből készül: a szomszéd felőli oldal és a koronaalj
sötétebb, az állományszél szabad oldala teltebb és világosabb. Ez előre számolt vizuális
árnyalás, nem új fizikai fényszimuláció. Érett lombos fáknál a zárt állomány rövidebb,
magasabban kezdődő lombos koronát ad; a szabadon álló fa megtartja a teljes koronamélységet.
A bükk mélyebb árnyéktűrő koronát tart meg, és erősebben alakot vált a szomszédok hatására:
vizuális plaszticitás 1,0, a tölgyfajoknál 0,7. Ezek megjelenítési hangolóértékek.

A `CrownSpace` egy jelzőbittel megkülönbözteti a mért semleges koronateret a hiányzó adattól.
Korábban mindkettő nulla kódot adott, ezért egy minden oldalról szomszédos fa megkaphatta
a hiányzó adat nyitottabb megvilágítását. Az `IsUnspecified` név ezt a különbséget jelzi.
A szimulált méret, egészség, faanyag és óra változatlan; a megjelenítés használja a koronateret.

Összehasonlító kép: `--broadleaf-canopy-preview` (tölgy/bükk; szabad fa, zárt állomány és
állományszél), `artifacts/broadleaf-canopy/after.png`. Az előtte–utána mintában egyaránt
7144 lombháromszög szerepel. A koronabüdzsé továbbra is legfeljebb 188 háromszög Near és
30 Far szinten; új levélkártya vagy renderpassz nem került be. Regressziók:
`BroadleafCanopyTests`, a meglévő zárt háló/normál/évszak/LOD tesztek, natív
`--forest-smoke-test` és `--tree-growth-smoke-test`.

### 2026-10-08 – koronaplaszticitás (a koronák kitöltik a teret)

Zárt állományban a koronák felülnézetben kitöltik a rendelkezésre álló teret, csak a szegélyfák
terebélyesednek (Pretzsch 2009: crown plasticity). Megvalósítás, csak a megjelenítésben (a
szimuláció koronaméretei nem változnak):

- `CrownSpace` (Ecology): 8 irányszektor, szektoronként 8 szint (0,55–1,9 × szimulált koronasugár).
  A mérés a saját és a 8 szomszédos csempe fáiból dolgozik: két korona a törzsek közti távolságot
  sugaraik arányában osztja meg (12% összefonódással, hogy ne maradjon rés), a 60%-nál alacsonyabb
  fák nem préselnek. Szomszéd nélküli irányban a korona 1,6×-ig terül szét (szegélyfa).
- A szint felfelé kerekít: a korona elérje a rá jutó teret, ne álljon meg előtte.
- A tér a `TreeSite`-on és a `ShapeKey`-en (most `long`) keresztül jut a fa-hálóhoz; a `TreeDeformation`
  a koronaalap fölött irányonként skáláz (a törzs érintetlen, a vázágak a koronával mozognak),
  tűlevelűeknél fele erősséggel. Ismert térnél a régi egyirányú „rés felé nyújtás” kikapcsol, a dőlés marad.
- Tesztek: `CrownSpaceTests`.

### 2026-10-26 – erdeifenyő-korona hangolása a szerkesztőben

A Fafajok → Erdeifenyő → Megjelenés → Lombkorona csoport a meglévő
`LobeCrownMesh` hat paraméterét teszi szerkeszthetővé: lombtömegszám,
lombszintek, magasság/szélesség arány, középrész teltsége, összesimulás és
felszíni egyenetlenség. A közös, életfázisonkénti és elhalt állapotra megadott
értékek ugyanabban a `shape` beállításban tárolódnak, mint a vázágak paraméterei.
A felület értékei közvetlenül a meglévő koronaprofilra kerülnek; a Weber–Penn
ágvázgenerátor nem kapja meg őket. A fiatal, kúpos koronákhoz ezek a vezérlők
nem jelennek meg, és a lombfelületük sem változik.

A nyitójelenet fenyőjének értékei az élő szerkesztőben készültek. A két
szabályos lombszint helyett összefüggő, szabálytalanabb tömeg ül a magasabban
kezdődő koronában. Az érett és idős fáknál az ágmentes törzs aránya 0,779,
öreg fáknál 0,802. A közös felületbeállítások: `CrownLayers=0`,
`CrownFlatten=0.663`, `CrownCore=0.502`, `CrownRoundness=0.405`,
`CrownJitter=0.167`. A lombtömegszám továbbra is a faj és életfázis alapértéke.
A fájlban csak a `Pine.shape` változott; a más fafajokhoz tartozó értékek és
a növekedési szabályok érintetlenek.

Az ellenőrzés az editor gyári/szerkesztett összehasonlításában, több egyeddel,
az életút nézetben és a mentés visszanyitásával történt. Képek:
`artifacts/pine-editor/editor-after-reopen.png` és
`artifacts/pine-editor/editor-life-course.png`.
A `TreeCrownAuthoringTests` ellenőrzi az értékek mentését, öröklését, a
fiatal koronák megőrzését és azt, hogy a felülethangolás nem módosítja az
ágvázat vagy a háló felbontását. Új koronagenerátor és új renderlépés nem készült.

Ellenőrzés: 1523 teljes teszt sikeres; az utolsó vezérlőszűrés után az 57 célzott
faforma- és LOD-teszt ismét sikeres. A teljes megoldás és a végleges editor
Release fordítása figyelmeztetés és hiba nélkül lefutott.

### 2026-10-26 – irányított lombpárnák és összefüggő felületi részletek

A meglévő lebenyes koronagenerátor továbbfejlődött; a lombcsoportok képzése
a `LobeCrownMesh.Clusters.cs` részfájlba került. Ugyanaz a felületépítő szolgálja
ki az editort, a játékot és az összes LOD-ot.

- A lombcsoportok vízszintes kovarianciájából számított főirány elforgatott,
  elnyúlt ellipszoidokat ad. A két vízszintes sugár szorzata változatlan, ezért
  az elnyúlás a térfogat megtartásával alakítja a lombpárnát.
- Az `Ágvégek követése` (`CrownBranchFollow`) a levélcsoport eredeti helyéhez
  közelebb tartja a lombot. A kapcsolódás ellenőrzése az elforgatott
  ellipszoidot vizsgálja; a héj továbbra is összefüggő marad.
- A csúcssorszámhoz kötött véletlen eltérést két léptékű, folytonos,
  fahelyi részletmező váltotta fel (`CrownSurfaceDetail`). Egy adott irány
  részlete minden LOD-ban ugyanaz, és a fával együtt fordul.
- A felületkeresés végén már rendelkezésre álló lebeny-hozzájárulások
  enyhén sötétítik a lombpárnák közti mélyedéseket. Ez előre számolt
  felületszín; nem igényel új renderlépést.
- A sugárkeresés az ellipszoidok befolyásolási tartományának analitikus
  metszésénél indul. A korona előtti üres tér bejárása elmarad, a belső
  mező és a tíz felezési lépés változatlan.
- A lapos koronák távoli hálója az eredeti vízszintes konvex kontúron belül
  tölti ki a felső és alsó vállrészt (`CrownContour`). A befoglaló méret,
  magasság és háromszögszám nem nő. A nyitójelenet érett, idős és öreg
  fenyőjének oldalnézeti vetülete is a meglévő LOD-tűréseken belül marad.

Az editor új vezérlői: `CrownBranchFollow` és `CrownAnisotropy`. A fiatal
kúpos fenyők továbbra is a saját, változatlan eljárásukat használják.
A mogyoró és galagonya alapértékei megtartják a váza-, illetve gömbölyű formát;
az új vezérlők ezeknél is használhatók.

A 24 fás, nyolc fajból és három életfázisból álló ellenőrző galéria
lombháromszögszáma előtte és utána is 16600 / 3628 / 676 (Near / Medium / Far).
Képek: `artifacts/tree-gallery/crown-before-*.png`,
`artifacts/tree-gallery/crown-after-*.png` és
`artifacts/crown-evolution/editor-pine-after.png`.
A `CrownSurfaceTests` a főirányt, a részletmező folytonosságát, a sugárhatárt,
a kontúrkorlátot, a forgatást, a normálokat és a zárt hálót ellenőrzi.

Ellenőrzés: 340 célzott és 1534 teljes teszt sikeres, a Release fordítás
figyelmeztetés és hiba nélkül lefutott. Az új vezérlők módosítása és
visszaállítása a futó editorban is ellenőrizve; a korábban hangolt nyitójelenet
és a többi jelenetadat változatlan maradt.


### 2026-10-27 – referencia szerinti erdeifenyő, mélyebb koronával

A formai cél a kapott, 26 m magas és 10 m koronaszélességű erdei példány:
a törzs felső felét követő, szabálytalanul ágas korona, sok kisebb hajtáscsoport,
karcsú csúcs és a lehullott alsó ágak csonkjai. Összehasonlításként a
[Maxtree Pinus sylvestris modelljeit](https://maxtree.org/3d-plant-models/v24_pinus_sylvestris_01/)
és [a Pinus Sylvestris 05 modellképeit](https://www.cgtrader.com/3d-models/plant/conifer/pinus-sylvestris-05)
néztem meg. Ezek a struktúra referenciái; külső modell vagy textúra nem került a projektbe.

A meglévő `LobeCrownMesh` ugyanazzal az implicit felületmintavételezővel és
hálókibocsátással készít egyesített koronát és külön, egyenként zárt
hajtáscsoportokat. Az utóbbiak áttekinthető ágközi réseket hagynak. A kimenet
mindkét esetben egy koronaháló; külön renderer, alfa-kivágás vagy renderlépés nincs.

- Az új `CrownGaps` / **Ágközi rések** vezérlő 0-nál a közös burkot használja,
  pozitív értéknél külön csoportokat tart meg, és azok méretén keresztül
  szabályozza a nyitottságot. A közös/fázis/elhalt öröklés változatlan.
- A nyitott korona kizárólag valódi levélcsoportokból származik: nincs külön
  mesterséges csúcscsomó vagy középső zöld összekötő rész. Egy csoport maga is
  eltérő méretű hajtások egyesítése. A belső csoportok teltségét a `CrownCore` állítja.
- Az érett/idős alapprofil 32/36 csoportot képez. A szerkeszthető tartomány
  3–64-re bővült. A nagyobb szám finomabb tagolást ad; a közeli teljes pontkeret
  csoportok közötti elosztása területarányos.
- Az alap koronamélység érett/idős/öreg fánál 0,56/0,58/0,52, a fényhez
  igazítva. A vázágak alapformája félgömbös, felfelé keskenyedő; az idős
  törzs villásodása ritkább. Minden közös és fázis szerinti szerkesztőérték
  felülírhatja az alapokat. A fiatal fenyő kúpos eljárása és paraméterezése megmaradt.
- A nyitott koronák ágkerete Near/Medium/Far esetén 1000/320/100 háromszög;
  a törzs és gyökér külön készül. A vázágak a mellékágak előtt készülnek,
  a felső támasztóágak előnyt kapnak. Az egyszerűsítés a gallyvastagság mellett
  0,6 képpontos eltérési korlátot is figyelembe vesz. Az alsó ágcsonkok a tényleges
  törzsvonalhoz kapcsolódnak, ugyanazzal a csőháló-építővel és környezeti deformációval.
- A sok kisebb csoport közeli részletességéhez a fenyő pontkerete négyszeresére
  nőtt: normál méretű közeli koronánál 3068 lombháromszög készül. A távolabbi
  háló sem hagyja el a csoportokat; mindhárom távolság költsége nőtt a korábbi
  egyburkos modellhez képest. Az effektek és a többi fafaj részletessége nem csökkent.
  Ez látványfejlesztés, nem igazolt sebességnövekedés.
- A csoportosítás a fa saját koordinátáiban történik, utána egyetlen elfordítás
  forgatja a középpontokat és főirányokat. Így a közeli azonos távolságú minták
  besorolását nem módosítja az elfordítás kerekítése. A nagy sűrűségű gömbháló
  síktesztje kettős pontosságot használ; az 1536 pontos változat is zárt és konzisztens.

A nyitójelenetben kizárólag `Pine.shape` változott. Az ágmentes törzs aránya
érett/idős/öreg fánál 0,48/0,45/0,50. Az élő editorban az érett/idős csoportszámot
49/64-re hangoltam; a korábbi közös laposság-felülbírálás megszűnt. A mentés
visszanyitása és az életút több egyeddel ellenőrizve. Képek:
`artifacts/pine-reference/editor-mature.png`, `editor-old.png`, `editor-life.png`,
`editor-saved.png` és `editor-reopened.png`. A tűlevelek továbbra is a játék
stilizált, zárt hajtáscsoportjaiként jelennek meg; ez nem a referencia részletes
tűlevél-geometriájának vagy áttetsző levéltextúrájának reprodukciója.

A `PineCrownTests` rögzíti a mélyebb koronát, a minden LOD-ban megmaradó zárt
csoportokat, a részletkeretet, az ágcsonkokat és a résvezérlő visszaállítását.
A `CrownLatticeTests` az 1536 pontos gömbhálót is ellenőrzi. A teljes tesztkör
1543 sikeres teszttel zárult; a végső jelenet célzott LOD-ellenőrzése is sikeres.
A Release fordítás 0 hibával és 0 figyelmeztetéssel lefutott. Naplók:
`artifacts/pine-reference/full-tests.log`, `final-scene-tests.log`, `final-build.log`.
A képek és tesztek a megvalósítást dokumentálják; a látvány felhasználói
elfogadását nem helyettesítik.
