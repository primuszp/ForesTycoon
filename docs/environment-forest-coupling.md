# Összehangolt időjárás, talaj és faegyedek

## Felelősségek és időrend

Az `IForestHabitat` adja a terep geometriáját, talajprofilját, vízkifolyását és burkoltságát. A szimuláció ehhez az interfészhez kapcsolódik, nem a rajzolóhoz vagy konkrét `Terrain` típushoz.

| Rendszer | Felelősség |
| --- | --- |
| `WeatherSystem` | Seedelt időjárási események, eső integrálása, felhőzet, besugárzás, hőmérséklet, páratartalom és szél |
| `SoilProperties` | Telítési vízkészlet, szabadföldi vízkapacitás, hervadáspont, beszivárgás, drénezés és termőképesség |
| `EnvironmentSystem` | Korona-, felszíni-, gyökérzóna- és mélyvízkészlet; párolgás, tényleges vízfelvétel és lefolyás |
| `ForestSystem` | Egyedek, szomszédos koronák árnyékolása, növőtér, növekedés, egészség és elhalás |
| `ForestEnvironmentCoordinator` | Félmásodperces vízlépések, havi erdőhatárok és környezeti összegzések sorrendje |

A koordinátor a vízlépést a hónap határán kettévágja: először lezárja az adott időszak víz- és fényösszegzését, utána lépteti az erdőt, végül törli a havi összegzőket. A környezet nem lépteti az erdőt, az erdő nem törli a környezet összegzőit. A képkockák időtartamának csoportosítása nem változtatja meg az eredményt. A fizikai faméret a havi ráta alapján folyamatosan változik, a ráták és az egészség havonta frissülnek.

Egy erdőév 1200 szimulációs másodperc; 60 másodperc egy környezeti óra. A vízkészletek mm-ben, a vízáramok mm/környezeti órában értendők. Ez gyorsított játékmodell, nem naptári éves hidrológiai előrejelzés.

## Közös erőforrásmodell

A felhőzetből számított relatív besugárzás `1 − 0,75 × felhőzet`. Az egyedi geometriai árnyékolás ezt tovább csökkenti. Ugyanez a besugárzás szerepel a párolgási és növényi vízigényben; a grafikai effektek állítása nem változtatja meg.

Az élő koronák vetülete, fafaja és egészsége adja a csempe borítottságát, intercepciós kapacitását és levélfelületi indexét. A borítottság `1 − exp(−koronaterület / csempeterület)`; a levélfelületi index legfeljebb 6. Ezek erdőrevíziónként gyorsítótárazódnak, így a vízlépés nem járja be minden faegyed adatait.

A talaj felvehetővíz-aránya a hervadáspont és a szabadföldi vízkapacitás közötti készletből számolódik. A tényleges gyökérvízfelvétel a levélfelületi igény és e hányados szorzata, legfeljebb a hervadáspont feletti rendelkezésre álló készlet. A közös készletből az egyedek levélfelületi igényükkel arányosan részesednek: egy csempén ugyanazt a teljesítettigény-hányadost kapják, eltérő fajspecifikus aszályválasszal. Az egyedi versengés ilyenkor nem alkalmaz második gyökérterhelési büntetést.

A havi növekedés és egészség a tényleges/teljes igény hányadosából, az átlagos túlöntözési stresszből, a fényből, növőtérből és termőhelyből származik. A talaj termőképessége külön tulajdonság; az induló nedvesség nem állandó növekedési szorzó. Emiatt egy kezdetben száraz, de termékeny csempe eső után újra növekedhet.

Kivágás és ültetés az érintett két szomszédgyűrű jövőbeli növekedési rátáit azonnal frissíti közös egyedpillanatképből. Ez nem okoz méret- vagy egészségugrást. A levágott koronán tárolt víz a következő vízlépésben a felszínre csepeg; a kidőlt fák és tönkök nem transzspirálnak.

## Vízmérleg és mentés

Az egyenleg: `tárolt víz = kezdeti víz + csapadék − párolgás − transzspiráció − kifolyás`. A csempék közötti lefolyás kettős pufferrel történik, ezért bejárási sorrend nem mozgatja többször ugyanazt a vizet egy lépésben. A párolgás és transzspiráció külön számláló; a globális összeg egysége azonos területű csempéknél cella-mm.

A mentésformátum **4-es verziójú**. A seed, tick és parancsnapló az új szabályokkal pontosan visszajátszható. A 3-as és korábbi mentések nem tölthetők be: az új növekedési szabályok más eredményt adnának ugyanabból a naplóból. A régi fájlok megmaradnak; nincs automatikus migráció vagy korábbi szabályokat futtató kompatibilitási ág.

## Ellenőrzések és mérési korlátok

`ForestEnvironmentCouplingTests` ellenőrzi a sűrűségfüggő vízigényt, a vízkészlet korlátját és megmaradását, a felhőzet hatását, az eső utáni regenerálódó növekedést, a termőképességet, a kivágás utáni szomszédos fényt és koronavízátadást, a szünetet és a képkockacsoportosítást. Az utóbbi nem egész másodperces hónaphatárokkal is fut.

```sh
dotnet test ForesTycoon.sln -p:UseAppHost=false
dotnet ForesTycoon/bin/Debug/net8.0/ForesTycoon.dll --environment-smoke-test
dotnet ForesTycoon/bin/Debug/net8.0/ForesTycoon.dll --tree-growth-smoke-test
dotnet ForesTycoon/bin/Debug/net8.0/ForesTycoon.dll --logistics-smoke-test
```

A 2026-10-04-i környezeti próba vízlépése: 64² cella 0,78 ms; 128² cella 3,14 ms; 256² cella 13,01 ms. Ezek izolált vízlépések, nem teljes játék-képkockák vagy havi egyedversengési mérések. A teljes próba vízmérlegeltérése −3,252 × 10⁻⁸ cella-mm. A grafikai függetlenség, a szünet, a nedvesedés/száradás és a mentés/visszajátszás ellenőrzése sikeres.

## Megmaradó feladatok

A valós terep egyelőre egységes alap-talajprofilt használ; a különböző profilok interfésze és tesztjei elkészültek, térképi generálásuk még nincs. A gyökérvíz csempén belül közös, nem áramlik szomszédos gyökérzónák között. A tó- és folyóvízszint rögzített. Nincs napi besugárzási ciklus, lombhullási fenológia, fagy/hó vagy kalibrált fotoszintézis- és tápanyagmodell. A havi versengés és a nagy térképek szerkesztési költsége külön profilozást igényel; a vízlépés javulása nem garantál sima futást minden térképméreten.

A havi versengés és helyi kitermelés külön mérése, az eredményt megőrző térbeli előszűrés, a geometriai versengés hónap közbeni előkészítése és az erdőművelés részleges érvénytelenítése elkészült: [helyi javítások és fennmaradó havi megakadások](engine-performance.md#helyi-változások-javítási-sorai). A környezeti hatás továbbra is a lezárt hónap tényleges adataiból származik. Nagyobb jeleneteknél a hónapvégi alkalmazás további felosztása szükséges.

A hónaphatárra várható méret, geometriai növekedési tagok, térfogatnövekmény és kezdeti állományösszegzés szintén előkészül. Ezek nem helyettesítik a tényleges havi víz- és fényválaszt. [Bontott profil és együtt futó környezet/erdő mérése](engine-performance.md#növekedési-görbék-és-hónapvégi-állomány-előkészítése).

## Egyedi fák havi megfigyelése és a Dudlesz-próba

A vízmodell minden vízlépésben a hervadáspont és a szabadföldi
vízkapacitás közötti felvehető hányadot méri. A fa havi vízválaszának
bemenete ennek az idővel súlyozott átlaga és a havi átlagos pangóvíz.
A `soil_mm` a hónapzáráskor a gyökérzónában levő teljes víz milliméterben.
A `mean_supply` az időszak átlagos felvehető hányada, 0–1 skálán. A
pillanatnyi diagnózis továbbra is az adott időpont készletéből számol.
A régi hónapközi mentésben az eltelt hónaprész felvehetővíz-összegét a
betöltő az ott tárolt gyökérzónavízből becsüli. Az új mentés az összeget is tárolja.

Az editor **Pálya → Erdő → Egyes fák vizsgálata** nézetében a nagyított
csempe pontjai a törzsek. Kattintással vagy azonosítóval választható fa.
A vízválasz és a stressz grafikonján kívül a hónapválasztóval leolvasható
a vízkészlet, a fény, a rendelkezésre álló növőtér, az egészség és a
törzsátmérő változása. A nyitott editor az utolsó 24 lezárt erdőhónapot
tartja meg. Új világ és visszatöltés új megfigyelést indít. Az alapesetben futó
játékban nincs egyedi idősor-rögzítés.

### Megismételhető futtatás

```sh
dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --forest-followup 30 42 artifacts/forest-followup
dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --soil-recovery
dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --month-work-benchmark
dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --month-work-benchmark --tree-history
```

A `trees-42.csv` minden nyír és erdeifenyő egyedi havi adatait adja.
Az elhalás sorában a szabály végrehajtásakor mért egészség és stressz áll.
A `cohorts-42.csv` minden természetesen megtelepedett fafajt tartalmaz.
A `gap` élő fák között, korábbi törzs helyén létrejött újulatot, az `open`
a megtelepedéskor élő fa nélküli csempét jelöl. A nyílt csempére is vethetnek
árnyékot a szomszéd koronák. Az adott túlélési arány nevezőjébe azok a magoncok kerülnek, amelyek
megtelepedése és a mérés vége között legalább a vizsgált számú év eltelt. Az évforduló hónapjában elhalt fa már kiesik a túlélők közül.

| Megtelepedés helye | Megjelent egyed | 1 évnél élő / értékelhető | 3 évnél | 5 évnél |
| --- | ---: | ---: | ---: | ---: |
| Lék | 15 | 15/15 | 14/14 | 12/13 |
| Nyílt csempe | 44 | 40/40 | 37/37 | 31/37 |

A három 16×16-os pálya kezdőértéke 17, 42 és 83, a megfigyelés hossza 30 év.
A 17-es pálya 132-es csempéjén a 141-es nyír a 49. hónapban elpusztult,
a 143-as a 78. hónapig élt. A 49. hónapban mindkettő vízválasza 0,454 volt.
A két fa növőtérértéke 0,330 és 0,640, stressze 4,08 és 1,67 év volt.
A 42-es pálya 81-es csempéjén az 50-es erdeifenyő a 49., a 49-es a 274.
hónapban pusztult el. A vízválaszuk a 49. hónapban egyaránt 0,929 volt.

### Soproni talajszelvények

A Dudlesz-erdőben feltárt 12. szelvény (agyagbemosódásos barna erdőtalaj)
0–200 cm-es diszponibilis vízkészletének közölt becslése 289 mm,
a 137. szelvényé (agyagbemosódásos rozsdabarna erdőtalaj) 195 mm.
A tanulmány a két talajcsoportra 280 és 184 mm-es átlagot is közöl.
A becslések forrása Mészáros Diána, Czimber Kornél, Szász Botond, Illés Gábor és Heil
Bálint (2026), *Távérzékeléssel támogatott talajtérképezés megalapozása
a Dudlesz-erdő területén*, kézirat, 1. táblázat és az azt megelőző bekezdés.

A `--soil-recovery` a négy kapacitással futtatja ugyanazt a két modelfát.
A többi talajparaméter a modell standard értékeiből származik. A kísérlet
13 hónap alatt 40 mm-es lépésekben növeli, majd csökkenti az előírt hiányt
a szabadföldi vízkapacitáshoz képest. A készlet alsó határa a hervadáspont.
A 7. hónapban, 240 mm előírt hiánynál a 137. szelvénynél elfogyott a
felvehető készlet. A 12. szelvénynél 49 mm maradt. A nyír vízválasza
sorrendben 0 és 0,221, az erdeifenyőé 0 és 0,412 volt. A 13. hónapban
mindkét szelvény mindkét modelfájának vízválasza visszatért 1-re.
Ezek az eredmények ellenőrzött vízkészletre adott modellválaszok. Terepi
túlélési vagy stresszidősorral történő validáláshoz ismételt felvétel szükséges.


### Hónapzárás költsége

Release mérés, 310 teljes szimulációs tick, 42-es kezdőérték, sűrű vegyes
állomány. Az alábbi idő a havi erdőmunka négy műszerelt szakaszának összege.
A zárójelben szereplő érték a teljes világ leglassabb tickje. A méréspár
azonos végállapot-lenyomatot adott mindkét pályaméreten.

| Pálya | Egyed | Játék, ms | Editor havi egyedi adatrögzítéssel, ms |
| --- | ---: | ---: | ---: |
| 64×64 | 16 160 | 138,2 (149,0) | 140,2 (151,0) |
| 128×128 | 66 735 | 164,2 (513,3) | 190,7 (501,8) |

Egy pályaméreten és beállításonként egy hónaphatár szerepelt a végső
mérésben. A teljes tick idejét a többi világrendszer és a futtatási
környezet is befolyásolja. A mérés sem renderelést, sem interaktív FPS-t nem mér.
