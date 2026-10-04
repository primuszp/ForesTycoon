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
