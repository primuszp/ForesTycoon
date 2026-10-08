# Térbeli ökoszisztéma-szimuláció

2026-10-08. Ez a dokumentum a következő fejlesztések terve és a terepszerkesztési javítás leírása.
A lent jelölt jövőbeli modulok, új raszternézetek és konfigurációs fájlok még nincsenek megvalósítva.
A korábbi [víz–időjárás terv](environment-simulation-plan.md) történeti előzmény; az ott már elkészült
vízmérleget megtartjuk. A cél játékmodell, szakirodalmi folyamatokkal és későbbi kalibrációval.

## Megvalósított első rendszer-dinamikai alap

Az `Ecology/Dynamics` modulban működik az `EcologicalProcessRuntime`, az `IEcologicalProcess`
folyamat-interfész, valamint a `StockFlowNetwork` készlet–áramlás számítás. Nincs új külső függőség.
A játék meglévő `ForestEnvironmentCoordinator` osztálya már ezt a futtatót használja:
előkészítés → víz/időjárás → növényzet → havi környezeti integrálok lezárása.

- Az alaplépés továbbra is 0,5 játék-másodperc. A futtató megőrzi a maradék időt, a havi határnál
  felosztja a lépést, és csak az erdő frissítése után törli a havi víz- és sugárzási integrálokat.
- A folyamatok azonosítót, fázist, olvasott és írt mezőneveket deklarálnak. A regisztráció sorrendje
  fázison belül stabil. A konstruktor elutasítja a hiányzó bemenetet, az ismétlődő azonosítót és az
  azonos fázisban ugyanarra a mezőre író két folyamatot. A mezőnevek függőségi metaadatok;
  még nem típusos raszterregiszter és nem automatikus függőségi gráfrendezés.
- A `StockFlowNetwork` egyetlen deklarált mértékegységű készleteket kezel. A folyamat a lépés eleji
  készletből számolja a nemnegatív egység/másodperc áramlási igényeket; a hálózat együtt alkalmazza
  őket. A donorhiány és a fogadó szabad kapacitása arányosan korlátozza az áramlásokat. Az új befolyás
  és a kiáramlással felszabadított hely csak a következő lépésben használható. A visszautasított áramlást
  nem osztja újra más igényeknek. Külön hálózatok készletazonosítói nem keverhetők.
- Külső be- és kiáramlás külön mérlegbe kerül. A `Commit` minden állapot ellenőrzése után publikál;
  hibás lépés megszakítható állapotvesztés nélkül. A véges kapacitásnál fellépő néhány ulp kerekítést
  korlátozza, ennek maradéka is látszik a mérleg hibájában. A kölcsönzött `Stocks` span a következő
  sikeres commitig érvényes. A hálózat explicit Euler szemléletű; lépésfinomítással ellenőrizni kell
  minden új folyamat pontosságát és stabilitását.
- A meglévő vízmodell továbbra is az eredeti szekvenciális egyenleteket használja, már közös,
  korlátos `StockFlows` műveletekkel. A teljes vízmodell egyidejű hálózatra váltása megváltoztatná
  az eredményt és a régi mentések visszajátszását; ez külön modellverzióhoz kötött jövőbeli munka.
- A futtató és a már bemelegített, változatlan kapacitású hálózat lépésenként nem foglal memóriát.
  A hálózat kéréslistájának növekedése foglalhat; ez nem jelenti a teljes erdőmodell allokációmentességét.
  Egy folyamat kivétele után a futtató leáll, mert az addigi folyamatok már módosíthattak állapotot;
  újrapróbálás helyett érvényes világállapotot kell helyreállítani. Teljes lépés-tranzakció még nincs.

Ellenőrzés: `SystemDynamicsTests` – arányos készletelosztás, kapacitás, anyagmérleg, véletlen
szomszédsági áramlások, hibás commit, függőségi ellenőrzés, havi határ és memóriafoglalás.
A visszacsatolásos tározó teszt analitikus megoldáshoz méri a lépésfinomítás konvergenciáját.
Az önálló korábbi koordinátor-algoritmushoz hasonlítás havi határokon és terepbeavatkozás után
azonos fa- és vízállapotot követel meg. A fertőzés és a vadak ökológiai hatása továbbra is későbbi lépcső.

## Megvalósított talajkatalógus és olvasó raszternézet

Az `Ecology/Soil/profiles.json` a beágyazott, verziózott induló katalógus: homokos, vályogos,
agyagos és szerves/nedves talaj. Minden profilnak stabil szöveges azonosítója, neve, színe,
telítési készlete, szabadföldi vízkapacitása, hervadáspontja, beszivárgási és drénezési korlátja,
valamint termékenységi tényezője van. Ezek kezdeti játékparaméterek, nem kalibrált helyszíni
talajadatok. Még egy effektív gyökérzóna-profilt használunk; két függőleges talajhorizont nincs kész.
Új profilhoz katalógusbejegyzés kell, nincs fajta szerinti új `switch` ág. Az alap JSON módosítása
és új build az új világok induló profiljait változtatja; élő katalógusszerkesztő/importáló UI nincs.

Az induló `SoilLandscape` egyszer oszt profilt a csempékhez. Az 1-es generátor verzió seedből
származó, simított 12-csempés geológiai foltokat és a kezdeti terepnedvességet használja, a katalógus
preferenciái alapján. Ez játékbeli térkép-generálási heurisztika. Nincs render/RNG függősége.
A `SoilHabitat` adapter élőben továbbítja a terepkérdéseket, de a talajtípust a külön raszterből adja.
Az `EnvironmentSystem` már ennek vízparamétereit használja; a meglévő erdőnövekedés ugyanennek
a termékenységét olvassa. Terraform nem generál talajt és nem állítja vissza a vízkészleteket.

A `RasterGrid` rögzíti az oszloponkénti indexelést, a cellaméretet és a területet.
A `RasterField<T>` saját, csak olvasható tömböt és név/egység/tulajdonos/kategóriás metaadatot ad.
Most a talajtípus az első ilyen mező; a dinamikus víz tömbjei még az Environment tulajdonában vannak.
Általános dinamikus mezőregiszter és chunkonként publikált overlay-textúra még nincs.

**Játékosnézet:** Környezet (`E`) → „Talaj és víz térképe”. Kategóriás talajtérkép és rögzített
0–100%-os elérhető gyökérzónavíz-nézet választható. A vízskála a hervadáspont és szabadföldi
vízkapacitás közötti készletet mutatja; nem a teljes telítettséget és nem talajvízszintet.
Az egér pontos csempeadatot mutat, kattintással talajparaméterek vizsgálhatók. Legfeljebb 64×64
mintacella rajzolódik; nagy térképnél a szín középponti minta, nem kategóriaátlag. A jelmagyarázat
és a szimulációs idő látható. Az ImGui nézet olvasó, és nem módosítja a világot.

**Mentés:** a 7-es formátum a teljes kanonikus talajkatalógust, SHA-256 hashét és a generátor
verzióját rögzíti. Betöltéskor a mentett katalógust használja, nem a mai alapértelmezést.
Hiányos, hibás vagy ismeretlen modell a világcsere előtt elutasításra kerül. A 4/5/6-os világ
az eredeti `SoilProperties.Standard` profillal fut; 7-es újramentéskor ez a 0-s generátor és a
rögzített standard katalógus megmarad. Új térkép generálása már az új alapmodellt választja.
Az állandó talajtípus-raszter az eredeti térképből, a mentett katalógussal és generátorral regenerálható;
a magasságmódosításokat ezután játsszuk vissza. A dinamikus készletek továbbra is parancsnaplóból
állnak helyre. Ez még nem teljes ökológiai checkpoint.

Ellenőrzések: determinisztikus és seedfüggő raszter, méret/indexelés és tömbtulajdon,
profilfüggő víz/fanövekedés, terraform-állandóság, katalógus- és hashvalidálás, történeti talajmodell
megőrzése. `--soil-raster-smoke-test`: valódi UI és GL, két réteg, szünetben azonos kép,
változatlan szimuláció és talaj-visszajátszás; képek az `artifacts/soil-raster` könyvtárban.

## Döntés és tudományos minták

Térbeli hibrid modellt építünk: a csempékhez folytonos készletek és fluxusok, a fákhoz egyedi
állapot tartozik. A vadak mozgó egyedek, de táplálékfogyasztásuk és rágási nyomásuk raszterbe kerül.
A rendszer-dinamika készlet–áramlás és visszacsatolás szemlélete adja az anyagmérlegeket;
egyetlen globális differenciálegyenlet nem írja le jól a szomszédságot, az egyedi fákat és a beavatkozásokat.

| Minta | Mi használható belőle? | Döntés a játékban |
| --- | --- | --- |
| iLand | Egyedi fák, fényverseny, növekedés, elhalás, újulat, térbeli bolygatások; napi klíma és talajadatok. [Áttekintés, 2024](https://doi.org/10.1016/j.ecolmodel.2024.110785) | Ez a legközelebbi ökológiai minta; a folyamatokat egyszerűsítjük és külön kalibráljuk. |
| LANDIS-II | Egymással explicit core-interfészen kapcsolódó ökológiai bővítmények. [Hivatalos leírás](https://www.landis-ii.org/extensions) | A modulhatár és térképi folyamatok mintája; nem vesszük át a teljes kutatási futtatórendszert. |
| APSIM Next Generation | .NET modellek, óra által kibocsátott események, víz/növény/károsítás interfészek. [Model design](https://docs.apsim.info/docs/development/software/modeldesign), [interfaces](https://docs.apsim.info/docs/development/software/interfaces) | A külön folyamatok és a közös erőforrás-allokáció mintája. |
| Math.NET Numerics | RK2/RK4 skalár és vektoros ODE-megoldók. [API](https://numerics.mathdotnet.com/api/MathNet.Numerics.OdeSolvers/RungeKutta.htm) | Lehetséges offline referencia és kalibráció; nem szimulációs ütemező vagy raszteres ökoszisztéma. |

**Javaslat:** a meglévő .NET Engine/Ecology rétegen belül kis, explicit folyamatfuttatót építsünk,
a már működő víz–erdő koordinátor fokozatos általánosításával. Most nem adunk hozzá új külső függőséget.
A kész kutatási modellek közvetlen beágyazását külön prototípusban kellene vizsgálni: lépték, futásidő,
állapotmentés, interaktív beavatkozás és licencelés szerint. Az APSIM felhasználását külön licencfeltételek
szabályozzák; a forrás elérhetősége nem önmagában integrációs engedély. [Hivatalos regisztráció/licenc](https://registration.apsim.info/)

## Jelenlegi állapot és a terepszerkesztési hiba

Meglévő alapok: `TerrainMap` raszter és topológia; `ForestTreeStore` stabil egyedi faazonosítók;
`ForestCompetition` szomszédsági erőforrás-verseny; `EnvironmentSystem` korona/felszín/talaj/mélyvíz
készletek és ellenőrzött vízmérleg; `WeatherSystem` determinisztikus események;
`ForestEnvironmentCoordinator` vízlépés/havi erdőlépés összehangolása; `WildlifeSystem` éhség és
helyi táplálékkimerülés; parancsnapló-alapú mentés. Az új játékvilágot a `SoilHabitat` kapcsolja a
talajprofil-raszterhez; a közvetlen `TerrainMap` élő terepadatot ad, a régi világ egységes talajjal fut.
Regionális klíma-, fertőzés- és rágási visszacsatolási raszter még nincs.
A meglévő mélyvízkészlet nem kalibrált talajvízszint.

A javított hiba két forrása:

1. `RefreshHabitat()` szerkesztés után minden fa növekedési sebességét és erőforrásait frissítette,
   akkor is, ha az adott csempe változatlan maradt. Ez az egész erdő új értékelésének látszatát keltette.
2. A terep-geometriát a térkép globális `SurfaceVersion` értéke érvénytelenítette. A lokális művelet
   így minden látható terrain chunkot újraépített. Egy nem rajzolt régi rács-VBO is teljesen újratöltődött.

A most megvalósított működés:

- Az ecset a valóban módosított csomópontokhoz tartozó csempéket adja vissza, rendezett, egyedi listában.
  A lejtéskorlát miatti kaszkád ténylegesen megváltozott csempéi is beletartoznak.
- Az ottani élő fa, holtfa, tönk, depó és telepítési kijelölés elveszik; nem lesz belőle kitermelt készlet.
  A többi fa azonosítója, seedje, mérete, kora, egészsége és növekedési intervalluma változatlan marad.
- A beavatkozás nem lépteti az időjárást, vízkészletet vagy erdőévet. A szomszédok versenyválaszát
  a következő rendes havi lépés számítja ki, az előkészített havi adatok helyi javításával.
- A környezeti lefolyási célok csak a módosult csempéken és közvetlen szomszédaikon frissülnek.
- A terrain/grid cache chunkonkénti felületverziót használ. A nem használt globális rácsbuffer megszűnt.
  Az erdőcache továbbra is az érintett chunkot/kapcsolódó kontaktárnyék-határt frissíti.
- A hibás, nulla vagy tiltott szerkesztés nem indít ökológiai frissítést és terrain újraépítést.
- A GPU-próba az érintetlen erdőt szerkesztés után és kényszerített újraépítés után is összeveti,
  változatlan kamera/idő mellett, 1/255 csatornatoleranciával.

**Megmaradó korlát:** a térképi `Hydrology.Rebuild()` medence- és spill-számítása jelenleg teljes
rasztert vizsgál. Ez származtatott terep/vízgeometria, nem az `EnvironmentSystem` tárolt vízének
úrainicializálása, és nem teljes jelenetújraépítés. Medenceáttörésnek lehet távoli, valódi vízgeometriai
hatása; a megváltozott vízborítás saját chunkverziót kap. Ezt később vízgyűjtő-függőségek alapján
kell inkrementálissá tenni. Az összesített erdőstatisztika eltávolítás után jelenleg teljes tömböt összegez.

## Raszterek és állapottulajdonosok

A közös `RasterGrid` a csempeazonosítót, méteres cellaméretet, területet, szomszédságot és határfeltételt
írja le. A csempe ID-kiosztását nem változtatjuk meg; a mostani elrendezés oszloponkénti (`u * rows + v`).
A csomóponti domborzatból levezetett csempeközépmagasság nem azonos a csempe minimumával.
A következő mezők külön típusos tömbökbe kerülnek, közös geometriai indexeléssel:

| Réteg | Hiteles állapot és egység | Folyamat / játékosnézet |
| --- | --- | --- |
| Domborzat | Csomóponti magasság m; cellaközép m, lejtés és kitettség származtatott | Terraform; magasság, lejtés, vízgyűjtő és folyási irány |
| Talaj | Talajprofil ID; felső/alsó horizont vastagsága m, vízkapacitások mm, vezetőképesség mm/nap, termékenység | Inicializált profil + lassú alom/tápanyag/erózió hatás; típus és tulajdonságok |
| Víz | Korona, felszíni víz, gyökérzóna, mély tároló mm; később talajvíz hidraulikus szint m | Csapadék, beszivárgás, párolgás, gyökérfelvétel, lefolyás, mélyvízcsere |
| Klíma | Klímazóna ID, hőmérséklet °C, csapadék mm/nap, relatív pára, sugárzás, szél m/s | Időjárási esemény + magasság/kitettség/lombkorona módosítás; térképi mezők |
| Erdő | Egyedi fa ID/faj/pozíció/kor/átmérő/magasság/biomassza/egészség/stressztörténet | Fény- és gyökérverseny, növekedés, elhalás, magterjedés, megtelepedés |
| Erdőösszetétel | Fajonkénti biomassza vagy körlapösszeg részaránya, domináns faj és elegyesség, LAI | Egyedi fákból származtatott; fafajtérkép és ültetési döntés |
| Táplálék/vad | Ehető biomassza kg szárazanyag/m²; állatsűrűség egyed/ha; rágási fluxus | Növényzet termelése, legelés/rágás, mozgás, szaporodás és mortalitás |
| Károsítók | Kórokozó/kártevő nyomás fajonként, fertőzési stádium egyedenként | Fogékony gazda, kolonizáció, terjedés, károsítás, túlélés/gyógyulás |
| Tápanyag/holtanyag | Avarkészlet, holtfa és hozzáférhető tápanyag kg/m² | Elhalás → alom/holtfa → lebomlás → talaj → növekedés |

Kétféle talajhorizont két függőleges réteg; a térképi talajtípus és vízkészlet pedig külön raszteres
változó. Ezeket nem keverjük össze. A talaj típusa nem változik esőnél vagy újrarajzoláskor.
Az erdő csempénkénti aggregátuma nem zárhatja ki a több fafajú elegyet. A fajarány-nézet megadja,
hogy törzsszámot, biomasszát vagy körlapösszeget mutat; ezekből nem következik ugyanaz a domináns faj.

Egy mezőnek egy hiteles állapottulajdonosa van. Több modul kérhet ugyanabból a vízből/táplálékból,
de a közös készletelosztó dönti el a megengedett felvételt. A raszternézet kizárólag olvasó; nem
generál új élővilágot, és a hiányzó érték külön maszk, nem a nullával azonos.

## Kölcsönhatások és kialakuló viselkedés

```mermaid
flowchart LR
    P[Játékos beavatkozása] --> T[Domborzat / ültetés / kitermelés]
    T --> W[Vízmozgás és készlet]
    T --> F[Egyedi fák és újulat]
    C[Klíma és időjárás] --> W
    C --> F
    S[Talajprofil és tápanyag] --> W
    S --> F
    W --> F
    F -->|intercepció, felvétel, árnyék| W
    F -->|alom, holtfa| S
    F --> Q[Táplálék és menedék]
    Q --> A[Vadállomány]
    A -->|rágás, taposás| F
    C --> D[Károsítónyomás]
    F -->|gazda és stressz| D
    D -->|károsítás és elhalás| F
```

Példa: kitermelés csökkenti a lombkoronát és a vízfelvételt; a talaj az időjárás/talajprofiltól függően
nedvesebb lehet, miközben a nyílt felszín párolgása nő. A több fény segíti az újulatot, amely odavonzza
a vadat; a rágás visszafoghatja az új erdő kialakulását. Az eredményt nem egy „kivágás után X lesz” ág
írja elő, hanem a külön folyamatok mérlege és válasza. A visszacsatolás előjele termőhelyfüggő.

Károsítónál az alacsony egészség csak fogékonysági tényező: önmagában nem hoz létre fertőzést.
Kell kompatibilis gazdafaj, jelenlévő fertőző forrás/nyomás, időjárási és fejlődési feltétel.
Az iLand szúmodulja külön kezeli a térbeli terjedést, klímahatást és stresszfüggő kolonizációt.
[Moduldokumentáció](https://iland-model.org/barkbeetle-module)

A játékban gazdafajhoz kötött paraméterezett hazard legyen:
`p(fertőzés, dt) = 1 - exp(-lambda(gazda, nyomás, stressz, klíma) * dt)`.
A nyomás az előző lépés fertőzött állapotából, távolsági kernelből és szélből készül; az újonnan
fertőzött fa ugyanazon lépésben nem terjeszt újra. A feldolgozás iránya így nem gyorsítja fel a járványt.
A kórokozó és rovarkárosító külön modell, saját lappangással, fejlődéssel és túlélési ciklussal.
Az iLand [rágási modellje](https://iland-model.org/browsing) a csemeték sérülékenységéhez ad mintát;
a játékbeli táplálékfogyasztás és egyedi állatmozgás kapcsolását külön tervezzük és teszteljük.

## Folyamatfuttató és numerika

A cél `EcologicalProcessRuntime` az Ecology rétegben, render- és UI-függőség nélkül. Az Engine
általános idő- és tömbes műveleteket adhat; nem tudhat erdőről vagy betegségekről.
Először a meglévő `EnvironmentSystem` és havi erdőlépés adaptere kerül bele, változatlan eredménnyel.

| Tervezett elem | Feladat |
| --- | --- |
| `EcologyModelDefinition` | Modellverzió, időkonverziók, faj-, talaj-, klíma- és károsítóprofilok |
| `RasterField<T>` / `FieldDescriptor` | Tömb, geometria, mértékegység, értéktartomány, tulajdonos, chunkverzió |
| `IEcologicalProcess` / `ProcessDescriptor` | Inicializálás, léptetés, olvasott/írt mezők, ütem, szomszédsági sugár |
| `FluxAccumulator` / készletelosztó | Készletigények, határfluxusok, egyszeri commit és mérlegellenőrzés |
| `EcologicalClock` | Egész tickszámú ütemezés, havi/eseményhatáron osztott lépés és maradékidő |
| `TerrainChangeSet` | Módosult csomópontok/csempék, valódi magasságváltozás, érintett vízgyűjtő |
| `EcologySnapshot` | Publikált állapot és diagnosztika a megjelenítéshez/mentéshez |

A folyamatok induláskor kapják a típusos mezőreferenciákat; a cellahurokban nincs stringkeresés,
reflexió, kifejezés-parse vagy per-cell objektum. A descriptor ellenőrzi a hiányzó függőséget és
írási konfliktust. A modulok sorrendje explicit fázisokban rögzített. A körkörös ökológiai hatást
előző lépésből olvasott állapot vagy dokumentált iteráció oldja fel; nem önkényes topológiai sorrend.

Egy rendes ökológiai lépés:

1. Az időhatárra naplózott beavatkozások alkalmazása; dirty-halmazok és lokális előkészítés javítása.
2. Klíma/időjárási forcing előállítása az adott intervallumra.
3. Előző publikált állapotból intercepció, párolgási/felvételi igények és szomszédfluxusok számítása.
4. Közös készletkorlátozás, víz/táplálék/tápanyag fluxusok commitja és intervallum-integrálok gyűjtése.
5. Esedékes rágás, fertőzési/fejlődési lépés, majd a havi határon növekedés/egészség/elhalás/újulat.
6. Származtatott raszterek, statisztika, chunkverziók és olvasói snapshot publikálása.

A pontos fázis- és ütemválasztást numerikus összehasonlító próbák rögzítik. A jelenlegi 30 Hz-es
játékóra, 0,5 szimulációs másodperces környezetlépés és havi erdőhatár jó induló adapter, nem indok
arra, hogy minden ökológiai mezőt minden renderframe-ben végigjárjunk. A gyorsítás több ugyanilyen
lépést futtat; nem módosítja a fluxuskoefficienseket. Minden fizikai időkonverzió mentett paraméter.

Víznél cellaél-fluxusokat egyszer számítunk és két cellára ellentétes előjellel könyvelünk.
Egy donor összes kiáramlása nem haladhatja meg a rendelkezésre álló készletét. Eltérő cellaterület
esetén térfogatban könyvelünk: `térfogat_m3 = készlet_mm / 1000 * terület_m2`.
A hidraulikus hajtóerőhöz méteres magasság/vízszint kell; a mai normalizált minimumszint egyszerűsített
lefolyási célja nem teljes áramlási modell. Lateralitásnál dokumentált határfeltétel, stabilitási
korlát és szükség esetén determinisztikus részlépés kell. A clamp nem helyettesítheti az anyagmérleget.

A mintakezelés per-rendszer/per-esemény determinisztikus, egyed/csempe ID-val és időlépés-indexszel.
Újrarajzolás és LOD-váltás nem fogyaszt szimulációs véletlenszámot. Párhuzamosítás csak független
chunkok/részfluxusok mentén, rögzített redukciós sorrenddel történhet. A bitazonos többplatformos
eredmény külön követelmény és mérés; nem következik automatikusan a seedből.

## Adatvezérelt paraméterezés

A folyamatok algoritmusa típusos C#; a fajfüggő érték, stresszküszöb, regenerációs esély, vízprofil,
fertőzési és túlélési görbe verziózott JSON-katalógusba kerül. Induláskor tartományt, egységet,
összefüggő küszöböket, referenciákat és növekvő görbepontokat ellenőrzünk, majd tömbökre fordítunk.
Új faj vagy talajprofil nem új `switch` ág. Ugyanaz a katalógus szolgálja a számítást és magyarázó UI-t.

Nem kell a forráskódba égetni például a ma használt többéves stresszhalál-küszöböt. A modell
szemantikája viszont maradjon olvasható: egy korlátlan, cellánként futó szkriptmotor rontaná az
ellenőrizhetőséget és futásidőt. Elsőként paraméterek és darabonként lineáris válaszgörbék kellenek.

## Térképi nézetek és játékosdöntés

Első nézetek: talajtípus; gyökérzónás elérhető víz és belvíz; domináns fafaj/elegyesség;
fény; egészség/stressz. Következő nézetek: klíma, folyási irány, táplálék/vadnyomás, fertőzőnyomás.
A jövőbeli `RasterOverlayRenderer` a publikált mezőt festi, chunkonként feltöltve; alapból 2D
felülnézetben, választható áttetsző terepfedéssel. Nézetváltás nem új szimulációs lépés.

Kell rögzített, egységes jelmagyarázat, mértékegység, szimulációs időbélyeg, kategóriás faj/talajszín,
és kurzor alatti cellaadat. Ne az aktuális térképi min/max alapján változzon állandóan a színskála.
Ültetéskor a javaslat összetevői láthatók: vízhiány, elöntés, fény, termékenység, klíma és rágási
kockázat. Ez feltételes alkalmasság, nem garantált végeredmény vagy rejtett betegség-orákulum.

## Mentés, skálázás és elfogadás

A mostani terepedit szemantika a 6-os mentési verzióhoz tartozik. A 4/5-ös parancsnapló történeti
terrain parancsai a régi szabállyal futnak vissza, és 6-os mentésbe történeti flaggel kerülnek.
Betöltött régi világban az új szerkesztések már a javított szabályt használják. A naptár kompatibilitása
megmarad. Ezt serializer- és natív világmentés/visszajátszás-próbák ellenőrzik.

Az új modulok előtt checkpoint + naplórészlet kell: fa/állat ID-k, összes készlet, fertőzésállapot,
RNG állapot/kulcs, órák és részlépés-maradék, területbeállítások, modell- és katalógushash.
Betöltés validálása külön példányon történik, majd egyszeri publikálással. A rendercache nem mentett
hiteles állapot. A katalógus megváltozásával régi napló nem játszható le csendben új szabályokkal.

A raster tömbös adatelrendezés; több mező/második buffer szükségességét memória alapján választjuk.
512² cellán egy double mező 2 MiB, húsz mező egy példánya 40 MiB; dupla buffer 80 MiB, a fák és
indexek ezen felül vannak. A faállapot továbbra is ritka per-csempe tömb, nem új objektum minden tickben.
Regionális klíma és aggregátum eltérő felbontását csak explicit leképezéssel/adatmegőrzéssel vezetjük be.

Elfogadási próbák: nulla idő/pausa nem módosít állapotot; azonos parancsnapló ugyanazt a világot adja;
lépésfelosztás numerikusan konvergens; víz/biomassza mérleg zár; készlet nem negatív;
cellafeldolgozási sorrend nem gyorsítja a fertőzést; újrarajzolás/LOD/nagyítás nem módosít állapotot;
egy helyi edit távoli fát nem változtat meg; a prepared és szinkron havi út azonos;
szárazság/elöntés/árnyék/rágás kontrollált forgatókönyve a várt irányba hat.
64²/256²/512² raszteren külön inicializálási, napi/havi határ-, memória- és edit-költséget mérünk.
Pontos ms-cél csak rögzített hardverprofil és reprezentatív fasűrűség után kerül a tervbe.

## Megvalósítási sorrend

1. **Elkészült:** helyi terraform-növényzet törlés; változatlan érintetlen állapot; helyi routing és
   terrain/grid cache; kihasználatlan teljes rácsbuffer eltávolítása; replay-kompatibilitás és regressziók.
2. **Elkészült első lépcső:** JSON talajprofil-katalógus és talajtípus-raszter, közös olvasói
   mezőmetaadatok, talaj/víz felülnézet; mentett modell és történeti kompatibilitás.
   Két talajhorizont és dinamikus/chunkos raszterpublikálás később.
3. **Részben kész:** típusos folyamatfuttató a meglévő víz/erdő adapterével, változatlan
   referenciakimenet. Teljes dinamikus checkpoint + naplórészlet még hátravan;
   ez következik az új dinamikus klíma, fertőzés és rágási modulok előtt.
4. Regionális klíma és konzervatív lokális vízfluxus; inkrementális vízgyűjtő/topológia frissítés.
5. Fajkatalógus, fény/tápanyag/egészség folyamatok, vegyes fajaggregátumok és alkalmassági UI.
6. Károsító/gazda modellek és valós rágási visszacsatolás; térképi magyarázat és emergens próbák.

Minden szakasz egy működő folyamatot és saját invariánsait adja. Egy új ütemező bevezetése önmagában
nem jelent összetettebb ökológiát; azt a dokumentált és ellenőrzött kölcsönhatások adják.

## A jelenlegi javítás ellenőrzése

1352/1352 Release-egységteszt sikeres. Új lefedettség: tényleges edit-terület és lejtési kaszkád,
érintetlen faállapot és vízkészletek, telepítés/tönk törlés, atomi hibás kérés, nulla beavatkozás,
helyi/teljes routing azonossága, lokális chunkverzió és prepared/szinkron havi eredmény.
A forest, tree-growth, graphics/weather és teljes játékablak OpenGL-próba sikeres.
A forest próba külön ellenőrzi a távoli erdő képi állandóságát szerkesztés és kényszerített
újraépítés után. A tree-growth próba a 6-os terepedit replayt, 5→6 naplómigrációt és a régi
világban végzett új szerkesztések szabályát is ellenőrzi. A korábbi `TreePreviewDump` nullable
figyelmeztetései megmaradtak. A részletes jövőbeli ökológiai modell még nem teljesült ettől a javítástól.
