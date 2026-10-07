# Kocsányos tölgy: az ágváztól a tagolt koronáig

## Mit készít a generátor?

A Weber–Penn modell rekurzív ágszintekkel dolgozik: a 0. szint a törzs,
az 1. a vázágak, a 2. a gallyak. Az ág hossza függ a szülőág hosszától és
az elágazás helyétől; az irányt az elágazási és azimutális szögek, majd a
szakaszonkénti görbület adják. A `V` végű paraméterek determinisztikus
véletlen eltéréseket engednek. A `SegSplits` az adott rendű tengely
villásodását szabályozza, nem a gyermekágak számát.

Ez a paraméteres alakmodell nem élettani növekedésszimuláció. A ForesTycoon
külön szimulációja adja a fizikai magasságot, törzsátmérőt, koronaméretet,
életfázist és termőhelyet. A geometria ezekre méretezi az ágvázat.

A feldolgozás:

1. `TreeArchitecture` beolvassa a faj presetjét, majd életfázis és fény
   szerint módosítja. A Dendro `StemImpl` rekurzívan létrehozza az ágakat
   és a levélpontokat.
2. `TreeSkeleton` megőrzi a szülő–gyermek kapcsolatot, pontosan közös
   csatlakozási pontokat képez, ritkítja a vázat és legfeljebb 160
   levélmintát tart meg. Az átmérők a kiszolgált gallyakból számított,
   2,2 kitevős csőmodellt követik.
3. `TreeForm` a szimulált méretekre skáláz, az elhalást és a környezet
   okozta alakváltozást alkalmazza. `TreeWoodMesh` ebből készít csőhálót.
4. A korona külön közelítés a levélmintákból. Itt veszett el korábban
   a tölgy ágrendszerének sok jellegzetessége: a magassági gyűrűkből
   képzett egyetlen burok kisimította a lombtömegek közti bemélyedéseket.

## A tölgyhöz végzett módosítás

A kocsányos tölgyre jellemző a széles, terjeszkedő korona és az erős
vázágak. A cél egy ilyen, szabadabb állású, érett fa stilizált low poly
modellje; a paraméterek vizuális választások, nem mért botanikai illesztés.

A `quercus_robur.xml` presetben 36 helyett 14 elsőrendű ág a kiindulás,
a nagy görbületi szórást mérsékeltem (`1CurveV`: 190 → 95), az induló
ágszöget 78°-ról 58°-ra, a törzsvilla szögét 15°-ról 32°-ra állítottam.
Az életfázis- és fényfüggő módosítások továbbra is érvényesek.

Az új `OakCrownMesh` csak az érett és idősebb `ForestSpecies.Oak`
példányokra lép működésbe:

- Az élő levélpontokat koronamérettel normalizált térben, legtávolabbi
  kezdőpontokkal és hat Lloyd-iterációval hét (idősen nyolc) csoportba
  osztja. A csoportközéppont és a pontok szóródása adja a lombtömeget.
- A közeli modellben minden lombtömeg egy 20 háromszögű ellipszoid;
  egy központi tömeg biztosítja a folytonos takarást. Az eltérő
  orientációk mérséklik a sokszögek ismétlődését. Az elhelyezés korlátja
  biztosítja a tömegek átfedését.
- Közepes és távoli nézetben ugyanazokat a tömegeket sugarakkal mintázott
  külső burok helyettesíti. Nagyon kis koronánál a közeli nézet is ezt
  az olcsóbb változatot használja.
- A lomb teljesen tömör. Nincs levélkártya, alfa-kivágás vagy új textúra.
  Az évszakos szín, az elhalás, az elfordítás és a termőhelyi torzulás
  továbbra is a közös rendszerből érkezik.

## Ellenőrzés és korlátok

`dotnet run --project ForesTycoon -- --oak-study-preview`

A tanulmány ugyanazt a 42-es seedű fát mutatja három irányból, lombosan
és lomb nélkül. Mérete 18 m magasság, 0,85 m mellmagassági átmérő és
8 m névleges koronasugár; ezek rögzített bemeneti értékek, nem egy
konkrét felmért fa adatai. A kimenet `artifacts/oak-study/after.png`.
A `--before` kapcsoló csak a fájl nevét választja; a korábbi algoritmus
képét a módosítások előtt ezzel mentettük, nem futás közbeni visszakapcsolás.

A bemutatott nyári modell 477 faanyag- és 160 koronaháromszögből áll
(összesen 637); az eredeti ugyanilyen méretű modell 407 + 96 = 503 volt.
A lomb nélküli, több ágat megmutató változat 1774 háromszög.

A tesztek ellenőrzik a véges, egységnyi normálokat, a zárt felületeket,
az egymásba érő lombtömegek összefüggő metszési gráfját, az elfordítás
követését, a determináltságot, a LOD-sziluettet és az évszakos színeket.
A közeli tölgykorona több zárt testből áll; ezek átfedik egymást, de
nem alkotnak hegesztett, egykomponensű hálót. Ez renderelési modell,
nem térfogatmérésre szánt geometriai unió. A kis poligonszám miatt a
kontúr továbbra is szögletes, az egyedi levelek és finom gallyak hiányoznak.

## Források

- [Weber és Penn (1995): Creation and Rendering of Realistic Trees](https://doi.org/10.1145/218380.218427).
- [A Weber–Penn modell paramétereinek implementációs leírása, PyHelios](https://plantsimulationlab.github.io/PyHelios/WeberPennTreeDoc.html).
- [University College Cork: Quercus robur](https://www.ucc.ie/en/tree-explorers/trees/a-z/quercusrobur/).

A lombcsoportosítás és a LOD-burkolás itt leírt kombinációja a ForesTycoon
saját közelítése; nem a Weber–Penn tanulmányból átvett algoritmus.
