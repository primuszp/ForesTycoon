# Faegyedek növekedése, öregedése és grafikai modellje

Állapot: kutatásra és a jelenlegi kódra épülő megvalósítási terv, 2026-10-03. A dokumentum nem jelent kész implementációt. A fajok most a lucfenyőt (*Picea abies*), közönséges nyírt (*Betula pendula*), kocsányos tölgyet (*Quercus robur*) és bükköt (*Fagus sylvatica*) közelítik. A korábbi általános tölgy/nyír típusok e fajválasztása az új modell explicit döntése.

## 1. Mit támasztanak alá a források?

A fa magassága, törzsátmérője és koronája eltérő ütemben változik. Az Evans és munkatársai által közölt adatok az átmérő, magasság, koronaméret és fényviszonyok kapcsolatát külön vizsgálják. Ebből a játékhoz külön növekedési változókat vezetünk le; a tanulmány nem szolgáltat automatikusan kész paramétereket mind a négy játékfajra. [Evans et al., 2015](https://arxiv.org/abs/1502.05827).

A növedék szezonális és vízellátásfüggő. A WSL svájci vizsgálatában a mintázott fák tényleges fatest-növekedése kevés napra koncentrálódott, főként tavasszal és kora nyáron. Játékbeli következtetés: az év egészében legyen követhető fejlődés, de a tényleges növedék kapjon évszakos súlyt; a téli méretváltozás lehet nulla. [WSL, 2021](https://www.wsl.ch/en/news/a-few-days-determine-the-growth-of-temperate-trees/).

Az idős vagy nagy fa nem szükségszerűen áll le a növekedéssel. A Stephenson et al. tanulmány a nagy fák jelentős abszolút tömeggyarapodását mutatja; ez nem jelent korlátlan magasságot vagy korlátlan éves sugárnövedéket. A játékban az érettség elérése nem fagyaszthatja be az átmérőt. [Stephenson et al., 2014](https://www.nature.com/articles/nature12914).

A pusztulásnak több oka lehet: versengés, szárazság, túlnedvesség, sérülések és más zavarások. A holtfa a helyszínen maradhat és lebomlik. [Forest Research – Forest Carbon Cycle](https://www.forestresearch.gov.uk/climate-change/carbon/forest-carbon-cycle/).

Az álló holtfa kidőlését a bomlás, fafaj, helyi körülmények és mechanikai terhelés befolyásolja. Az észak-amerikai eredményeket nem kezeljük magyarországi fajokra hitelesített kidőlési időként. A közvetlenebb európai háttérhez a fennoskandináv vizsgálatot is használjuk; a tényleges játékkonstansokat külön kalibráljuk. [Oberle et al., 2018](https://journals.plos.org/plosone/article?id=10.1371/journal.pone.0196712), [Aakala et al., 2024](https://doi.org/10.1111/1365-2664.14729).

Faji különbségek a tervezéshez:

| Faj | Kutatási támpont | Játékbeli következmény |
|---|---|---|
| Nyír | Gyors kezdeti növekedés, fényigény, viszonylag rövid élet; a Forest Research kb. 70 évet említ | Korai gyors növekedés, korábban megjelenő öregedési kockázat |
| Luc | A JRC 200–300 éves élettartamot ismertet | Hosszabb fejlődési pálya, elkülönített víz- és szélérzékenység |
| Bükk | A JRC tipikusan 150–300 éves életet és későbbi korban is jelentős növekedést ír le | Idősen is vastagodó törzs, fényversengéshez alkalmazkodó korona |
| Tölgy | A JRC szerint az élettartam meghaladhatja az 500 évet | Hosszú életű, vastag törzsű veteránok is maradhatnak |

Források: [nyír](https://www.forestresearch.gov.uk/tools-and-resources/tree-species-database/131561-silver-birch-sbi-2/), [luc](https://forest.jrc.ec.europa.eu/en/european-atlas/qr-trees/norway-spruce/), [bükk](https://forest.jrc.ec.europa.eu/media/atlas/Fagus_sylvatica.pdf), [tölgy](https://forest.jrc.ec.europa.eu/en/european-atlas/qr-trees/pedunculate-oak/).

Ezek tájékozódási pontok, nem kötelező halálozási határok. Az erdészeti vágáskor, a magtermő kor és a biológiai öregedés három külön fogalom.

## 2. A jelenlegi rendszer akadályai

- `ForestStand` csempénként közös fajt, kort, biomasszát és egészséget tárol. A látható törzseknek nincs önálló, tartós szimulációs állapotuk.
- `Terrain.BuildStems` a csempe érettségéből és biomasszájából állítja elő a darabszámot és méretet. A méretosztályok a töltöttséggel változhatnak; egy fa nem őrzi meg saját növekedési történetét.
- `TreeVisualScale = 0.28 + 0.72 × Maturity`, és a `Maturity` legfeljebb 1. Az érett kor után a grafikai méret megáll.
- `ForestSystem.GrowOrDie` havi szinten egész állományt törölhet. Nincs száradási időszak, álló holtfa vagy kidőlt törzs.
- A `ForestVisualState` 1/32-es lépcsőkben követi a változást. A jelenlegi cache önmagában nem képes folytonos egyedi növekedésre.
- A kitermelés biomasszát csökkent, nem konkrét faegyedeket távolít el. A tönk egy egész korábbi állomány látványából rekonstruálódik.

A célmodellben az egyedek az elsődleges állapotok, az állományadatok ezek összesítései. Nem tarthatunk fenn két, egymástól független növekedési igazságot.

## 3. Faegyed és állomány adatszerződése

A `ForestTreeStore` tömbös, chunkonként indexelt tároló. Nem készül külön objektum és külön GPU-buffer minden fához. Egy látható fa egy szimulált játékbeli egyed; a darabszám nem valós hektáronkénti törzssűrűség.

| Adat | Szerep |
|---|---|
| `TreeId`, `TileId`, `Species` | Stabil azonosító, hely és faj |
| `BirthTime`, `LocalU`, `LocalV` | Pontos kor és állandó terepi hely |
| `VariantSeed`, `Yaw` | Tartós sziluett, ágkiosztás és elfordulás |
| `Diameter`, `Height`, `CrownRadius`, `CrownLength` | Egymástól elkülönített méretek |
| `GrowthAnchorTime`, méretnövekedési ráták | Folytonos, visszajátszható méretértékelés |
| `Vitality`, `StressMemory`, `CrownDamage` | Életerő, tartós stressz, visszaszáradás |
| `Lifecycle`, `StateStartTime` | Életciklus és az átmenet időpontja |
| `Decay`, `FallDirection`, `FallStartTick` | Bomlás és determinisztikus kidőlés |

A törzsátmérő referenciahelye a DBH, azaz 1,3 méteres magasságban mért átmérő. Ennél alacsonyabb csemetéknél külön törzsalapi átmérőt használunk, és a DBH csak később válik értelmezhetővé. A méretek belső egysége méter; a dioráma-megjelenítés külön, dokumentált leképezést kap. A járműhöz hangolt grafikai skála nem módosíthatja a faanyag mennyiségét.

A `TreeId` sosem a tömb aktuális indexe. A felszabadított helyek újrafelhasználhatók, de az azonosító nem; a kezdeti példányok és az újulat létrehozási sorrendje determinisztikus. Halál, kitermelés vagy újratelepítés nem rendezi át a túlélők helyét, méretét és seedjét.

A csempénkénti kezdő célérték a jelenlegi 4–14 látható fa fajonként, legfeljebb 16 élő példány az első profilban. Ez konfigurálható játékbeli korlát, nem ökológiai állítás. A holtfa és a tönk külön, ritkán foglalt tárolóban marad, így az újulat mellett is megőrizhető.

## 4. Folytonos növekedés és éves növedék

Az erdőév a környezeti modellben jelenleg 1200 játék-másodperc. Ezt megőrizzük. A teljes szimuláció közös időgyorsítása és szünete minden növekedésre, állapotátmenetre és kidőlési animációra érvényes.

A növekedés faj-, méret-, fény-, termőhely- és vízfüggő. Kiinduló játékmodell:

```text
átmérőráta = fajAlapráta × méretfüggvény × termőhely × fény × víz × életerő × szezon
magasságráta = fajMagasságiRáta × telítődésiFüggvény × termőhely × fény × víz × életerő × szezon
```

A függvények paraméterezett játékközelítések, nem a hivatkozott tanulmányokból átvett kész egyenletek. A magassági telítődés hamarabb erősödik, mint a vastagsági. Az érettségi kor nem nullázza le a növekedést. Jó körülmények között évente pozitív növedék keletkezik; súlyos stresszben megállhat. A korona sérüléskor csökkenhet, de ettől a meglevő fatest nem zsugorodik vissza.

Két frissítési szint:

1. **Havi ökológiai lépés:** a lezárt hónap környezeti összesítése, szomszédos koronák fényversengése, életerő, következő időszak növekedési rátái és állapotátmeneti kockázatai. Minden egyed ugyanabból a lezárt pillanatképből számol, az új állapotokat második fázisban alkalmazzuk.
2. **Folytonos méretértékelés:** a havi határon rögzített méret és ráta alapján `méret(t) = horgonyméret + ráta × eltelt erdőév`. A szimulációs lekérdezés és a renderer ugyanazt a tiszta függvényt használja. Havi határon és minden faállapotot módosító esemény előtt az addigi növedéket elszámoljuk, majd új horgonyt állítunk.

Ez nem jövőbeli növekedés előre megjelenítése: a már eltelt időt integráljuk a korábban meghatározott rátával. A havi határnál a méret folytonos marad. Egy súlyos káresemény a saját időpontjában új rátát állít, így nem vár a következő hónapra. Az életerő normál környezeti változásainak havi reakciója az első verzió tudatos egyszerűsítése.

Az éves statisztika külön rögzíti a bruttó növedéket, a kitermelést, az élő készletből holtfába átkerülést és a bomlási veszteséget. A faanyag térfogata a törzs geometriájából és fafaji alakszámból származik; a tömeghez külön fasűrűség tartozik. A lomb és gyökér biomasszáját nem számítjuk automatikusan szállítható rönknek.

## 5. Életciklus és öregedés

```text
Csemete → Növekvő → Kifejlett → Öregedő
                                  ↓
                        Koronavisszaszáradás
                                  ↓
                             Álló holtfa
                                  ↓
                              Kidőlés
                                  ↓
                            Fekvő holtfa
                                  ↓
                           Bomlás → Eltűnés
```

Az első négy állapot fejlődési kategória. A koronavisszaszáradás stresszes fiatal fán is megjelenhet, és részben visszafordulhat. Egy egészséges öreg fa sokáig életben maradhat. A halál végleges: álló holtfából nem lesz újra élő fa. A lombhullató téli lombvesztése külön szezonális állapot, nem betegség.

Az első hangolási profilban az öregedési kockázat kezdősávjai: nyír 60–90, luc 160–220, bükk 150–230, tölgy 250–400 erdőév. **Ezek javasolt játékpróbaértékek, nem szakirodalmi élettartamhatárok.** Egyedenként tartósan sorsolt eltérés és termőhelyi módosító akadályozza meg az azonos korú erdő egyszerre történő elhalását. A veterán tölgy az 500 évet is túllépheti.

Az éves halálozási intenzitás több komponensből áll:

```text
lambda = alapKockázat + öregedés + tartósVízstressz + fényhiány + sérülés
p(dt) = 1 - exp(-lambda × dt_erdőév)
```

A lépésre átváltás megőrzi a kockázat helyes időskáláját. A véletlen csatornákat világseed, `TreeId`, ökológiai lépés és eseménytípus azonosítja; a FPS vagy a láthatóság nem befolyásolja az eredményt. Az időbeli eloszlás és a halálozási paraméterek játékbeli kalibrációt igényelnek.

A tartós stressz koronaritkulást, csúcsszáradást, ágvesztést és csökkenő vitalitást okoz. Az idősödés emeli a visszaszáradás és halál kockázatát; nem kényszerít minden fát ugyanazon életkorban kiszáradásra. A természetes öregedési út általában többéves vizuális leépülést mutat. Vihar és más későbbi akut sérülés ezt megkerülheti.

## 6. Álló és kidőlt holtfa

Az elhalt fa őrizze meg eredeti átmérőjét, magasságát, ágszerkezetét és helyét. A lomb/tű megtartási ideje faji paraméter; később a kéreg és a vékony ágak részben leválnak. A fában levő anyag először a holtfa készletbe kerül, nem tűnik el.

A bomlás első közelítése `maradó faanyag = kezdeti faanyag × exp(-k × eltelt év)`. A `k` a faj, hőmérséklet, nedvesség és talajkontaktus függvénye. A nedvességhatás nem korlátlanul növekvő: nagyon száraz és oxigénhiányos, tartósan telített környezetben is lassulhat. A fasűrűség és a bomlásállóság külön paraméter, nem felcserélhető.

Első látványteszt-sáv: az álló holtfa gyakran 3–15 erdőévig maradjon, a fekvő holtfa több évtizedes pályán bomoljon. Ezek hangolási célok, nem minden fajra érvényes természeti adatok. A melegebb hely, faanyag-gyengülés és szélterhelés módosítja a kidőlési kockázatot; nem lesz minden törzsnek azonos lejárati ideje.

A kidőlés szimulációs esemény: időpont, irány és töréspont kerül az állapotba. A renderer például 2–4 játék-másodperc alatt mutatja a mozgást; ez játékbeli animációs idő, nem erdőév és nem valós fizikai mérés. Szünetben áll, gyorsításkor a közös játékidőt követi. Első változatban kinematikus animáció készül, láncreakció és teljes merevtestfizika nélkül.

A törzs végső helyzete CPU-oldali terepmintavételezésből származik, és menthető. A törzsszakaszok követik a lejtőt; a maradvány térbeli kiterjedése több chunkhoz tartozhat, ezért mindegyik érintett chunk láthatóságát és árnyékát figyelembe kell venni. A természetes törés szakadt, sötétebb felületet hagy; a kitermelési tönk világos, fűrészelt vágási lapot. A két esemény külön modellváltozatot kap.

Első játékverzióban a kidőlt fa nem blokkol utat és nem okoz járműkárt. A későbbi akadálykezelés önálló gameplay-fejlesztés. A bomlás végén vizuális halványulás zárja az életciklust; a faanyag eltávolítása könyvelt veszteség.

## 7. Jobb egyedi famodellek

Minden fa külön törzset, fajra jellemző vázágakat és több lombtömeget kapjon. A korona ne kizárólag egy nagy, felületmintázott test legyen. A közeli modellben látható ágak a lomb belsejébe csatlakozzanak; a távoli egyszerűsítés ugyanazt a sziluettet őrizze.

| Faj | Fiatal modell | Kifejlett/öreg modell | Száradó/holt modell |
|---|---|---|---|
| Luc | Karcsú vezérhajtás, elkülönülő ágemeletek | Szabálytalan emeletek, szélesedő törzsalap, ritkuló alsó ágak | Foltos tűvesztés, látható ágörvök, kopasz csúcs |
| Nyír | Vékony világos törzs, könnyű keskeny korona | Finom elágazás, enyhén lehajló gallyak, áttört lomb | Részleges koronavesztés, megmaradó világos kéreg |
| Tölgy | Karcsúbb fiatal forma | Vastag, görbülő főágak, több széles koronatömeg, veterán forma | Egyes vázágak kiszáradnak, tagolt kopasz ágkorona |
| Bükk | Keskenyebb, felfelé törekvő korona | Sima szürke törzs, magas tömör korona, eltérő fényoldali terjeszkedés | Csúcs- és oldalági visszaszáradás, nyitottabb korona |

A változatok rögzített seedből készülnek. A forma idővel folytonosan változik; ugyanaz a fa nem sorsol minden évben új ágelrendezést. Törzs és lomb külön növekedési transzformációt kap. A gyökérnyak a terepen rögzített marad, a törzs vastagodása nem nyomja fel a fát.

A száraz ágak és lombvesztés alapja a `CrownDamage`; az évszakos lombfedettség külön szorzó. A száradást koronán belüli, tartósan kijelölt ág/tömegcsoportok mutatják, nem az egész fa egyszerre történő barnítása. Tavasszal a lomb visszatérhet az élő ágakra, a halott ág nem zöldül ki.

A `ForestTrunkProfile` elvét megtartjuk, de DBH-alapú teljes törzsprofillá bővítjük. Kitermeléskor az aktuális egyedi törzs mérete és vágási magassága határozza meg a tönköt. A tönk nem a csempe átlagából rekonstruálódik.

## 8. Renderelés és teljesítmény

A havi frissítés nem építheti újra képkockánként az egész erdőt. A renderer fajonként és alakcsaládonként újrahasználható faelemeket, chunkonként csoportosított példányadatokat használjon. OpenGL 3.3 alatt az instancing megvalósítható; a projektben új instance-buffer és shaderút szükséges hozzá.

Az instance-adat a mérethorgonyokat, növekedési rátákat, színt, seedet és állapotot hordozza. A shader chunkhoz viszonyított idővel értékeli a folytonos méretet, így a hosszú játékidő float-pontossági hibája elkerülhető. Ugyanaz az értékelés szükséges az árnyékpassban, CPU-oldali kijelölésnél és bounds-számításnál. A GPU sosem dönti el a halált vagy a faanyag mennyiségét.

Near: vázágak, elkülönülő koronatömegek, tönkrészletek. Medium: kevesebb ág és tömeg. Far: olcsó faji sziluett; a halott fa ettől még a szimulációban létezik. Külön LOD-sáv és hiszterézis szükséges; a jelenlegi minden zoomnál Near útvonal nem elég ehhez.

A korona topológiája csak új ág/tömegcsoport, állapotváltás vagy LOD-csere esetén változik. A puszta növekedés példányadatból készül. Új részletek megjelenése determinisztikus méretküszöbhöz és átmenethez kötött, hogy ne ugráljon a sziluett.

512×512 **csempén**, 16 egyed/csempe felső korláttal kb. 4,19 millió egyed lehet. 64 bájtos nyers egyedállapot önmagában kb. 256 MiB; az indexek, holtfa és GPU-adatok ezt növelik. Ezért foglalt chunkokra allokáló tömbös tároló, aktív listák és újulat-korlátok kellenek. A tényleges struktúraméretet és térképméretet mérni kell. Távoli egyedek kihagyása a szimulációból nem megengedett optimalizálás.

Mérendő: havi ökológiai lépés p95/max ideje, növekedési lekérdezés költsége, draw-callok, instance-upload bájt/hó, VRAM/RAM, sok egyidejű kidőlés, térképszél és chunkhatár. A hosszú havi munkát determinisztikus sorrendben, több frame-en át lehet előkészíteni, de egy ökológiai lépés eredményét következetes határon kell publikálni; a render-budget nem változtathatja meg a világállapotot.

## 9. Illesztés a játékhoz, kitermelés és mentés

| Modul | Feladat |
|---|---|
| `ForestTreeStore` | Stabil egyedek, chunk/tile indexek és ritka maradványtároló |
| `ForestGrowthModel` | Tiszta növekedési függvények és ráták |
| `ForestLifecycleModel` | Vitalitás, állapotátmenetek, halál és bomlás |
| `ForestSpeciesParameters` | Verziózott faj- és játékprofil |
| `ForestSystem` | Havi koordináció, ültetés, újulat, kitermelés, aggregáció |
| `ForestTreeRenderer` | Modellek, példányadatok, LOD, láthatóság és árnyék |
| `ForestFallVisual` | A szimuláció által meghatározott kidőlés megjelenítése |

A `ForestStand` összesített lekérdezési nézet lesz. Vegyes fajoknál külön fajonkénti részösszesítések és domináns faj jelenjen meg; az átlagkor mellett korcsoport-eloszlás is kell. A környezeti modell az élő lombfelület és vízigény aggregátumát használja. Holtfa nem vesz fel vizet élő faként; bomlása nem módosítja automatikusan a jelenlegi talajvízmodellt.

A kitermelés stabil sorrendben kiválasztott faegyedeket vág ki. Egy teherautó részrakománya nem csökkentheti az összes élő fa átmérőjét. A teljes kivágott törzs kitermelési depóba/rönkkészletbe kerül, a teherautó abból vesz át részleteket. A jelenlegi `ExtractTimber` adaptere ezt a készletet kezeli. A maradó fák nem méreteződnek át a kitermelés miatt; a felszabadult fény a későbbi növekedésüket módosítja.

Az újulat külön fiatal egyedeket hoz létre a ténylegesen felszabadult helyeken, faji magforrás és fényviszony alapján. Meglévő, idős fák között is lehet fiatal fa. A holtfa eltűnése nem feltétele a csempe újratelepítésének, de az elfoglalt törzsalapok helyét figyelembe kell venni.

Nincsenek megőrzendő korábbi mentések. Egyetlen egyedi famodell és aktuális mentésséma szükséges; régi állománymodell, migráció és modellenkénti verziókapcsolók nélkül. Azonos build alatt a seed és a parancsnapló visszajátszásának ugyanazokat az egyedeket és készleteket kell előállítania.

Az életciklus későbbi mentési snapshotja őrizze az egyedeket, növekedési horgonyokat/rátákat, maradványokat, következő azonosítókat, paraméterprofilt és félbeszakadt kidőlés állapotát. Ellenőrzött snapshot + az azt követő ticknapló szükséges; az aktuális formátum egyetlen modellhez tartozik. A pontos bitazonos reprodukció első célja azonos build/runtime; eltérő platformon numerikus toleranciával ellenőrzött állapot-egyezést vállalunk, amíg külön determinisztikus matematikai réteg nem készül.

Anyagmérleg: `korábbi készlet + növedék = élő készlet + álló/fekvő holtfa + kitermelt, még nem szállított fa + szállítás/üzemi készlet + összes könyvelt veszteség`. A halál belső átadás, nem automatikus veszteség. A rönkkészlet nem számolható egyszerre holtfaként és kitermelt faként.

## 10. Megvalósítási sorrend és elfogadás

1. **Egyedi állapot és mentés:** tároló, azonosítók, kezdeti elhelyezés, aggregátumok és mentésséma. Képileg ekkor még a meglévő modellek használhatók. Elfogadás: újraindítás után ugyanazok az egyedek és készletek.
2. **Folytonos növekedés és egyedi kitermelés:** külön magasság/átmérő/korona, horgonyfüggvény, éves statisztika, rönkdepó és mérethelyes tönk. Elfogadás: havi határon nincs méretugrás; az érett fa jó körülmények között tovább vastagszik; részrakodáskor a túlélők mérete változatlan.
3. **Új famodellek és instancing:** először egy közeli tölgy- és lucmodell, majd mind a négy faj; fiatal, kifejlett, veterán és száraz ágváltozatok. Elfogadás: növekedéskor nincs teljes mesh-újraépítés, forgatva is felismerhető fajok, gyökérnyak nem mozdul el.
4. **Öregedés, halál, kidőlés és bomlás:** stresszmemória, ágvesztés, álló és fekvő holtfa, menthető animáció. Elfogadás: az egyed nem tűnik el közvetlenül halálkor; a téli lombhullás nem vált holtfává; betöltés ugyanott folytatja a kidőlést.
5. **Teljesítmény és hangolás:** sűrű 64/128/512-es térképek, sok egyidejű halál, időgyorsítás és több száz erdőéves headless futás. Elfogadás: a FPS, kamera és grafikai kapcsolók nem befolyásolják a növekedést vagy halálozást; a készletmérleg tolerancián belül zár; a memóriaigény hosszú futásban sem nő korlátlanul.

Külön determinisztikus látványteszt: ugyanazon négy faj több egyede 1, 10, 30, 80, 180 és 350 éves állapotban, továbbá részben száraz, álló holt és fekvő változatban. A halott egyed helyett nem kényszerítünk élő fát az összehasonlító képre. Az öregedés nem minden faj esetében ugyanazon korhoz tartozik.

A tesztmód gyorsított erdőórát kaphat, hogy percek alatt bejárja a teljes életciklust. A normál játék alaptempója ettől nem változik. A következő konkrét fejlesztési egység az 1–2. szakasz: e nélkül a szebb modellek továbbra is közös csempeállapotból származó látványelemek maradnának.
