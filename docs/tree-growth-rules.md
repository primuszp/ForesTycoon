# Fanövekedés és közös szabályszerkesztő

A `forest.growth` a projekt rendes, alapértelmezetten bekapcsolt gráfja.
A Szabályok lapon „Fák növekedése” néven szerepel. Külön célt ad:

- `tree.diameterGrowth`: éves átmérő-növekedés, méter/erdőév;
- `tree.heightGrowth`: éves magasságnövekedés, méter/erdőév;
- `tree.crownGrowth`: éves koronasugár-növekedés, méter/erdőév.

A gráfon a tervező választja ki, mely tulajdonság milyen görbén és milyen
erősséggel hasson az egyes rátákra. A három cél kapcsolatai külön módosíthatók,
kikapcsolhatók és törölhetők. A fa végleges méretét továbbra is a motor
interpolálja a mentett horgonyidőből és az éves rátákból.

## Alapminta

A termőhelyi alkalmasság, fényellátottság, víz, egészség, koronatér és évszak
szorzói együtt adják a növekedés környezeti feltételeit. A vastagodás alapja a
fafaj sugárnövekedési rátájának kétszerese; az átmérő növekedése inverz görbén
lassítja ezt. A magassági növekedés a faj alaptempójából indul, és a faj
magassági határának elérésével nullára csökken. A korona a hajtásból és a
vastagodásból számolt két hozzájárulás közül a nagyobbat kapja.

Az évszakválasz szerkeszthető, kilencpontos görbe. Ez a korábbi kódbeli
koszinusz helyét veszi át; a köztes évszakértékek darabonként lineárisak.
A koronatér gyökválasza és az átmérő inverz válasza a közös `RuleCurve`
görbecsaládjai, az előnézet és a futtató ugyanabból a definícióból dolgozik.
Az összesítés minden vizuális hatásnál választható: összeg, maximum, minimum.
Kikapcsolt kapcsolat nem vesz részt a minimum/maximum kiválasztásában.
Az erősség forrása bármely olvasható mező lehet, külön numerikus szorzóval.

A kódnevek angolok, a forrásválasztóban magyar objektum- és tulajdonságnevek
láthatók. Új mezők a ráták mellett: `crownRadius`, `relativeHeight`,
`radialRate`, `heightRate`, `crownRatio`, `crownSpread`, `yearFraction`,
valamint a stabil `treeId`.

## Egyetlen végrehajtási út

Az ökológia minden rátafrissítéskor lezárja az előző növekedési intervallumot,
nullázza a három kimeneti rátát, majd a projekt `growth` időzítésű szabályait
futtatja. Ez az út szolgálja ki a havi határt, ültetést, újulatot, betakarítás
utáni szomszédfrissítést és környezeti rátafrissítést is. Egy frissítésen belül
a további hatás már látja az előző ráták eredményét; a korona ezért olvashatja
az előbb kiszámolt átmérő- és magassági rátát.

A `growth` időzítés csak a három növekedési rátát írhatja, `set` vagy `add`
móddal, időarányos szorzás nélkül. Nem hajthat végre topológiát módosító
műveletet. A havi életciklus másik időzítés: előbb az egészség, stressz és
elhalás szabályai futnak, majd a túlélők rátái frissülnek.

A `ForestTreeGrowth` régi `Rates`, `RatesWithResources`, `Factor`, `Season`,
`Shape` számításai és a `forest.growth` natív viselkedéskötés törölve vannak.
Az előkészített geometria nem tárol második növekedési képletet. A fajok
induló fizikai méretét előállító generálási adat és a méretinterpoláció
megmaradt; ezek nem párhuzamos rátafrissítések. Projektgazda nélkül az
önálló ökológiai modell nem talál ki növekedési rátát.

## Szeletelés, mentés és azonosítók

A rátafrissítés csempénként és faegyedenként folytatható. A közös RuleEngine
ugyanazt a mentett programot futtatja; a munkaegységek sora nem függ a faliórától.
A csempére ideiglenesen bekötött nézet eredeti objektumlistája és havi ideje
`finally` ágban helyreáll, megszakítás és világcsere esetén is. A korábban
kiválasztott fa nem változhat meg egy helyi rátafrissítés miatt.

Egy szerkesztési parancs a saját folyamatban lévő növekedési kiértékelését
folytathatja; más parancs vagy mentés nem előzheti meg. A mentés teljes
logikai tickre vagy parancshatárra vár. A halasztott kezdeti/frissített
rátaszámítás jelzője a checkpoint része. Mentés és napló-visszajátszás
ugyanazt a projektet és rátákat állítja vissza.

A fák saját szabályállapotai a stabil faazonosító szerint tárolódnak.
A csempénkénti rátafrissítés nem méretezi a teljes állapotot a csempe fáinak
számára. Népességnövekedéskor az új helyek szeletelten inicializálódnak, a
meglévő értékek megmaradnak; elhalás nem rendelheti át őket a következő fához.

## Egy szerkesztési munkaterület

A közös listában találhatók az objektumhatások, folyamatkapcsolatok,
munkaciklusok, globális készletek és áramlások, valamint a sablonok.
A külön képletszöveges szabálypanel megszűnt. Egy korábbi képlet ugyanabban
a munkaterületben számítási csomópontokként látható és módosítható.
A teljes szabályfeltétel is ilyen gráf lehet. A megszokott kapcsolatos
hatásgráf megmarad az egyszerű szorzós/görbés hatásokra.

A régi kifejezés puszta megnyitása nem módosítja a projektet. Szerkesztéskor
`RuleEffect.Computation` vagy `WorldRule.ConditionGraph` tárolja a tényleges
csomópontokat. A motor közvetlenül fordítja őket; nem írja vissza kerekített
képletszövegként. A modell elutasítja ugyanazon hatás több egyidejű tárolási
alakját. A korábbi mentett képletek tovább futtathatók, nincs automatikus
szabályátvezetés.

A folyamatkapcsolatok és globális készletáramlások meglévő végrehajtási
jelentése megmarad. A közös menü nem jelenti azt, hogy a globális, egyidejű
áramlásfeloldás sorrendi objektumátadássá változott. A közös Vissza/Újra a
folyamat-, vezérlő- és készletadatokat is megőrzi.

## Ellenőrzés

A `TreeGrowthGraphTests` ellenőrzi a három rátát, a látható környezeti
kapcsolatokat, a magassági határt, a koronakiválasztást, a szerkesztett
évszakgörbét, az új görbék és a fordított program egyezését, a natív tartalékág
hiányát, a stabil saját állapotot, a parancs saját kiértékelésének folytatását,
a szeletelést, mentést és visszajátszást. A kis ökológiai tesztvilágokhoz a
`ProjectGrowthFixture` a valódi projektgráfot köti be; nem másolja le annak
növekedési képletét. Az editor füsttesztje tényleges kapcsolatot húz és
képernyőképet készít a növekedési gráfról, a közös készlet- és áramlásnézetről.

A futtató verziója `forestycoon-simulation/2026-10-16.5`. A nyitójelenet és
archivált referenciája frissült; a küldetések és saját pályaadatok megmaradtak.
A helyi projekt megszűnt alapértelmezett növekedési kötésének eltávolítása előtt
másolat készült az `artifacts/growth-project-backup` könyvtárban. Egyedi
növekedési viselkedésgráfot nem töröltünk automatikusan.

## Release teljesítménymérés

A `--tree-vitality-benchmark` a teljes havi erdőmunkát méri, felmelegített
gráffuttatóval, grafika nélkül, 4 ms célú gazdakerettel és hívásonként
16 determinisztikus munkaegységgel. .NET 8.0.32,
`DOTNET_TieredCompilation=0`. A végső mérést a tesztek és az editor füsttesztje
befejezése után külön futtattuk. A normál eset az újulatot is tartalmazza;
a másik eset minden induló fát elhalásra készít elő.

| Pálya | Eset | Induló fa | Munkaképkocka | CPU összesen | p95 | Maximum |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 64×64 | Növekedés | 2 000 | 6 | 22,633 ms | 4,613 ms | 4,613 ms |
| 64×64 | Tömeges elhalás | 2 000 | 4 | 17,015 ms | 7,158 ms | 7,158 ms |
| 256×256 | Növekedés | 31 638 | 49 | 193,567 ms | 4,022 ms | 4,052 ms |
| 256×256 | Tömeges elhalás | 31 638 | 36 | 167,067 ms | 15,273 ms | 15,806 ms |

A pálya létrehozása és a próbaállapot beállítása kívül esik a mérésen;
a mért munka memóriafoglalást és szemétgyűjtést is tartalmaz. Ezek kis minták,
nem garantált maximális képkockaidők. A tömeges elhalás kiugrásai további
motoroldali vizsgálatot indokolhatnak; az egész havi munkát egyben befejező
futási ág nincs.

Végső ellenőrzés: **1830/1830 teszt**, figyelmeztetés és fordítási hiba nélkül.
Az editor gráf- és önállóablak-füsttesztje átment. A pályagenerálás tesztje
háttérfordításra váráskor átadja a futást a dolgozó szálnak, és időkorláttal
ellenőriz: a sikere nem egy tetszőleges üres várakozási ciklusszámtól függ.
