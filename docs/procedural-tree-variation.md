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

## Megvalósított első lépés

A lombos fák korábbi négy változata helyett a teljes egyedi seed határozza meg
a teltséget, a vízszintes nyújtottságot, a kéttengelyű koronaeltolódást,
az alsó/felső koronatömeg arányát és a lombcsomók számát, erősségét, fázisát.
A tölgy szélesebb, csomósabb; a bükk simább, tojásdad; a nyír könnyebb koronája
megőrzi a faj saját profilját. Ez továbbra is egyetlen zárt koronafelület,
változatlan háromszögszámmal: közel 216, középtávol 80, távol 36 háromszög.

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

Következő lépésként külön lombcsomók és az azokat hordozó lombos ágváz javíthatná
a közeli sziluettet. Ehhez előbb meg kell szabni a fa és a teljes erdő
geometriai költségkeretét. A Fibonacci-pontelosztás és a térfogati koronaépítés
egyelőre nincs beépítve; a Tree3D teljes generátorát nem portoltuk.
