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
