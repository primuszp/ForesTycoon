# Fagenerálás: irodalmi áttekintés és fajmodellek

Ez a leírás a DendroKit (Weber–Penn) alapú generátor fajparamétereinek és
poligonkeretének hátterét foglalja össze. A kód: `ForesTycoon.TreeModels/Meshing/DendroTreeGenerator.cs`
és `DendroCrownMesh.cs`.

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
