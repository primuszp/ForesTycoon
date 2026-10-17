# Logisztikai döntések a közös gráfos szerkesztőben

2026-10-17 – 5/b: rakodás, gépi munkaválasztás, mozgás és malom.

A játék 33 projektgráfból számolja ezeket a döntéseket: 10 processzor-,
15 forwarder-, 7 teherautó- és 1 malomválasz. Ugyanabban a Szabályok listában,
pipával, közös folyamatábrával, objektum → mező választással, görbével,
számbevitellel és csúszkával szerkeszthetők. Egyedi flotta-ID vagy malom-ID
felülírhatja a típusszabályt; eltávolítása a típusszabályhoz tér vissza.

## Mit állíthatsz össze?

- **Processzor:** munkatempó; kitermelt térfogat; a vágás folytatása;
  mikor induljon a saranghoz; lerakási mennyiség; haladási sebesség;
  munkafogyasztás; áthaladási tömeg; útvonalköltség és szakaszengedély.
- **Forwarder:** munkatempó, sebesség, fogyasztás és áthaladási tömeg;
  fel- és lerakott mennyiség; rönkciklus időtartama, megfogás és elengedés;
  részrakománnyal indulás; mikor induljon a forrásért, a célhoz vagy haza;
  útvonalköltség és szakaszengedély.
- **Teherautó:** fel- és lerakott mennyiség; indulás a rakodásból, akár
  részrakománnyal; megbízás befejezése; útvonalköltség és szakaszengedély.
  A központi fakészletes bemutató jármű rakodási ideje is külön gráf,
  nincs külön beégetett hárommásodperces számítás.
- **Malom:** a lépésben feldolgozott térfogat a készletből és az eltelt időből.
  Alapérték: 0,25 m³/jármű-s. Az értékesítési bevétel továbbra is az érkezéskor
  keletkezik; a feldolgozás nem hoz létre második bevételt.

Például a **Forwarder haladási sebessége** gráfon válaszd a
**Gép / jármű → Rakomány** bemenetet, kapcsold egy közös **Görbe** csomópontra,
és állítsd a pontokat 0 m³ → 2 csempe/munka-s és 20 m³ → 0 értékre. A görbe
kimenetét kapcsold a szabály eredményére. A tényleges gép ezután több fával
lassabban halad. Nem szükséges képletet írni. Az angol kulcs `machine.cargo`;
a megjelenítési nevet, súgót és egységet a játék típusaiból deríti fel az editor.
A világ saját készletei és megfigyelhető mennyiségei is beköthetők forrásként.

A felhasználói adatok forrása `LogisticsResponseSchema.cs`; a tiszta
alapgráfoké `LogisticsRules.cs`. A képletszöveg ezekben csak a fejlesztői
alapgráf előállítására szolgál: a mentett csomópontokat futtatja a játék.
A négy lekérdezési típus (`logisticsProcessor`, `logisticsForwarder`,
`logisticsTruck`, `logisticsMill`) nem új fizikai objektumokat jelent.
A vezérlési állapotgráf továbbra is ugyanarra a tényleges flotta-ID-ra kötődik.

## Kikapcsolás és kötelező motorfeladatok

Kikapcsoláskor választott fix eredmény helyettesíti a gráfot. Például 0
kitermelt térfogat leállítja a kitermelést, 0 indulási döntés az automatikus vezérlésben várakoztatja a
teherautót akkor is, ha tele van. A tempó 0 értéke a munkát állítja meg,
a fenntartási óra tovább jár. A kártya összecsukható magyarázata jelzi,
mikor fut, mit olvas, mire hat, ki használja és mit jelent a fix érték.

A gráf nem teremthet fát, nem lépheti túl a készletet és a kapacitást, nem
másolhatja le a rakomány értékét, és nem hozhat létre hiányzó útkapcsolatot.
A felvétel/átadás, a mozgási állapot léptetése és az útkeresés végrehajtása
motorfeladat. A munkaciklus állapotgráfjának közvetlen indulási utasítása elsőbbséget kap
a válaszgráfhoz képest. A hazahívás a már megfogott rönköt biztonságosan átadja,
majd leadatja a rakományt. Új fizikai munkafázist vagy új játékosparancsot
ezekkel a válaszgráfokkal még nem lehet létrehozni.

A folyamatban levő rönkciklus ideje, megfogási és elengedési pontja mentett
érték. A gráf cseréje a következő ciklus időzítésére hat; a megfogott fa nem
vész el. Hibás aritmetika nem indít régi natív szabályt: biztonsági fix értéket
ad, az útvonalköltség hibája tiltja a szakaszt. A hiba látható marad.

## Szeletelés, mentés és tisztítás

Gépenként, teherautónként, malmonként, útkeresési csúcsonként és a kész útvonal
másolásakor elemenként megszakítható a munka. A nagy sebességű gép mozgása
szakaszonként megszakítható, minden érintett csempe áthaladási eseményt kap;
a gép nem ugorhat át köztes megszakadt útkapcsolatot. A további beszállítást
kereső vizsgálat egy szeletelve felépített beszállítói indexen jár. Ciklusbiztos,
iteratív keresése nem vágja el a hosszú láncokat négy szint után, és a még
markolóban lévő fát is érkező készletként veszi figyelembe. Minden csúcs és
beszállító után visszaenged a hostnak.
A telítettségi, készlet- és útvonal-döntéseket a gráf hozza, az időkeretet a
motor a már meglévő determinisztikus munkaegységekkel tartja.

A mentési formátum maradt 17; a futtató azonosítója
`forestycoon-simulation/2026-10-17.1`. Teljes tickhatáron mentünk; részleges
iterator nincs sorosítva. A visszatöltés újra felépíti a közös szabály- és
paraméterkapcsolatot. A gráfcsere nem kér teljes erdei rátafrissítést.
A függő projektparancs mentése, a naplóból visszajátszás és a teljes/szeletelt
folytatás külön regressziós próbát kapott.

Törölve a korábbi natív tempó-, mennyiség-, sebesség-, fogyasztás-, tömeg-,
malom- és munkaválasztási számítások, valamint a `MachinesEnabled` kapcsoló és
a közvetlen erdő → teherautó kitermelési kerülőág. A régi logisztikai smoke
ugyanazt a gépesített láncot ellenőrzi, mint a gépi smoke; nincs külön modell.
A korábbi natív eredményt olvasó logisztikai kötések nem támogatottak az új
futtatóban. A szállított jelenet és a meglévő helyi projekt csak ilyen
alapcsomópontokat tartalmazott; ezeket egyszeri adatátírással cseréltük ki,
futtatási migrációs ág nélkül. A helyi projekt eredetije:
`artifacts/logistics-project-backup/project.before-logistics.json`.
A jelenet és a helyi világ egyedi szerkesztései megmaradtak.

A nyitójelenet referencia-állapotában csak a `Behaviors` lenyomat változott.
A **telephely építhetősége, induló flotta összetétele és a játékosparancsok
engedélyezési feltételei** a 6. csomagban maradnak; ezeket a leltár továbbra is
hiányként jelzi. A vezérlési állapotgráf már szerkeszthető, a fizikai
végrehajtó állapotai továbbra is kötöttek.

## Ellenőrzés és mérés

A `LogisticsGraphTests`, `FleetBehaviorTests`, `FleetWorkTests`,
`TransportRuleTests` és `LogisticsProjectTests` ellenőrzik a tényleges
kitermelést, rakodást, görbés sebességet, várakozást, hazahívást,
a megfogott fa értékét, a mentett időzítést, az egyedi kötéseket,
a forrásnevek felderítését, a naplót és a szeletelt folytatást.

`--logistics-rule-benchmark`, Release, 128×128 sík pálya, 220 tick,
első 3 bemelegítés; aktív forwarderek, külön forrásokkal. A mért szakaszba
a megfogás és az elengedés is beleesik, az átadás megtörténtét a mérőprogram
külön ellenőrzi:

| Gép | Teljes lépés p95 / p99 / max (ms) | 64 egységes szelet p95 / p99 / max (ms) |
| --- | ---: | ---: |
| 1 000 | 5,157 / 5,888 / 7,179 | 0,295 / 0,324 / 3,270 |
| 10 000 | 14,244 / 15,053 / 18,712 | 0,080 / 0,110 / 0,439 |

A két futás teljes logisztikai checkpointja azonos. Ez célzott rakodási
terhelés, nem 10 000 egyidejű útkeresés vagy teljes erdővilág. A ritka maximum
és a JIT/GC miatt az értékek nem jelentenek merev időgaranciát. Nyers napló:
`artifacts/logistics-benchmark.txt`.

A külön `--rule-host-benchmark` normál erdővilág-próbája 900 s/év tempón,
120 szimulációs másodpercet futtatott, hónaphatárral, 4 ms/60 Hz hostkerettel:

| Pálya / fák | CPU p95 / p99 / max (ms) | Hátralék csúcs / vég (szimuláció-s) |
| --- | ---: | ---: |
| 64² / 15 795 | 4,038 / 4,110 / 13,004 | 2,6167 / 0,0333 |
| 128² / 65 086 | 4,063 / 4,083 / 14,568 | 7,2667 / 0,0333 |

Nem volt eldobott idő vagy új idő nélküli ledolgozó képkocka. A korábbról
ismert havi kapacitáskorlát megmaradt; a közel 7 másodperces csúcshátralékot
ez a logisztikai átvezetés nem oldja meg. Nyers napló:
`artifacts/logistics-host-benchmark.txt`.

Végső ellenőrzés: **1935/1935** teszt sikeres
(`artifacts/logistics-full-tests-release.txt`); a beágyazott és önálló editor
füsttesztje is sikeres (`artifacts/logistics-smoke-release.txt`). A Release
fordítás 0 hiba / 0 figyelmeztetés; a leltár és a diff ellenőrzése sikeres.
A térképréteg kifejezetten a grafika nélküli Rules modulra támaszkodik az
alap útvonalköltséghez; az architektúrapróba továbbra is tiltja a grafikai és
UI-függőségeket, és a Rules modul saját függőségeit is ellenőrzi.
A végső bemenetpróbák a rönkciklus tényleges munkaidejét, az áthaladási tömeg
időlépését és a hazatérési döntés tényleges kopásértékét is ellenőrzik.
A két flottaeset egymás után fut egy folyamatban; a JIT/GC miatt a ritka
maximumokból nem következik a nagyobb flotta jobb általános teljesítménye.


## Telephely és induló flotta – 2026-10-17

A `logisticsDepot` négy új, közös gráfot ad: `depot.allow`,
`depot.processors`, `depot.forwarders`, `depot.trucks`. A telephely szabályánál
ugyanaz a forrás–számítás–eredmény felület jelenik meg. Az Épülethely
objektum mezői: Erdő van a helyén (`site.forest`), Kitermelés érintett
(`site.harvest`), Útkapcsolat (`site.roadAccess`). A Telephelyek objektum
Meglévő telephelyek mezője `depot.count`: az új építése előtti darabszám.

Alapértelmezésben erdő és kitermelési kijelölés nélküli, út melletti hely
engedélyezett; csak az első telephely kap egy processzort, egy forwardert és
egy teherautót. Ez most gráf, nincs mellette külön első-telephelyes kódág.
Az egyedi kötés az építendő telephely csempeazonosítójával működik.
A darabszám gráfok eredményének egészrésze számít, 0–1024 gép fajtánként.
Kikapcsolt engedély 0-val elutasítja az új építést; kikapcsolt darabszám
0-val nem ad abból új gépet. Egyik sem törli a meglévő flottát.

A motor továbbra is száraz, sík, szabad 2×2 épülethelyet követel; épületre,
útra vagy vízre engedélyező gráffal sem lehet építeni. Ha a szerkesztett gráf
erdős helyet engedélyez, a motor szeletelten eltávolítja az ottani erdőt,
bevétel nélkül. Elutasítás előtt nincs világmódosítás. A jelenetfestés
korábbi szabálya megmarad: az alkotó által megadott épülethely alól előbb
letakarítja a festett erdőt, majd ugyanazt az építési gráfot futtatja.

A játékosparancs és a jelenetgenerálás egyaránt `PlaceDepotWork`-öt használ.
A kitermelési kijelölések vizsgálata csempénként, a flotta létrehozása gépenként
ad vissza vezérlést. Nincs korlátlan befejező ág. A szinkron adapter csak a
szokásos, őrzött CLI/teszt út: szeletelt futásból nem hívható.
A mentés a már megkezdett parancs befejezett határán készül; félkész flottát
nem publikál. Azonos darabszámhoz azonos gépsorrend, ID és véletlenmag tartozik.

Külön Release mérés (`--depot-rule-benchmark`, renderelés nélkül):

| Pálya | Vizsgált kijelölt csempe | Új gép | Teljes parancs | 64 egységes szelet p95 / p99 / maximum |
| --- | ---: | ---: | ---: | --- |
| 128×128 | 15 872 | 3072 | 7,040 ms | 0,024 / 0,092 / 3,233 ms |
| 256×256 | 64 512 | 3072 | 2,755 ms | 0,012 / 0,016 / 2,288 ms |

Mindkét eset teljes logisztikai mentési állapota pontosan azonos a szinkron
és a szeletelt futásban. Az első eset hideg kódot is érint; a két teljes
idő nem skálázási összehasonlítás. Ez építési mikrobenchmark, nem teljes
képkockaidő vagy a havi szimuláció kapacitásmérése. A korábbi havi torlódás
külön feladat marad. Következő csomag: a munkára küldési parancsok
engedélyezése; a gazdaság és a többi nyitott tétel az auditban szerepel.


Ellenőrzés: Release fordítás 0 figyelmeztetéssel és 0 hibával; 1938/1938 teljes
teszt; mindkét editor-füstteszt; szabályaudit és `git diff --check` rendben.
Új regressziók: gráfos tiltás mellékhatás nélkül, egyedi kötés és törtszám
kezelése, épületátfedés tiltása, 3072 gép őrzött szeletelt létrehozásának
pontos állapotegyezése, szerkesztett induló flotta jelenetgeneráláskor,
megkezdett építés mentési határa és visszatöltés. A teljes csomag napló-
visszajátszási tesztjei is átmentek.


## Munkára küldés engedélyezése – 2026-10-17

Három `orders.allow` gráf van a processzor, forwarder és teherautó közös
szerkesztőjében. A 0 elutasítja az új megbízást, az 1 engedélyezi a motor
által végrehajthatónak talált út és cél esetén. A grafikon kikapcsolásakor
megadott fix eredmény ugyanezt jelenti. Elutasításkor a régi megbízás,
a markolóban tartott rönk és a rakomány is megmarad. A hazahívás nem tiltható
le ezzel a gráffal.

A Gép objektumnál a fajta, rakomány, kapacitás, kopás és hazahívási állapot
érhető el. A megbízási rakomány a forwardernél a markolóban tartott rönköt
is tartalmazza. A szállítóknál a Forrás / Forrás készlete a most kiválasztott
sarang készlete, nem a korábbi megbízás forrásáé. A Megbízás objektumban:

- processzor: Elérhető kijelölt fa (`order.hasTimber`);
- forwarder és teherautó: A cél malom (`order.destinationIsMill`).

A processzor alapgráfja az elérhető kijelölt fát követeli meg. Ezt a feltételt
nem ismétli meg natív elutasítás: átírható például előzetes munkakiosztás
engedélyezésére. Ilyenkor a gép nem kap hamis `HasWork` állapotot.
A két szállító alapgráfja engedélyez; a felhasználó összekötheti például a
kopási görbét, a minimális forráskészletet és a malomcélt egy engedéllyé.
Az egyedi gép-ID-re kötött gráf a típusszintű döntést helyettesíti.

Az előnézet és a parancs ugyanazt az engedélykiértékelőt hívja, a parancs
az aktuális adatokkal újraellenőriz. A keresések és a járműgeometria a
szeletelt motorútvonalon futnak. A szabály nem hoz létre hiányzó fizikai utat,
nem engedi a forrás és cél azonosságát, és nem rendeli újra az úton lévő
teherautót. Az állapotmódosítás csak az engedély után kezdődik.

Release mikrobenchmark (`--orders-rule-benchmark`, külön futtatva,
renderelés nélkül), hosszú teherautó-megbízás forrás–cél útkereséssel,
telephelyi megközelítéssel és a szeletelt járműgeometriával:

| Pálya | Teljes parancs | 64 egységes szeletek száma | Szelet p95 / p99 / maximum |
| --- | ---: | ---: | --- |
| 128×128 | 22,698 ms | 14 | 0,625 / 0,625 / 0,625 ms |
| 256×256 | 4,432 ms | 28 | 0,406 / 0,850 / 0,850 ms |

Mindkét eset logisztikai és járműállapota pontosan azonos az egyben és a
szeletelten végrehajtott parancs után. Az első eset hideg kódot is érint;
a két teljes idő nem skálázási összehasonlítás. Egy-egy parancs mérése,
a kevés mintás p99 a maximumot jelenti, nem általános képkockaidő-garancia.
A havi szimulációs torlódás külön nyitott feladat marad.


Ellenőrzés: Release build 0 figyelmeztetéssel, 0 hibával; 1947/1947 teljes
teszt; mindkét editor-füstteszt (köztük napló-visszajátszás); audit és diff
ellenőrzés rendben. Új regressziók: mindhárom gépfajta tiltása a korábbi
megbízás és rakomány megőrzésével; előnézet–végrehajtás közös döntése;
felülírható kijelölt-fa feltétel; valódi forráskészlet, cél és kopás, valamint
a markolóban tartott rakomány; hiányzó út és azonos forrás/cél motorvédelme;
mindhárom elfogadott parancs pontos szeletelt állapotegyezése; megkezdett
parancs mentési határa, visszatöltése és eltérő szeletméretű folytatása.
