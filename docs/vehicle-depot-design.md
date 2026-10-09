# Telephely, járműpark és munkautasítások – terv

A Transport Tycoon mintájára a járművek nem maguktól jelennek meg. Mindegyiket megvesszük, a telephelyen állnak, és
a játékos küldi őket dolgozni. A mostani automatikus indítás (vágás kijelölése → gépek megjelennek, „Teherautó
indítása”) helyébe lép.

## Alapelvek

- **Minden járműnek gazdája és helye van.** Megvásárlás után a telephelyen parkol, onnan indul, és oda tér vissza.
- **A játékos utasít, a jármű végrehajt.** Egy kattintás a járműre, egy a célra – mint a TT-ben a „Go to”.
- **A szállítás láncát a játékos rakja össze:** processzor a vágásba, forwarder ugyanoda, teherautó rakodó → malom
  körjáratra. Ha egy láncszem hiányzik, a lánc megáll, és a felület megmutatja, hol.
- **Kezdetben egy-egy jármű:** egy processzor, egy forwarder és egy tehergépkocsi áll a telephelyen. Továbbiak
  később vásárolhatók, amikor lesz pénzügyi rendszer.

## Telephely (gépudvar)

- **Épít → Telephely:** 2×2 sík csempe, út mellé (mint a malom). Modell a gyűjteményből: `hangar_001` csarnok,
  mellette `fence_wood_*`, `barrel_*`, `generator`, `liquid_storage_*` kellékek. A repó saját, egyszerű
  csarnokmodellt kap tartaléknak.
- Egy térképen több telephely is lehet. Új játéknál az első telephely helyét a játékos választja ki. Ott áll a
  kezdő három jármű.
- **Telephely-ablak (kattintás az épületre):** a parkoló járművek listája ikonnal és állapottal; „Vásárlás” gomb
  (később); „Indítás” járművenként.

## Járművek és utasítások

| Jármű | Hol jár | Utasítás (TT-stílusban) | Mit csinál |
|---|---|---|---|
| **Processzor** | úton lassan (~20 km/h), közelítő nyomon, vágásban | „Kitermelés itt:” → vágás kijelölése | Odamegy, dönt, darabol, a nyom mellé sarangol. Ha elfogyott a fa, visszamegy a telephelyre (vagy új utasítást vár a helyszínen – beállítható). |
| **Forwarder** | úton lassan, nyomon, vágásban | „Közelítés innen:” → vágás | A sarangokat a rakodóra hordja. Ha a vágás kiürült, hazamegy. |
| **Tehergépkocsi** | csak úton | **Menetrend:** 1. rakodás (rakodó), 2. lerakás (malom) – ismétlődő körjárat | Mint a TT menetrend: a rendeléseket listába vesszük, a teherautó körbe járja őket. „Teli rakományra vár” kapcsoló a rakodónál. |

- **Utasítás menete:** jármű kiválasztása (a telephely-ablakból vagy a térképen kattintva) → „Küldés” gomb → a
  kurzor célkijelölővé válik (zászló ikon) → kattintás a vágásra/rakodóra/malomra. Érvénytelen célnál (nincs
  útkapcsolat, nincs nyom) piros jelölés és magyarázat.
- **Útvonal:** a telephelytől az úthálózaton (meglévő `RoadPathfinder`), majd a nyomokon (a mostani gép-útkereső). Az
  erdei gép az úton is a saját kerekén megy, lassan. (Lásd a nyitott kérdést a trélerről.)
- **Állapotok a járműablakban:** „Telephelyen”, „Úton a vágáshoz”, „Dönt”, „Közelít”, „Rakodásra vár”, „Szállít a
  malomba”, „Hazafelé”. Mindegyik mellett a cél neve, és egy „Mutasd” gomb, ami odaviszi a kamerát.
- **Vissza a telephelyre:** minden járműnél gomb; a jármű befejezi a mostani mozdulatot, és hazamegy.

## Játékállapot és mentés

- `Depot` (csempe, footprint) a logisztikában a malmok mellett.
- `Fleet`: járművek tulajdonnal és utasítással. A mostani `ForestMachine` megmarad, de a `Site` helyett
  `Order`-t kap (cél vágás, vagy null = telephely), és útvonala a telephelyről indul. A teherautó (`Vehicle`)
  `SourceTiles/SawmillTileId` helyett menetrendet kap: `(rakodó csempe, malom csempe)` lista.
- Új parancsok (a parancsnaplóba, visszajátszható): `PlaceDepot`, `AssignMachine(machineId, siteIndex)`,
  `SetTruckSchedule(truckId, stops[])`, `SendHome(vehicleId)`. Később: `BuyVehicle(kind, depot)`.
- Régi mentések: ha nincs telephely, a betöltés a meglévő gépeket „telephely nélkülinek” jelöli, és a mostani
  viselkedéssel dolgoznak tovább, amíg a játékos telephelyet nem épít.

## Megvalósítás lépései

1. Telephely-épület (elhelyezés, modell, tartalék, mentés) és a kezdő három jármű a telephelyen.
2. Járműablak a telephelyhez: lista, kiválasztás, „Mutasd”.
3. Erdei gépek utasítása: célkijelölés, út + nyom útkeresés a telephelyről, hazatérés. Az automatikus gépindítás
   megszűnik.
4. Teherautó-menetrend: rakodó → malom körjárat, várakozás teli rakományra. A „Teherautó indítása” gomb megszűnik.
5. Később: vásárlás és eladás, üzemeltetési költség (üzemanyag ár, karbantartás a telephelyen), gépek kopása.

## Nyitott kérdések

1. **Tréler:** a valóságban a processzort és a forwardert trélerrel (lowbed) viszik közúton. Elég, ha lassan
   „maguktól” mennek az úton, vagy legyen trélerszállítás külön járművel?
2. **Kezdőtőke és árak:** a vásárlás előtt kell egy pénzügyi rendszer (bevétel a malomtól m³-enként, kiadások).
   Most tervezzük meg, vagy előbb a telephely és az utasítások készüljenek el pénz nélkül?
3. **Kiürült vágás:** a gép automatikusan hazamenjen, vagy álljon meg a helyszínen új utasításra várva?

## Megvalósítva (2026-10-09)

- **Egységes úthálózat:** a közelítő nyom a legolcsóbb úttípus (30 eFt/csempe, csak keréknyom). Az útkereső a jó
  utakat választja (aszfalt 1, makadám 1,15, nyom 3,5+ költség/csempe); a teherautó a nyomon is jár, de nagyon
  lassan és erősen dülöngél. Kereszteződésben a nyom ívesen fordul balra és jobbra.
- **Sarangok:** a játékos jelöli ki őket (Termel → Sarang) nyom vagy út mellé. A processzor a vágásban dönt, és a
  legközelebbi saranghoz hordja a fát (3 m³-es fordulókkal): a távolság üzemanyagba és teljesítménybe kerül. A
  sarang modellje a fával együtt nő.
- **Utasítások:** processzor → vágás; forwarder és teherautó → forrás sarang, majd cél (másik sarang helye vagy a
  fűrészmalom). A fa addig vándorol gépről gépre, amíg a malomba nem ér; ott fafaj szerinti áron bevétel lesz.
- **Egyenleg:** bevétel a malmokból; kiadás az építésre, az üzemanyagra (620 Ft/l), a javításra és a szervizre.
- **Kopás és meghibásodás:** a munka koptat (nyomon, rakottan, döntéskor jobban); a kopott gép többet fogyaszt,
  lassabb, és gyakrabban romlik el. Elromlott gépet a helyszíni szerelő 40 mp alatt megjavít (díj + a kopás egy
  része eltűnik); a telephelyen álló gépet szervizelik. A meghibásodás visszajátszható (járművenkénti generátor).
