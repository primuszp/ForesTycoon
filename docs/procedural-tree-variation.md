# Eljárásos fák változatossága

A játék alapértelmezett famodellje ismét a saját C# eljárásos generátor.
Az importált modellek, az eredeti részletes fenyő és az EZ-Tree készlet a
Grafika → Fa modellek választóban továbbra is elérhetők.

## Tree3D vizsgálat

A [Tree3D](https://drajmarsh.bitbucket.io/tree3d.html) és a
[szerző leírása](https://andrewmarsh.com/software/tree3d-web/) két megközelítést
mutat be: egyszerű geometriai koronákat és részletesebb, ágvázra épülő fákat.
A korona ponteloszlása, zajossága, lombcsomói és az ágazás külön szabályozható.
A geometriai mód többek között Fibonacci-pontelosztást, Perlin-zajt és metaball
csomókat kínál; a részletes mód a proctree.js elágazó fáira épít, radiális
ágazási lehetőséggel kiegészítve.

A vizsgálat a HTML-felület paramétereire és a szerző dokumentációjára támaszkodott;
az interaktív böngésző megnyitása környezeti hiba miatt nem sikerült.
Az alkalmazás kódját és grafikai anyagait nem másoltuk át.

## Megvalósított életfázisok

A Grafika menüben az **Eljárásos fák (életfázisok)** mód az alapértelmezett.
A korábbi „Új modellek” felirat az importált készletet jelentette; ezt
„Importált modellek”-re pontosítottuk. A meglévő alternatívák megmaradtak.

Mind a négy faj három rögzített alapváltozatot kap minden életfázisban:
összesen 4 faj × 4 fázis × 3 változat = 48 kombináció, további seed-variációval.
A változat az egyed seedjéből számolódik és életkorváltáskor megmarad.

| Fázis | Lombos fák | Lucfenyő |
| --- | --- | --- |
| Csemete | 3–5 külön hajtás/lombpamacs, vékony szár, alacsony lombhatár | 3–5 egyszerű örv, kevés tűleveles ág, oldalsó lombspray nélkül |
| Fiatal | magas, tömör központi korona és alacsony oldalkoronák | sűrűbb, talajközeli ágak, 7–9 örv |
| Középkorú | 5–7 külön, saját ággal hordozott koronatömeg | teljes, 9 örvös korona |
| Idős | 4–6 rövidebb, elkülönülő koronatömeg, magasabb lombhatár és csupasz oldalágak | 7 örv, szélesebb felső korona, részben csupasz alsó ágak |

A fajok koronaprofilja különböző; a nyírcsemete kérge barna, később fehér.
A fázishatárok művészeti/játékparaméterek, nem biológiai élettartam-előrejelzések.
Csemete: nyírnál 2, a többi fajnál 3 éves korig; fiatal: a faj érettségi korának
75%-áig; középkorú: a faj maximális játékbeli korának 65%-áig; utána idős.

A gyors GPU-méretnövekedés az egyes fázisokon belül megmarad. Egy fázishatár
átlépése viszont új korona- és ággeometriát készít, havi szimulációs tickre várás
nélkül. A cache az érintett erdőrészlet legközelebbi fázishatárát követi.
A terepszerkesztés helyi invalidálása változatlanul működik.

A mellékkoronák közeli nézetben is kisebb felbontásúak: a lombos korona
legfeljebb 696 háromszög, nem hétszeres költségű teljes részletességű korona.
A teljes erdő költsége és a fázisok közti látványos modellváltás külön ellenőrzendő.

`--tree-life-stage-preview` fajonként és fázisonként három változatot rögzít az
`artifacts/tree-life-stages` mappába. Ezek szándékosan azonos fizikai mérettel
mutatják a szerkezeti különbséget; a játékban a tényleges növekedési méret jelenik meg.
`python tools/review_tree_life_stages.py` a képeket összehasonlító lapokra rendezi.

## Korábbi alapváltozatosság

A lombos fák korábbi négy változata helyett a teljes egyedi seed határozza meg
a teltséget, a vízszintes nyújtottságot, a kéttengelyű koronaeltolódást,
az alsó/felső koronatömeg arányát és a lombcsomók számát, erősségét, fázisát.
A tölgy szélesebb, csomósabb; a bükk simább, tojásdad; a nyír könnyebb koronája
megőrzi a faj saját profilját. A koronatömegek továbbra is zárt felületek,
egyenként közel 216, középtávol 80, távol 36 háromszöggel.

A lucfenyőn egyedenként 4–6 ág alkot egy örvöt. Változik a kúposság, az örvök
elfordulása, az alsó lombhatár, a lehajlás és az ágonkénti lomb szélessége.
A fás ág és a lomb ugyanazt az elrendezést használja. A közeli lucfenyőkorona
2 904–4 344 háromszög; a hatágú változat legfeljebb 20%-kal több a korábbi,
mindig ötágú koronánál. Ez geometriai korlát, nem FPS-mérés.

A paraméterek külön hash-csatornákat kapnak, ezért az új tulajdonságok hozzáadása
nem tolja el a többi véletlen sorozatát. A fa növekedés közben megtartja alakját;
a részletességi szintek ugyanazokat az egyedi paramétereket használják.
A fizikai törzsátmérő és a kivágott tönk méretezése változatlan.

## Ellenőrzés és további irány

A geometriai tesztek négy faj és nyolc seed esetén ellenőrzik a zártságot,
a háromszögek és normálok érvényességét, a mérethatárokat és a determinisztikus
újragenerálást. Fajonként 64 azonos méretű fa 64 eltérő körvonalat ad.
Az OpenGL modellteszt ellenőrzi a modellváltást és a megmaradt importált készleteket.

`--procedural-tree-preview` fajonként hat azonos korú és méretű fa képeit írja
az `artifacts/procedural-tree-variation` mappába, a tényleges játék-rendererrel.

Az önálló lombcsomók és az azokat hordozó lombos ágváz már beépültek.
A Fibonacci-pontelosztás és a térfogati koronaépítés
egyelőre nincs beépítve; a Tree3D teljes generátorát nem portoltuk.
