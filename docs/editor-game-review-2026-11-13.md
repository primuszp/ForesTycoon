# Editor–játék–motor felülvizsgálat, 2026-11-13

## Hatókör és következtetés

A vizsgált kiinduló commit `178ba32`. A felülvizsgálat az Editor.Core → Simulation →
Rules/Engine/Ecology/Map függőségekre, a közös paraméterekre, próbafuttatásra,
időléptetésre, anyagmérlegre, animációmintavételre, hangra és a meglévő
ellenőrzési infrastruktúrára koncentrált. A teljes tesztcsomag futtatása kiegészíti
a célzott kódolvasást; ez nem valamennyi forrássor független auditja, és nem
ökológiai vagy erdészeti terepi validálás.

Az alaparchitektúra megtartandó. A külön Editor.Core, a grafika nélküli
SimulationWorld, a közös RuleProject, a parancsnapló és a határon publikált
világcsere megfelelő alapot ad. Az editor ne kapjon saját erdő-, víz- vagy
gazdasági modellt: a próbák is ugyanazt a szimulációs modult használják.

## Feltárt és javított hibák

| Prioritás | Hely, kiváltó eset | Javítás és ellenőrzés |
|---|---|---|
| P1 | `FixedStepClock.AdvanceWork`: szüneteltetett editorban előre kért idő utolsó tickje a lebegőpontos kivonások után elmaradhatott. | A kifejezett kérés egész tickszáma önálló végrehajtási feltétel. 1, 10 és 180 s kérés teljesül; nincs utólagos extra tick. A félkész tick közben érkező új kérés a már megkezdett tick után teljesül. |
| P2 | `RequestTick` megkerülte a kérési számláló és az akkumulátor túlcsordulás-védelmét. | Közös, változtatás előtti validálás; elutasításkor a hátralék érintetlen. |
| P1 | `TrialService`: a UI által megengedett 10 s esőpróbát a legalább 20 s eseményt váró motor elutasította. | A próba legalább 20 s-os esemény kért időhosszúságú elejét szimulálja. A 10 s esőpróba tényleges világokon tesztelve. |
| P2 | A „0 eső: csak források” próba hagyta továbbfutni a természetes időjárást. | Kifejezett napos, nulla csapadékcsúcsú időjáráskérés. A Core–host összehasonlító teszt ugyanilyen kontrollt futtat. |
| P2 | Negatív vagy túl nagy próbaidő hibás/üres futást, illetve egészszám-túlcsordulást okozhatott. | A Core belépési pontjai 0–600 s időt és 0–40 intenzitást fogadnak; a UI továbbra is 10–180 s-ot kínál. |
| P2 | `AnimatedGlbModel.Channel`: érvényes végpontok mellett is nullává válhat a köbös kvaternió-interpoláció; normalizálása NaN. | Kifejezett adathiba nulla vagy nem véges interpolánsnál, dupla pontosságú normaképzés. Nincs önkényes identitásrotációra cserélés. |
| P2 | A hangmix minden hanghoz kontextust váltott és delegáltat foglalt. | `IAmbientOutput.SetGains` köteg, natív implementációban egy kontextusaktiválás/visszaállítás egy mixerfrissítéshez; értéktípusú kontextusőr, veremtömbök. A külön eseményindítás továbbra is saját aktiválást használ. |

A natív audiofolyamat mintavétele `alcMakeContextCurrent` alatt
`OALContext::ConnectMixerToDevice` / `AUGraphUpdate` hívásokat mutatott.
Ez konkrét oka a kontextusváltások csökkentésének, nem általános FPS-ígéret.
Az audioállapot a tulajdonos szálon marad, másik ablak korábbi kontextusát
visszaállítja. A teljes OpenAL-driver működésére nincs valós idejű garancia.

## Megvalósított editor–játék kapcsolat

Nyolc hangparaméter került a közös GameTuning-katalógusba és a szállított
`default.project.json` dokumentumba:

- átmeneti időállandó valós másodpercben;
- teljes szélhangerő referencia-sebessége m/s-ban;
- teljes esőhangerő referencia-intenzitása mm/h-ban;
- szél, eső, évszak, mennydörgés és szarvas alap-hangszintje.

Az editor Játékparaméterek nézetében, a Megjelenítés / Környezeti hangok alatt
érhetők el. A meglévő tuningparancs kezeli az Undo/Redo-t; a projektfájl,
alkalmazás és mentés a meglévő név szerinti tárolást használja. A játék
`Viewport.UpdateAudio` ugyanebből a világparaméterből frissíti a hangkeverőt.
A játékos hangerőszabályzói továbbra is külön szorzók. Régi projektből hiányzó
mezők a szállított alapértékeket kapják; meglévő kulcsot nem neveztünk át.

A `TuningSpec.Presentation` megkülönbözteti a megjelenítési számokat:
ezek olvasása nem futtat szimulációs viselkedésgráfot a renderképkockában.
Az új értékek a közös editorlistában látszanak, de nem kínálnak nem létező
gráfkötést. A katalógus alapállapota a hivatalos exportparanccsal újragenerálva.

A lefedettségi ellenőrzés eredménye: **259 hook, 113 hangolható szám,
61 szerkeszthető motorszabály, 91 jelenetadat; regisztrált lefedettségi hiány nincs**.
Ez a regisztrált szerződések elérhetőségét bizonyítja, nem azt, hogy minden
forráskódbeli konstans szerkeszthető vagy minden funkció kész.

## Felelősségek és további fejlesztési sorrend

| Terület | Megtartandó közös alap | Következő lépés / elfogadási feltétel |
|---|---|---|
| Szabályok, tuning | Egy RuleProject, közös validálás és katalogizálás. | Új számhoz mértékegység, tartomány, alapérték, editorparancs, mentési és tényleges futásidejű teszt. |
| Editor | Core dokumentum, tranzakciók, keskeny olvasófelületek. | `CurrentRulesView` és a nagy HUD további bontása csak konkrét változtatáskor; ne keletkezzen párhuzamos állapottulajdonos. |
| Szimuláció | Fix tick, folytatható munkaszeletek, explicit publikálás. | Nagypályás p95/p99/max mérés és hátralék megfigyelése külön, zavartalan gépen; a mérési idő ne döntsön a játékállapotról. |
| Víz/erdő | Készlet- és áramláskönyvelés, egy közös ökoszisztéma. | Faj- és termőhelyfüggő növekedési, csapadék–párolgási referenciaadatokkal kalibrálás; érzékenység- és időlépés-vizsgálat. |
| Animáció | glTF csatornák, klipkeverés, szimulációtól elkülönített póz. | A klip–akció kötések és előnézet elkészült; lásd az alábbi kiegészítést. A morph target animáció jelenleg nem támogatott. Hibás köbös klip most kivételt jelez; editoros import-előellenőrzés hasznos következő lépés. |
| Hang | Eszközfüggetlen mixer és OpenAL-kimenet, most közös tuning. | A hangfájl-/eseménykötések, előhallgatás, térbeli géphangok és korlátos hangforráskészlet elkészült. Doppler és hangárnyékolás további, külön feladat. A jelenlegi rendszer környezeti hangokat kezel, nem teljes játék-audiorendszer. |
| Render | Backend-szerződések, OpenGL-implementáció, chunkos gyorsítótárak. | Metal csak teljes backend- és natív ablakteszttel tekinthető késznek; most nincs ilyen implementáció. |
| Mentés | Verziózott checkpoint, parancsnapló, tickhatár. | Régi kiadások fixture-jeivel migráció és hosszú mentés–betöltés–visszajátszás ellenőrzése. |

A 15 motoroldali katalóguselem nem automatikusan szerkesztőhiba: ide tartozik
a parancsvégrehajtás, checkpoint, anyagmegőrzés, útkeresés és kinematikai
integrálás is. Ezek algoritmusok/invariánsok; a játéktervezési paramétereket és
döntési gráfokat érdemes szerkeszthetővé tenni, az invariánsokat a motorban tartani.

## Irodalmi összevetés és határai

- [Fiedler: Fix Your Timestep](https://github.com/mas-bandwidth/gafferongames/blob/main/content/post/fix_your_timestep.md): fix időlépés és megjelenítési interpoláció. A projekt interaktív útja megőrzi a hátralékot; a régi szinkron `Advance` külön, dokumentált eldobási politikát használ. A kézi kérések javítása nem változtatja meg a fizikai időlépést.
- [FAO 56, 8. fejezet](https://www.fao.org/4/x0490e/x0490e0e.htm): csapadék, lefolyás, kapilláris utánpótlás, evapotranszspiráció és mélybeszivárgás explicit vízmérlege. A projekt készletkönyvelése ezzel az elvvel összevethető. Ettől az egyszerűsített erdőmodell még nem FAO-56-implementáció vagy mérési adatokkal validált erdőhidrológia.
- [Khronos glTF 2.0](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html): lineáris rotációnál slerp, köbös interpoláció után normalizált kvaternió szükséges. A javítás a normalizálhatatlan interpolánsra ad kifejezett hibát.
- [OpenAL 1.1](https://www.openal.org/documentation/openal-1.1-specification.pdf): a forrásműveletekhez aktuális kontextus tartozik. A kötegelés megőrzi ezt és a korábbi kontextust; a natív többkontextusos próba ellenőrzi a használatot és takarítást.

## Ellenőrzés

Riportok: `artifacts/review-2026-11-13/` (generált, nem verziózott).
- Release solution build `-warnaserror`: 0 figyelmeztetés, 0 hiba (`build.log`).
- Natív editor- és önállóablak-próba: sikeres (`editor-smoke.log`); az önálló ablak képernyőképe vizuálisan ellenőrizve.
- Többablakos játék/előnézet/editor életciklus: sikeres (`window-lifecycle.log`). A macOS-driver egy textúrafigyelmeztetést kiírt; a teszt sikeressége nem bizonyít minden GPU-n képi hibamentességet.
- Natív audio, nyolc hang, két kontextus, némítás és takarítás: sikeres, az új kötegelt implementációval 13,732 s teljes tesztidő (`audio-smoke-final.log`). Nincs kontrollált előtte–utána időmérés, ezért gyorsulási szorzót nem állítunk.
- A mixer új allokációs tesztje bemelegítés után 1000 frissítésre 0 menedzselt bájtot és pontosan egy kimeneti köteget igazol frissítésenként. Ez a mixerre vonatkozik, nem az OpenAL-driver belső memóriahasználatára.
- Útvonal- és parancsbenchmark: 128×128 és 256×256 pályán a teljes/szeletelt állapot és útvonal egyezik. A 64 egységes parancsszeletek legnagyobb mért ideje 0,230 / 0,104 ms; az előnézeté 0,845 / 0,126 ms (`orders-benchmark.log`). Szintetikus, rövid mérés más ellenőrzések mellett; nem teljes játék-FPS vagy reprezentatív p99-garancia.
- Kiinduló teljes csomag: 1913/1913 sikeres. A végső teljes eredmény lent szerepel.
- A köztes teljes futás három katalógus/szótár/lefedettségi hiányt jelzett az új hangbejegyzésnél; mindhárom metaadatforrás frissítve. Egy ismétlés párhuzamos tesztfolyamatok közös `artifacts/algorithm-integration/world.json` útvonala miatt ütközött; a végső teljes futás elkülönítve indult.
- `git diff --check`: tiszta.


Végső teljes Release tesztfutás: **1926 sikeres, 0 hibás, 0 kihagyott**,
4 perc 16 másodperc; `review-complete.trx` és `full-final.log`.
A végső futás tartalmazza az időléptetés, a próbafuttatás, az animáció, a
hangkeverés és a katalógus-integráció összes mostani regresszióját.
A változtatások helyi munkafában vannak; commit és push nem készült.


## Folytatás: szerkeszthető animáció- és hangkötések

Elkészült a 9-es RuleProject közös megjelenítési profilja, a Core szerkesztési
tranzakciója és Undo/Redo-ja, a világonkénti futásidejű profil, a migráció és a
checkpoint/export integráció. Az editor új lapja két szarvasanimációt és nyolc
hangkötést kezel. A játék és az előnézet ugyanazt a klipmintavételt használja;
a natív WAV-előhallgatás az éles OpenAL-kimeneten működik.
Használat és korlátok: [presentation-bindings.md](presentation-bindings.md).

A natív UI-teszt valódi szövegbevitellel, visszavonással, alkalmazással és
checkpointtal sikeres; a mozgó és szüneteltetett előnézet képi ellenőrzése is
sikeres. A többablakos életcikluspróba átment. A macOS OpenGL-driver meglévő
textúrafigyelmeztetése továbbra is megjelenik. Alvó kijelző mellett a GLFW
monitorlekérdezése natív hibával leállt; aktív kijelzővel a próbák sikeresek.
Windows/Linux natív eszköztesztet ez a futás nem igazol.
Eredmények: `artifacts/presentation-bindings/`.

A bővítés teljes Release regressziója: **1937 sikeres, 0 hibás, 0 kihagyott**,
4 perc 4 másodperc (`complete.trx`, `full-tests.log`). A végső solution build
`-warnaserror` mellett 0 hibával és 0 figyelmeztetéssel sikeres. A szállított
alapprojekt a kötéseket kifejezetten tartalmazza; a régi dokumentumokhoz a
migrációs alapértékek maradnak. A natív audioteszt mind a nyolc hangot és két
kontextus függetlenségét igazolta (`audio.log`). A változások továbbra is helyiek;
commit vagy push nem készült.
A szállított alapprojekt végső frissítése után további **23/23 célzott teszt**
sikeres (`final-targeted.trx`); a lefedettségi generátor `--check` és a
`git diff --check` ellenőrzése is tiszta.


## Folytatás: térbeli géphangok és motoralapok konszolidációja

A térbeli géphangok és a három járműtípus procedurális animációeseményei
elkészültek; a korábbi javaslat ezen része lezárult. Használat és támogatási
határok: [megjelenítési kötések](presentation-bindings.md). A későbbi munka
a játékmotor közös tickmagjára, parancsvégrehajtására, hostkapcsolatára és
blokkolási hibáira is kiterjedt: [motoralapok review](engine-foundation-review-2026-11-13.md).
