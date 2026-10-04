# Erdőtelepítés és egyedi versengés

## A játékos számára

A telepítési terület kijelölése után a sikeresen beültetett csempék tartós, borostyánsárga területhatárt és halvány talajszínezést kapnak. A szomszédos, azonos művelettel telepített csempék között nincs belső határvonal. A barna sorjelölések a terep felszínét követik; a kijelölési előnézet elengedése után is megmaradnak. A jelölés a **Grafika → Erdőtelepítések jelölése** kapcsolóval rejthető el.

Minden fafajból csempénként **6×6, azaz 36 csemete** kerül a talajba. A sorok a csempehatáron folytatódnak; az egyedi seed változtatja a méretet és a modellváltozatot, nem a sorhelyet. Az alapcsempe fizikai közelítésében ez körülbelül 2,6 méteres térköz. A természetes erdő változatos helyei és korai nem válnak soros telepítéssé.

A fák saját méretükkel nőnek, és a korukhoz tartozó csemete, fiatal, középkorú és idős modellek jelennek meg. A koronák terjeszkedésével egyre erősebb a verseny. Az árnyékban vagy vízhiányban maradó fák lassabban nőnek, rosszabb egészségi állapotba kerülnek; tartós elnyomás esetén az állomány önmagát ritkítja. A fényigényes nyír hamarabb szenved a záródástól, mint az árnyéktűrő bükk.

A **Vizsgálat** eszköz a telepítés azonosítóját, fafaját, élő/eredetileg telepített egyedszámát, még látható elhalt fáit és az élő egyedek átlagos fény-, víz- és növőtérmutatóját mutatja. A határ a kitermelés után is megmarad. Az újratelepítés új telepítési azonosítót ad az érintett csempének; az új világ vagy térkép újragenerálása törli a régi kijelöléseket.

## Kutatási alap és egyszerűsítés

A választott alap a FORMIND egyedi fa alapú megközelítése: a magasabb koronák fényt vonnak el az alacsonyabb egyedektől, a talajvíz korlátozza a növekedést, a zsúfoltság és az egyed állapota befolyásolja a mortalitást. Elsődleges források: [UFZ: versengés és környezeti korlátozások](https://formind.pages.ufz.de/competition-limitation.html), [UFZ: mortalitás](https://formind.pages.ufz.de/mortality.html).

A játék saját, egyszerűsített közelítést használ. Nem a teljes FORMIND implementációja, és nincs erdészeti mérési adatokhoz kalibrálva. A sűrűség, a növedék és az elhalás küszöbei játékparaméterek. A képletek koronák vízszintes átfedését és relatív magasságát használják; nincs külön levélfelület-rétegezés, fotoszintézis-/szénmérleg vagy napsugárkövetés.

## Havi szimuláció

1. Az összes egyed azonos havi időpontra vett méretéből készül egy versengési pillanatkép. Az egyedek frissítési sorrendje nem változtatja meg a szomszédok árnyékolását.
2. Minden fa a saját csempéjén és két szomszédsági lépésen belül keres versenytársakat. Ez a valódi terepen az átlós szomszédokat is tartalmazza. A helyek és méretek méterben szerepelnek, azonos dioráma-átváltással, mint a kirajzolás.
3. Egy `d` távolságú szomszéd koronájának közelített átfedése `O = max(0, 1 − d²/(r₁+r₂)²)²`. A relatív koronaigény `q = clamp(r₂²/max(0.0225,r₁²), 0.001, 4)`.
4. A magassági dominancia `D = clamp(0.5 + 2(h₂−h₁)/max(0.8,h₁), 0, 1)`. A fény `L = exp(−0.65 Σ[2 O D egészség₂])`. Az alacsony csemeték nem vetnek mesterséges árnyékot a fölöttük álló koronára.
5. A növőtérmutató `S = 1/(1 + 0.55 Σ[O q])`. A gyökérverseny ugyanilyen átfedési függvényt használ, legalább 1 méteres, egyébként a koronaérintkezési távolság 1,25-szeresét elérő hatókörrel. A vízmutató `W = helyi vízfaktor/(1 + 0.22 Σ[gyökérátfedés √q])`.
6. A helyi vízfaktort a meglévő időjárási és talajvízrendszer adja. A gyökérverseny a növekedési hozzáférést csökkenti, nem vesz ki másodszor vizet a vízmérlegből. Környezeti rendszer nélküli tesztben a termőhely nedvessége adja az alapértéket.
7. Az árnyéktűrés `T` mellett a fényválasz `L^(1−0.65T)`. A növekedési szorzó a termőhelyi alkalmasság, fényválasz, víz, `√S`, egészség és évszak szorzata. A korona a magassági növedékkel is terjeszkedik; érett fáknál a törzsvastagodás tovább növelheti a koronát.

A víz- és helymutató nem külön fizikai készlet. A százalékok relatív növekedési lehetőséget jelentenek. A kétlépéses szomszédság és a talaj fölötti relatív famagasság használata tudatos gyorsítás; meredek lejtőn nem számolunk teljes, abszolút magasság szerinti fénysugárterjedést.

## Tartós stressz és holtfa

A legerősebb korlátozás a fényválasz, víz és növőtér minimuma. Stressz gyűlik, ha ez kisebb, mint `0.25 + 0.25(1−T)`, vagy az egészség 18% alatti. Minden ilyen hónap 1/12 stresszévet ad hozzá; kedvező hónapban ennek fele leépül. Az egészség havi legfeljebb 0,035 lépéssel közelít a termőhelyi alkalmasság és erőforrások által meghatározott célhoz.

Legalább négy felhalmozott stresszév után az egyed elpusztulhat, ha egészsége `0.4 + 0.25(1−T)` alatt van. A faj maximuméletkorának átlépése szintén elhalást okoz. Ezek determinisztikus játékszabályok; nem terepi mortalitási valószínűségek.

Az elhalt egyed növekedése megáll, lombja eltűnik. Két évig álló száraz törzs és ágak láthatók, utána kidőlt fa; nyolc év után eltűnik. A kidőlés rögzített irányú geometriai forgatás, nem fizikai ütközésszimuláció. A holtfa nem akadályoz utat, és nem ír jóvá automatikusan kitermelt faanyagot. A kitermelés továbbra is külön művelet, saját méretű tönkkel és anyagmérleggel.

## Tárolás és teljesítmény

- A telepítés csempénként fajjal, területazonosítóval, telepítési idővel és kezdeti egyedszámmal szerepel. Az élő faegyedek erőforrásmutatókat és felhalmozott stresszt tárolnak.
- A mentés formátuma **3**. A seed és parancsnapló visszajátszása azonos buildben újraépíti a kijelöléseket, sorokat, növekedést és elhalást. Korábbi mentésformátumhoz nincs migráció.
- A versengés helyi, újrahasznált tömböket és gyorsítótárazott szomszédságot használ; nincs világméretű, minden fát minden más fával összehasonlító ciklus. A természetes erdő tömbkapacitása nem nő meg a sűrű telepítések miatt.
- A területjelölés chunkonként megőrzött GPU-geometria. A havi növekedés nem építi újra. A telepítés vagy helyi terepváltozás frissíti; az egyedi fák hónapon belüli növekedése továbbra is shaderben történik.

## Ellenőrzés

```powershell
dotnet test --no-restore
dotnet run --project ForesTycoon --no-build -- --plantation-smoke-test
```

A tesztek mind a négy faj sorhelyeit, a tartós kijelölést, az átlós szomszéd árnyékolását, a versenytárs eltávolítása utáni erőforrás-felszabadulást, a holtfa és kitermelés elkülönítését, az öregkor előtti önritkulást, az aszályt és az eltérő képkockahosszok determinisztikus eredményét vizsgálják. A rejtett OpenGL-próba valódi játékgeometriáról készít képeket az `artifacts/plantations` mappába, és ellenőrzi a tartós jelölés láthatóságát, gyorsítótárát, terepkövetését és a mentés visszajátszását.
