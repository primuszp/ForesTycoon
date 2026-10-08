# Víz, időjárás és erdő: szimulációs terv

A teljes raszteres ökoszisztéma következő fejlesztési terve, a helyi terepszerkesztési javítás és
a szakirodalmi/.NET keretrendszer-értékelés: [ecosystem-simulation-design.md](ecosystem-simulation-design.md).
Az alábbi dokumentum a víz–időjárás modell korábbi tervezési előzményeit és megvalósult részeit őrzi.

Állapot: az első 1.0 játékmodell megvalósult; a részletes terv további szakaszokat is tartalmaz. A dokumentum végén szerepel a ténylegesen elkészült hatókör. A cél egy terepasztalon olvasható, determinisztikus erdőgazdálkodási szimuláció. Az eső a helyi vízkészletet változtatja; az erdő ezt felveszi, árnyékolja a talajt és módosítja a lefolyást. A grafikai kapcsolók kizárólag a megjelenítést vezérlik.

## 1. Kiindulópont a kódban

- `Terrain/Hydrology.cs`: terepfüggő medencék, folyócsomópontok, vízmélység és statikus `TileMoisture`. Nincs időben integrált csapadék–párolgás mérleg. A meglévő mélységet nem szabad automatikusan fizikai víztérfogatnak tekinteni.
- `Rendering/WeatherVisualState.cs`: napos/esős/viharos látvány, szél, villám, globális nedvesség. A grafikai beállítások befolyásolják; nem hiteles szimulációs forrás.
- `Simulation/Forestry/ForestSystem.cs`: havi erdőlépés, fajfüggő növekedés és regeneráció, jelenleg 30 másodperc/erdőév. A habitattól kapott nedvesség nem napi gyökérzónás vízkészlet.
- `Simulation/GameWorld.cs`: fix időlépés és parancsalapú mentés/visszajátszás. Az új rendszer működését verziózni kell.
- Meglévő köd, eső, vihar és szarvas megmarad; a hóval fehérré váló táj továbbra sem kerül az első változatba.

## 2. Időskála, intenzitás és játszható események

A korábbi javaslatot felülírja: a 30 másodperces erdőév nem megfelelő alap az új környezethez. A napi időjárás gyors váltogatása és annak hosszú grafikai kisimítása szétválasztaná a látható esőt a lehulló víztől. Az új világban eseményalapú időjárás és hozzá igazított erdőtempó szükséges. A régi mentések időskálája kompatibilitási módban megmarad.

### Intenzitásból számolt csapadék

A hiteles állapot `RainRate`, nem egy esik/nem esik kapcsoló. A vízlépésben `csapadék_mm = intenzitás × eltelt idő`; változó intenzitásnál az esemény burkológörbéjének integrálja. A szimuláció és az eső megjelenítése ugyanabból a pillanatnyi intenzitásból dolgozik. A részecskék számának grafikai korlátja nem csökkenti a lehulló vizet.

A vihar több összetevő: csapadékintenzitás, átlagos szél és széllökések, felhőzet, villámesemények. Erős szél nem jelent automatikusan sok esőt. A villám fényvillanása nem módosítja a csapadékot.

Az intenzitás szakmai konfigurációban mm/környezeti óra. A játékos által megfigyelhető időre történő átváltás explicit: `mm/játék-másodperc = mm/környezeti óra × környezeti órák/játék-másodperc`. Ugyanazt az időkonverziót használja a beszivárgás, párolgás és minden vízfluxus. Az esemény teljes csapadékát előre és futás közben is kijelezzük. A magas időgyorsítás nem változtatja meg egy esemény összes vízmennyiségét.

### Tartós események, nem állandó újrasorsolás

`WeatherEvent` tartalma: azonosító, kezdés, vég, típus, csapadékburkoló, szél/széllökések, hőmérséklet és páratartalom, seed. Az eseményt indulásakor sorsoljuk; képkockánként és naponként nem választunk új típust. Egymást követő események között minimum tartózkodási idő van. Gyengülés után rövid visszaerősödés lehet ugyanazon záporon belül, ez nem új időjárási mód.

Javasolt játszhatósági próbaértékek, normál játéksebességnél:

| Esemény | Megfigyelhető időtartam |
|---|---|
| Száraz/napos időszak | 2–5 perc |
| Borult időszak | 1–3 perc |
| Eső | 45–120 másodperc |
| Vihar aktív szakasza | 20–60 másodperc |
| Felerősödés/lecsengés | 5–15 másodperc, az esemény részeként |

Ezek kezdeti tesztértékek, nem valós meteorológiai időtartamok. Nem merev napos→borult→eső→vihar ciklus: a szezonális profil és az előző esemény korlátozza a következőt. Tartós szárazság több száraz eseményből is felépülhet, az ég minden eseményhatáron nem változik meg látványosan. A szél kis ingadozásai nem okoznak állandó állapotváltást.

### Erdőév és vízidő összehangolása

Kiinduló játékpróba: az erdő növekedési éve 15–20 játékperc, nem 30 másodperc. Így egy év alatt több jól megfigyelhető időjárási esemény és száraz periódus fér el. Ez még nem végleges alapérték: a fakitermelés, újulat és gazdasági ciklus sebességével együtt kell kipróbálni.

Fontos mértékegységbeli döntés: nem lehet egyszerre 365 fizikai napot 20 percbe sűríteni, egy valós záport egy percig mutatni és annak fizikai mm/óra intenzitását változtatás nélkül integrálni. Ez több napnyi folyamatos esőt és túl sok vizet adna. Ezért a gyors erdőév kezdetben növekedési/szezonális játékskála; a vízmérlegnek külön, explicit környezeti időkonverziója van. A két skála ugyanabból a fix játékórából származik, konfigurációban együtt szerepel és mentésbe kerül. A növekedési év nem állítja át automatikusan a vízfolyamatok óráját.

A vízidő-konverziót az események reális összcsapadékához és az események közötti kiszáradáshoz kalibráljuk. A növekedés a saját havi ablakában integrált tényleges vízstresszt használja, nem 365 napnyi párolgást feltételez. Az évszakok a növekedési évhez kötött, lassú hőmérsékleti és lombfelületi profilt adnak. Ez következetes játékmodell, nem fizikailag teljes, 365 napos éves hidrológiai előrejelzés. Későbbi valós naptári mód külön tempóprofil lehet.

A sebességszorzó az egész játékszimulációt gyorsítja: események, vízmérleg, erdő és járművek. Szünetben mind megáll. A normál sebességre megadott eseményhossz gyorsításkor arányosan rövidül. Az erdő fejlődési tempójának konfigurációs változtatása ettől külön beállítás.

A panel mutassa az esemény pillanatnyi intenzitását, várható hátralévő idejét és összcsapadékát. A grafikai időjárás kikapcsolása továbbra sem szünteti meg a csapadékot a környezetben. A kézi „Eső/Vihar/Villám most” látványteszt külön marad a naplózott, környezetet is módosító fejlesztői parancstól.

## 3. Helyi vízraktárak

| Csempénkénti állapot | Egység | Szerep |
|---|---|---|
| Lombkorona vízkészlete | mm | Felfogott eső, korona párolgása |
| Felszíni víz | mm | Pocsolya, beszivárgás, lefolyás |
| Gyökérzóna vízkészlete | mm | Növényi felvétel, talajpárolgás |
| Mélyebb vízkészlet | mm | Lassú utánpótlás és alapvízhozam |
| Felületi jég, későbbi hó vízegyenértéke | mm | Későbbi fagyás/olvadás |
| Összegzett vízhiány és túlzott nedvesség | nap, illetve normalizált stressz | Tartós erdőhatás |

A talajprofil tárolja a hervadáspontot, szántóföldi vízkapacitást, telítési kapacitást, beszivárgási és mélyebb átszivárgási sebességet. Első körben kevés, konfigurálható talajtípus elegendő. A fafaj gyökérmélysége módosíthatja az elérhető készletet; az első modell egy effektív gyökérzónát használ, nem rétegenkénti gyökérhálózatot.

A felszíni víz és a korona vízkészlete nem helyettesíti a gyökérzóna nedvességét. Rövid zápor után lehet nedves az út és száraz a talaj mélye.

## 4. Vízlépés és anyagmérleg

A környezeti frissítés rögzített környezeti részlépésekben történik; minden eseményhatárt és intenzitásváltozást integrálunk; nagy csapadék és gyors lefolyás esetén determinisztikus al-lépésekre bontjuk. Az al-lépések szükségességét a vízfluxus és a tárolókapacitás dönti el, nem a képkockaszám.

1. Eső egy része a korona szabad kapacitásába kerül, a többi a felszínre.
2. A felszíni víz beszivárog a talaj szabad kapacitásának és vezetőképességének megfelelően.
3. A párolgási igény kerete megoszlik a korona, a felszín és a talaj/növény között. Ugyanazt az energiakeretet nem használhatjuk fel többször.
4. A talaj a szántóföldi kapacitás felett a mélyebb tároló felé szivárog; onnan késleltetett vízhozam vagy könyvelt határveszteség keletkezik.
5. A felszíni lefolyás szomszédos cellákba, folyóba vagy medencébe kerül. A kiáramlást a tényleges rendelkezésre álló víz korlátozza.
6. Az erdő napi stresszét és a havi átlagokat frissítjük.

`összes tárolt víz változása = csapadék + külső befolyás − evapotranszspiráció − határkifolyás`.

A cellák közötti áramlás belső transzfer, nem veszteség. A mély szivárgás akkor veszteség, ha ténylegesen elhagyja a modellezett tárolókat. Minden fluxust térfogatban is könyvelünk: `m³ = mm × cellaterület(m²) / 1000`. A mm és a terep magassági egysége közötti átváltás külön szerződés.

A szomszédos átadások kétfázisúak: régi állapotból kiszámolt, összesen korlátozott kiáramlás; majd közös alkalmazás. A bejárási sorrend nem változtathatja meg az eredményt. Negatív vízkészletet nem utólagos, víztömeget eltüntető clamp kezel.

## 5. Kapcsolat a meglévő hidrológiával

A `Hydrology` kezdetben a domborzati hálózatot, medenceazonosítókat és lefolyási irányokat adja. A dinamikus készlet külön `EnvironmentSystem` tömbökben él. A régi `TileMoisture` csak induló habitatbecslés: kapacitásfüggő, explicit inicializálás alakítja vízkészletté.

Első szakaszban a tavak/folyók látványgeometriája maradhat rögzített. A bejutó vizet azonban explicit fogadó tároló vagy könyvelt térképi kifolyás veszi át; nem tűnhet el. Ez a szakasz még nem mutat dinamikus tóvízszintet és árvizet.

Második szakaszban medencénként térfogat–vízszint görbe szükséges, majd a túlfolyás a kifolyási magasság alapján. A tenger rögzített külső határ, a zárt tó véges tároló; eltérő szabályok vonatkoznak rájuk. Terepszerkesztéskor a domborzati hálózat újraszámolható, de a vízkészleteket konzervatívan kell átadni az új medencéknek. A lokális pocsolyák vékony felületek legyenek, ne minden esőtől emelkedő teljes tengervízszint.

## 6. Erdőválasz és visszahatás

A növekedés a havi lépésben a hónap során integrált hőmérsékleti, vízhiányos és túlnedves stresszből származik. Egyetlen hónapvégi mintavétel elrejtené a köztes aszályt.

- Mérsékelt vízhiány először csökkenti a növekedést; tartós, súlyos hiány egészségromlást és később mortalitást okoz.
- Telített talajon a pangó víz időtartama és a faj toleranciája határozza meg a károsodást.
- A regeneráció a helyi talajállapottól is függ; palánták érzékenysége külön paraméter.
- Fenyves és lombos állomány eltérő korona-víztárolást, szezonális lombfelületet, vízfelvételt és stressztoleranciát kap. A jelenlegi fajprofilokat bővítjük; fajonkénti konkrét értékek külön kalibrációt igényelnek.
- A fakitermelés csökkenti a korona felfogását és a növényi vízfelvételt, növelheti a talajra jutó sugárzást. A kivágott erdő ezért nem minden helyzetben egyszerűen „szárazabb”.

A `IForestHabitat` hosszú távú termőhelyi jellemzői megmaradnak. Az aktuális gyökérnedvesség és stressz külön környezeti interfészen érkezik; az éves alkalmasságot és a pillanatnyi állapotot nem keverjük össze.

## 7. Látható állapot és játékosfelület

A renderer csak olvassa a szimulációt: helyi vízfilm, nedves föld, pocsolya; később változó tópart. A fák fokozatos stresszszínt és ritkulást kaphatnak, nem egy nap alatt átváltó teljes erdőszínt.

Köd helyi felszíni nedvesség, páratartalom, lehűlés, szél és völgyhelyzet alapján jelenjen meg foltokban az erdőben/víz mellett. A kezdeti szabály becslés, nem teljes légköri párakondenzációs modell.

Környezeti panel: év/hónap/nap, hőmérséklet, mai és 30 napos csapadék, kiválasztott cella gyökérnedvessége, vízhiány/túlnedvesség és növekedéscsökkenés oka. Kapcsolható térképrétegek: talajnedvesség, felszíni víz, erdőstressz; később tűzveszély. A textúrázás nélküli mód ezekhez is kap színes visszajelzést.

Úttapadás későbbi fogyasztója lesz a helyi vízfilm/jég/sár adatoknak. Erdőtűz előtt külön avar- és holtfa-készlet, azok nedvessége és valódi gyújtóesemény szükséges. Az aszály önmagában nem gyújt tüzet; a grafikai villámteszt nem gyújtóesemény.

## 8. Architektúra, skálázás, mentés

Javasolt új elemek: `EnvironmentCalendar`, `WeatherSystem`, `EnvironmentSystem`, `SoilProfile`, `EnvironmentSnapshot` és havi `ForestEnvironmentSummary`. Szimulációs sorrend: időjárás → víz és napi stressz → esedékes havi erdőlépés → további fogyasztók. Több részlépést átlépő update minden eseményhatárt, vízlépést és növekedési hónapot a helyes sorrendben dolgoz fel, nem egyetlen végső időjárást használ. A napi összesítések a vízidőt, a havi erdőösszesítések a növekedési időt követik; az interfész mindkettő egységét egyértelművé teszi.

Csempénként tömbök, előre kiosztott fluxuspufferek. A talaj párolgása száraz, nem látható cellákon is halad: kameraalapú szimulációs kihagyás nincs. A felszíni lefolyás aktívhalmazzal gyorsítható, a ritkább talajlépések költsége mérendő. A teljes medencekeresést csak terepváltozás indokolja. A nedvesség változása ne érvénytelenítse a terep teljes geometriai gyorsítótárát; külön környezeti revízió és ritkított anyagfrissítés szükséges.

Mentés/visszajátszás: környezeti modellverzió, növekedési és vízidő-konverzió, aktív időjárási esemény és burkológörbéje, konfiguráció, seed és generátorállapot, vízraktárak, stresszintegrálok, részlépés maradékidő. A jelenlegi parancsalapú replay esetén vagy a teljes kezdeti állapotból verziózott determinisztikus újraszámítás, vagy konzisztens checkpoint kell. Későbbi checkpoint betöltése után csak az azóta történt parancsokat játsszuk vissza. Régi mentések megtartják korábbi növekedési szabályaikat; az új környezet bekapcsolása explicit migráció, nem észrevétlen replay-változás.

## 9. Megvalósítási sorrend és elfogadás

1. **Közös naptár és hiteles időjárás:** tartós seedelt események, intenzitásintegrálás, összehangolt növekedési és vízidő, szünet/gyorsítás/replay és grafikai függetlenség. A meglévő renderer adaptert kap.
2. **Talaj- és koronavízmérleg:** eső, beszivárgás, párolgás, növényi felvétel, mélyebb tároló; nedvességpanel és vízmérlegdiagnosztika.
3. **Helyi lefolyás és fogadó tárolók:** konzervatív szomszédos átadás, lejtők/völgyek, vízfolyás-kapcsolat. Utána dinamikus tóvízszint.
4. **Erdőstressz:** havi integrált válasz, fajprofilok, palánták és fakitermelési visszahatás; látvány és magyarázható panel.
5. **Fagy, úttapadás, tűz alapjai:** külön szakaszok. Hóborítás továbbra is külön grafikai fejlesztés.

Első implementációs csomag: az 1. pont és a 2. pont izolált, GL-mentes vízmérlegmagja. A teljes tó- és tűzrendszer nem szükséges ehhez.

Kötelező ellenőrzések: állandó intenzitásból pontos eseménycsapadék; intenzitásburkoló integrálása; minimum eseményidő betartása; eseményhatáron átlépő nagy update; zárt rendszer tömegmegmaradása; ismert mm→m³ átváltás; száraz időszak monotón készletcsökkenése; telített talaj korlátozott beszivárgása; víz továbbadása és határkifolyás; azonos eredmény szünet/gyorsítás/render FPS és grafikai minőség mellett; mentés–betöltés–replay; hónaphatári stresszintegrál; terepszerkesztés vízmegmaradással. A tűréshatár a használt lebegőpontos típushoz és összes fluxushoz igazodó, dokumentált numerikus tolerancia legyen.

Teljesítményt 64², 128² és 256² csempén, száraz és telített terepen mérünk; napi lépés, lefolyási al-lépések és éves összköltség külön riportban. Célkeretet mérés után rögzítünk. A fizikát a grafikai minőség nem változtathatja meg.

## 10. Szakmai alap és korlátok

A réteges tárolók és folyamatok mintáját a [HEC-HMS Soil Moisture Accounting](https://www.hec.usace.army.mil/confluence/hmsdocs/hmstrm/canopy-surface-infiltration-and-runoff-volume/infiltration/soil-moisture-accounting-loss-model) adja. A korona kezeléséhez a [HEC-HMS Simple Canopy](https://www.hec.usace.army.mil/confluence/hmsdocs/hmstrm/canopy-surface-infiltration-and-runoff-volume/canopy-interception/simple-canopy-model) hasznos. A tervezett játékmodell ezek egyszerűsített saját adaptációja.

A vízhiány és a rendelkezésre álló gyökérzónás készlet kapcsolatának kiindulópontja a [FAO-56 vízstressz és vízmérleg fejezete](https://www.fao.org/4/x0490e/x0490e0e.htm). A referencia-párolgás részletesebb későbbi becsléséhez a [FAO Penman–Monteith módszer](https://www.fao.org/4/x0490e/x0490e06.htm) szolgál alapul. Mezőgazdasági referenciaértékekből nem következnek automatikusan erdei fajparaméterek. A napi tárolómodell, a grafikai simítás és a stresszátlagolás itt javasolt mérnöki döntés, nem a forrásokból átvett teljes erdőmodell.


## 11. Megvalósított 1.0

Az `EnvironmentSystem` a normál `GameWorld` része. Seedelt, tartós események, lineáris intenzitásburkoló pontos integrálása, fokozatos felhőátmenet és intenzitásfüggő vihar működik. Fix 0,5 játék-másodperces vízlépés; eseményhatárok és burkoló-töréspontok további részlépést kapnak. Egy növekedési év 1200 játék-másodperc; a vízóra 60 játék-másodperc. Ezek az 1.0 modellverzió rögzített paraméterei.

A gyökérzóna kapacitása 180 mm, a mély szivárgás 130 mm felett indul, a növényi vízfelvétel 25 mm alatt leáll. A korona fafajtól és érettségtől függő tároló; a vízfelvétel nyílt terület és erdő között eltér. A beszivárgás talajon 12, úton 0,5 mm/környezeti óra. Ezek egyszerűsített játékparaméterek, nem helyspecifikus erdészeti mérések.

Négy vízraktár, közösen korlátozott párolgási keret, mélyebb alapvízhozam és régi állapotból számolt szomszédos lefolyás működik. A vízmérleg diagnosztikája cellánként összegezett mm-ben fut; egyenlő cellaterületeknél ez közös területszorzóval térfogattá alakítható. Dinamikus tóvízszint nincs, a vízfolyás és a térképi kifolyás a meglévő hidrológiai geometriához illeszkedő, könyvelt határveszteség. Több talajprofil és szezonális lombfelület még nincs.

A hónap során integrált vízhiány/túlnedvesség az erdő tényleges növekedésére, egészségére és regenerációjára hat. A tölgy kevésbé érzékeny a vízhiányra, mint a lucfenyő. A renderer külön környezeti textúrát frissít, nem építi újra minden vízlépésben a terepgeometriát; a koronák szárazságszínt, a terep helyi nedvességet, a köd helyi nedvességi bemenetet kap. Az eredeti színalapú mód működik és a panel ott is hozzáférhető.

A Környezet panel kézi indítása `SetWeatherCommand`, a mentési parancsnapló része. Az 1.0 mentés determinisztikus replay-t használ, nem külön környezeti checkpointot. A régi, hiányzó/0 környezeti verziójú mentés megőrzi a korábbi erdőszimulációt. Terepszerkesztés újraszámolja a lefolyási kapcsolatokat, a tárolt víz megmarad.

Ellenőrzés: 148 sikeres teszt; intenzitás és vízmennyiség arányossága, eseményburkoló integrálja, esemény stabilitása, kis/nagy update azonossága, vízmegmaradás, telítési korlát, lejtőirányú lefolyás, száraz idő, tényleges erdőstressz, grafikai függetlenség és mentési verzió. A `--environment-smoke-test` valódi GameWorld mentés–betöltést, vízállapot pontos azonosságát, viharos/száradó képeket és GL-hibamentességet ellenőriz. Szüneteltetett kép ellenőrzésénél 1/255 csatornatűrés szükséges az MSAA-kerekítés miatt. A teljes játék smoke is sikeres.

Mért izolált vízlépés: 64² cella 0,53 ms, 128² cella 2,44 ms, 256² cella 8,76 ms. Ez fél játék-másodpercenkénti CPU-munka, nem teljes renderidő; a benchmark nem tartalmaz erdős terep- és úthálózat-lekérdezéseket. A teljes játék rövid smoke alatt kb. 7,7 ms simított képkockát adott. A rövid minták nem helyettesítik a hosszú, nagy erdőn futó terhelésvizsgálatot.
