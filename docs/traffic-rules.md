# Forgalmi út- és nyomkopás

A `traffic.wear` projektbeli szabály az Editor **Szabályok → Forgalmi út- és
nyomkopás** lapján szerkeszthető. Ugyanaz a `WorldRule` és `RuleInfluence` modell,
gráf, görbeszerkesztő és futtató működteti, mint a vízáramlást vagy a tönklebomlást.
Nincs külön útkopási modell, fordító, viselkedésfelülírás vagy natív nyomkopási képlet.

## Szerkesztés

Az **Áthaladás** objektum választása áthaladási indítást jelent. A gráfban külön
választható a kapcsolt **Jármű**, **Út**, **Nyom** és **Csempe**, majd azok mezője.
Az angol azonosítók stabilak; a felületen kijelzett név, mértékegység és segítség
tartozik hozzájuk.

| Objektum | Példák | Szerep |
| --- | --- | --- |
| Áthaladás | `isRoad`, `isTrail` | A belépés burkolt úton vagy közelítőnyomon történt-e. |
| Jármű | `vehicle.kind`, `vehicle.mass`, `vehicle.fill`, `vehicle.vehicleId` | A belépéskor rögzített fajta, össztömeg, rakományarány és azonosító. |
| Út | `road.condition`, `road.surface` | Állapot változtatása; burkolat szerinti feltétel. |
| Nyom | `trail.wear`, `trail.idleYears` | Vályúmélység változtatása; használat óta eltelt idő beállítása. |
| Csempe | `tile.moisture`, `tile.wetness`, vízkészletek | További, szabadon hozzáadható környezeti hatások. |

A kapcsolatok erőssége számmal és csúszkával, a források hatása a közös görbékkel
állítható. A feltételek mező- és értékválasztók. Egy áthaladás hatása egyszer,
időszorzás nélkül érvényesül; nem függ a megjelenítési képkockák számától.
Az időarányos hatás beállítása áthaladási szabályban érvénytelen.

Az alapgráf külön kapcsolatként mutatja az aszfalt és a makadám útkopását, a
teherautó nyomterhelését, az erdészeti gép alapnyomását, a rakomány többlethatását
és a nyom pihenőidejének nullázását. A tömeg nagyobb kopást, a már mély vályú
kisebb további mélyülést okoz. A gépek alapmintája csak a nyomra hat; útkopásuk
ugyanitt új kapcsolattal adható hozzá. A gépek saját tömege típusadat, nem kopási
képlet. A szabály kikapcsolása vagy törlése az összes felsorolt hatást leállítja.

Az **Áthaladás próbája** mezőiben hely, járműfajta, tömeg és rakomány választható.
Az előnézet nem ír a világba, és a kijelölt, alkalmazott szabály hozzájárulásait
mutatja. Az **Egy áthaladás a tesztvilágban** gomb minden bekapcsolt áthaladási
szabályt futtat, listasorrendben, naplózott szerkesztési parancsként. Így ugyanaz
a próba menthető és visszajátszható. Nem létező kapcsolt út vagy nyom nem írható;
ezt az előnézet is figyelembe veszi.

## Futtatás és mentés

A mozgás csak nyers eseményt ad: melyik jármű melyik csempére lépett, milyen
tömeggel és rakománnyal. A rendszer nem a rajzolásból és nem a félmásodperces
szabálylépésből következtet az áthaladásokra.

Az eseménysor keletkezési sorrendben fogy el. Egy esemény csak a `passage`
indítású szabályokat értékeli ki, a saját csempéjén; nincs hozzá pályaméretű
út-/nyomlista-előkészítés. A kiértékelés, a kihagyott szabályok bejárása és a saját
mezők első inicializálása is munkaszeletekben történik. Az időkeret csak azt dönti
el, mikor adja vissza a motor a vezérlést; az eredmény sorrendje determinisztikus.

A saját `passage` mezők az érintett csempe azonosítójához kötött tartós állapotot
tárolnak, például áthaladásszámlálót. Nem keletkezik minden eseménnyel új,
korlátlanul szaporodó mentett objektum.

A gépek eseményei a logisztikai szakasz végén, az időszakos szabályok előtt
futnak le. A teherautók eseményei a járműrendszer után, ugyanazon logikai tick
lezárása előtt. Az eseményben a járműadatok rögzítettek; a cél állapotát az esemény
végrehajtásakor olvassa a gráf, így az egymást követő áthaladások hatása egymásra
épül. Az időszakos és eseményszabályok közös mezőt is írhatnak; az Editor az
indítást külön feltünteti, nem állít közöttük pusztán listasorrendet.

A mentés teljes tick- vagy parancscsomag-határra vár. Nem fejezi be korlátlan
ciklussal az eseménysort, és nem készít részben alkalmazott áthaladásról mentést.
A projektcsere sem előzheti meg a futó eseményeket. A még el nem indult próba
naplózott parancsként megmarad a mentésben. A teherautó utoljára érintett csempéje
is a checkpoint része, így betöltéskor nem marad ki és nem ismétlődik meg belépés.

A futtató azonosítója `forestycoon-simulation/2026-10-16.3`. A korábbi futtató
parancsnaplója nem játszható vissza úgy, mintha ezt az eseményrendszert használta
volna. A megszűnt `road` projektág és a régi kopási kötések nem támogatottak;
nincs hozzájuk a játékban átvezető vagy tartalék számítás. A szállított projektek
és a rögzített nyitójelenet adatai frissültek. Hibás projekt megnyitását az Editor
jelzi, a forrásfájlt megőrzi és használható marad.

## Ellenőrzés

`TrafficRuleTests`: alapgráf, tömeg és rakomány, vályúfüggés, kikapcsolás/törlés,
nem módosító előnézet és feltételek, hiányzó kapcsolt objektum, tényleges gép- és
teherautómozgás, eltérő munkakeretek, mentés, függő projektcsere és puszta
parancsnaplóból történő visszajátszás. A hosszú eseménysor 64×64 és 256×256
pályán, hét munkavégzési egységes folytatásokkal is ellenőrzött.

CPU-mérés: `dotnet run --project ForesTycoon -c Release --no-build -- --traffic-benchmark`.
Egy bemelegítő áthaladás után 3000 eseményt dolgoz fel 64×64 és 256×256 pályán,
4 ms-os gazdakerettel és hívásonként 16 determinisztikus munkaegységgel.
A generálás, a sor feltöltése és a grafika nincs a mérésben; ez nem teljes
képkockamérés és nem kemény valós idejű garancia.

Mérés 2026-10-16, Release, .NET 8.0.32, `DOTNET_TieredCompilation=0`:

| Pálya | Esemény | Munkaképkocka | Korlátos folytatás | Összes CPU | Legnagyobb munkaképkocka |
| --- | ---: | ---: | ---: | ---: | ---: |
| 64×64 | 3000 | 7 | 2813 | 30,101 ms | 9,524 ms |
| 256×256 | 3000 | 7 | 2813 | 32,393 ms | 8,696 ms |

Nyers eredmény: `artifacts/traffic-benchmark.txt`. A hét munkaképkocka kevés
stabil percentilisbecsléshez. A 4 ms-os időkeret hívások közötti ellenőrzés,
nem garantált felső faliórakorlát; az itt mért maximumot sem állítjuk 4 ms alattinak.
Az események száma és sorrendje minden esetben megmaradt.

Végső ellenőrzés: **1807/1807 teszt**, az Editor gráfos és önálló ablakos
füsttesztje sikeres. A `TrafficRuleTests` 17 esete a nagy pályás munkaszeletelést,
a naplózott próbát, mentést és visszajátszást, valamint egy félbehagyott eseményt
lecserélő teljes világbetöltést is lefedi. Naplók:
`artifacts/traffic-tests-verified.txt`, `artifacts/traffic-editor-smoke-final.txt`.
