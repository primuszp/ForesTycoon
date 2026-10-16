# Fák kezdő méretgörbéi – 2026-10-16

A korábbi természetes erdőmegújulás commitja: `b12be1e`.
A mostani átállításban minden létrehozott fa fizikai kezdő méretét a projekt
három közös válaszgráfja adja. A régi `ForestTreeGrowth.Initial` képleteit
eltávolítottuk; a megmaradt, azonos nevű adapter csak a fafaj adatait adja át
a közös gráffuttatónak és az eredményt méterben tárolja.

| Gráf | Eredmény | Korábbi gráfok olvasható eredménye |
| --- | --- | --- |
| `forest.initialHeight` | Kezdő magasság, m | Nincs |
| `forest.initialDiameter` | Kezdő törzsátmérő, m | `initial.height` |
| `forest.initialCrown` | Kezdő koronasugár, m | `initial.height`, `initial.diameter` |

A kiértékelés sorrendje magasság → átmérő → korona. Önmaga vagy egy későbbi
kimenet nem olvasható; nincs ciklikus kiértékelés. A korona saját összefüggéssel
követheti a valóban kiértékelt magasságot vagy átmérőt. A nulla kimenet
nulla méretet ad, nem kapcsol vissza beégetett minimumot vagy tartalék képletet.
A biztonsági kimeneti tartományok: 0–1000 m magasság és koronasugár, 0–100 m átmérő.

## Közös szerkesztő és felfedezett források

Mindhárom gráf a Szabályok közös listájából nyílik. Ugyanaz a forrásválasztó,
csomópont-, kapcsolat-, művelet- és görbeépítés használható, mint a többi
ökológiai válasznál. Képletírás nem szükséges. A konstansok számmal vagy
csúszkával szerkeszthetők. Nincs új külön méretgörbe-panel.

Az objektumokat és kijelzett mezőneveket a `TreeInitialSizeSchema.cs` annotált
interfészeiből fedezi fel a meglévő rendszer:

- **Új fa:** `tree.age` (az egyed kezdő életkora), `tree.random` (0 ≤ seedelt
  érték < 1), `tree.scale` (a játékban 1), `tree.planted` (ültetett fa: 1;
  generált, festett vagy természetes újulat: 0).
- **Fafaj:** `species.matureAge`, `species.matureHeight`, `species.matureDiameter`,
  `species.crownRatio`.
- **Kezdő méretek:** `initial.height`, `initial.diameter`, a fenti függőségi korláttal.

Az alapgráfok megőrzik a korábbi életkorfüggő méretburkolat alakját. A magasság és
az átmérő életkori telítődése, a kezdő méretarányok és a méretbeli változatosság
0,88 + seedelt érték × 0,24 szorzója most csomópontokként látható. A korona
alapgráfja a 0,15 m-es minimum és a magasság × faj-koronaarány nagyobbikát adja.
Ezek szerkeszthető kezdő projektadatok; játék közben a mentett gráf fut.

A gráfok bármelyikét lecserélheted egyetlen forrásra vagy állandóra. Például
az ültetett jelzővel külön kezdő méretet adhatsz az ültetett és a természetes
fáknak; a koronasugarat közvetlenül az átmérőre is kötheted.

## Mely útvonalakon fut?

A generált erdő, a teljes és helyi erdőfestés, a játékosi egyedi és területi
ültetés, valamint a természetes újulat a világ tényleges projektprogramját
adja át a közös `ForestTreeStore.CreateWork` létrehozási mechanizmusnak.
A szinkron és a háttérben készülő pályagenerálás is ugyanazokat a gráfokat kapja.
A diagnosztikai mintafák azonos alapgráfokat használnak, önálló képletük nincs.

A gráfcsere **nem méretezi át a már élő vagy mentett fákat**. A létrehozott
méret, faazonosító, seed és időhorgony továbbra is a checkpoint része.
A későbbi növekedést a külön, már gráfos `forest.growth` szabály adja.

A helyválasztás, a seed, a generált egyedek kezdő koreltérése, az induló állomány
létszáma és az ültetvény 6×6-os elrendezése változatlan mechanizmus/adat.
Az újulat létszámát a korábbi `forest.seedlingCount` gráf adja. Ezeket nem
számítjuk újra a méretgráfokban. A kezdő életkorok kiosztásának és az induló
állomány létszámának általános gráfosítása nem része ennek a lépésnek.

## Szeletelt létrehozás és helyes publikálás

A létrehozási helyek feltöltése, a rendezés egy-egy külső lépése és minden új
fa kiértékelése külön determinisztikus munkaegység. A rács és az egy csempén
létrehozható újulat 128 egyedes felső határa korlátos; az egy gráfra jutó 256
csomópontos határ is megmaradt. A rácspuffer `ArrayPool<int>`-ből jön,
`finally` ágban visszakerül, és csak a tényleges rácsméretet járjuk be.
Így a finomabb szeleteléshez nem kell minden csempén új rácstömböt foglalni.

A generálás, festés és újulat közvetlenül a létrehozás iterátorát fogyasztja.
Az ültetés `PlantCoreWork` / `PlantInAreaWork` útvonalon folytatható;
a területültetési parancs is ezt futtatja. A szinkron `Create`, `Plant` és
`PlantInArea` adapterek a diagnosztikai/tesztelési felhasználást szolgálják,
nem az interaktív parancs új korlátlan befejező ágai.

A részben létrehozott világ vagy parancs nem publikálható mentésként.
Az `AtTickBoundary` mentési kérés a teljes logikai művelethatárra vár, és nem
meríti ki korlátlan munkával az iterátort. A párhuzamos generálás leállításakor
az iterátor eldobása visszaadja a bérelt puffert, a korábbi élő világ megmarad.
A sorsolás csak a seedből és a stabil egyedazonosítóból készül, nem a munkaszeletek
időzítéséből. A futtató `forestycoon-simulation/2026-10-16.9`.

## Jelenetek és ellenőrzések

A külön mappába újrarögzített nyitóreferenciában csak az `Ecology` és `Behaviors`
lenyomat változott. A gráfmotor double kiértékelése és a végső float tárolás
miatt az alapképletek azonos alakja mellett is lehetnek kis numerikus eltérések.
A referencia saját pályája, küldetései és eddigi gráfjai megmaradtak. A szállított
nyitójelenet kezdő geometriája az új alapgráfokból újrarögzített állapotot kapta.
A helyi editorprojekt korábbi mentett fáit ezzel szemben megőriztük; csak a
három új alapgráfot kapta meg a projekt és a beágyazott állapot.
Másolat: `artifacts/initial-size-project-backup/project.before-initial-size.json`.
Nincs régi méretképletet vagy régi futtatóverziót fenntartó betöltési ág.

A `TreeInitialSizeGraphTests` a tényleges projektgráfok hatását vizsgálja a
festés, ültetés, újulat és pályagenerálás során. Ellenőrzi a kimeneti függőségeket,
a nulla méret natív visszahelyettesítés nélküli működését, az ültetett jelzőt,
a 128 egyedes szeletelt létrehozást és seedazonosságot, a mentett egyedeket,
a meglévő fák méretének megőrzését, az ültetési parancs közben kért mentést,
a hónaphatárt és a parancsnaplós visszajátszást. A fajkatalógus minden fajra
ellenőrzi az alapgráf monoton méretburkolatát. A közös névteszt az új források
objektum- és mezőneveit is ellenőrzi.

Az editor füsttesztje a három új gráfot is megnyitja és képként rögzíti:
`artifacts/rule-editor/initial-size-height-graph.png`,
`artifacts/rule-editor/initial-size-diameter-graph.png`,
`artifacts/rule-editor/initial-size-crown-graph.png`.
A nagy pályás generálás/területszerkesztés headless CPU-mérése a meglévő
`--foreground-benchmark` paranccsal ismételhető meg.


## Végső eredmények

- Release editor build: 0 hiba, 0 figyelmeztetés (`artifacts/initial-size-build-final.txt`).
- Teljes tesztcsomag: **1859/1859** (`artifacts/initial-size-tests-final.txt`).
- Az editor gráf- és önállóablak-füsttesztje is sikeres
  (`artifacts/initial-size-editor-smoke.txt`); a koronagráf képét szemrevételeztük.
- A helyi projekt validálható, betölthető, egy ticken át futott, és a korábbi fái
  megmaradtak (`artifacts/initial-size-local-trace.json`).
- `git diff --check` tiszta.

A nagy pályás mérés Release, .NET 8.0.32, `DOTNET_TieredCompilation=0`,
ablak és renderelés nélkül futott, a tesztcsomag befejezése után.
A `SimulationFrameRunner` keretenként 4 ms célkeretet használ, belül
16 egységes determinisztikus parancsmunkákat fogyaszt. Az alábbi sorok a
keret CPU-munkáját mérik, nem egyetlen fa vagy egyetlen gráf költségét.
A generálási háttérmunkát és a végső publikálást is a tényleges útvonal kezeli.

| Pálya | Művelet | Teljes szinkron művelet (ms) | Keretek | CPU p95 / p99 / max (ms) |
| --- | --- | ---: | ---: | --- |
| 128×128 | Generálás | 487,017 | 209 | 4,035 / 12,128 / 17,266 |
| 128×128 | Terepecset + út + nyom + ültetés + kijelölés | 140,250 | 19 | 15,897 / 15,897 / 15,897 |
| 256×256 | Generálás | 800,309 | 318 | 4,009 / 9,683 / 15,640 |
| 256×256 | Terepecset + út + nyom + ültetés + kijelölés | 159,104 | 40 | 4,007 / 4,008 / 4,008 |

Mindkét méretnél a teljes checkpoint, a mentés/betöltés és a további 20 tick
állapota pontosan egyezik. Napló: `artifacts/initial-size-foreground-benchmark.txt`.
A 4 ms célkeret nem kemény képkockaidő-garancia: ritka, akár 17,266 ms-os
kiugrás ebben a headless mérésben is volt. A napló több GC-vel egybeeső lassú
szeletet és GC nélküli kiugrást is jelez; az okokat nem mossuk össze a
kezdőméret-gráfok költségével. A korábbi, folyamatos szimulációban mért
34,6 ms-os ritka kiugrás okát ez a generálási/szerkesztési mérés nem zárja le.
