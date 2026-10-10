# Eső, vihar és felhőzet: kutatás és megvalósítási terv

2026-10-03-i kutatási és megvalósítási jegyzet, későbbi kiegészítésekkel. A kezdeti terv a havat kizárta; 2026-10-10-én a felhasználó évszakos havazást kért. Ennek aktuális megvalósítása és forrásai az alábbi kiegészítésben szerepelnek.

## Évszakos havazás — 2026-10-10

Elsődleges források: [Stout et al., 2024: Stable and unstable fall motions of plate-like ice crystal analogues](https://acp.copernicus.org/articles/24/11133/2024/), valamint [Quantification and parameterization of snowflake fall speeds in the atmospheric surface-layer, 2025](https://acp.copernicus.org/articles/25/16729/2025/). A laboratóriumi analógkísérletek stabil, billegő és spirális mozgásokat különítenek el; az alak, tehetetlenség és Reynolds-szám befolyásolja a viselkedést. A terepi sebességvizsgálat a turbulencia és a hópelyhek szerkezetének együttes szerepét mutatja.

A játék ezekből vizuális közelítést készít: a GPU-részecskék egyedi, lassú süllyedési sebességet, eltérő méretet, két tengely menti periodikus oldalmozgást, finom forgást és billegést kapnak. A szél korlátozott sodródást ad. A kamera felé forduló, lágy szélű pehelyfelületek tömör aggregátumokat és ritkább, hatsugaras sziluetteket közelítenek. A 0,4–1,15 világegység/másodperc tartomány saját grafikai választás; nem a tanulmányokból átvett fizikai kalibráció. A folyamat a szimulációs órát követi, így szünetben azonos képet ad. Az eső megtartja a gyors, szélirányhoz igazított csíkokat.

A normál játék télen automatikusan havazhat, ősszel gyakori, hosszabb esőt, nyáron rövid záporokat és intenzív viharokat választ a seedelt eseménygenerátor. A hó csempénként külön vízkészlet, vízegyenértékben mérve; fagypont felett a felszíni vízbe olvad. A meglévő irány- és anyagfüggő hófedés ezt a készletet követi. A koronák formái és fafajspecifikus textúrái megmaradnak. A modell nem oldja meg az egyedi hókristályok aerodinamikáját, a kristályok ütközését vagy a hótakaró tömörödését.

Ellenőrzés: `SeasonalWeatherTests`, a v12→v13 mentésmigrációs teszt, valamint `--seasonal-weather-smoke-test`. A natív próba a normál automatikus időjárást és a valódi GPU-hórajzoló szüneteltetését és animációját ellenőrzi, évszakos képi mintákat is ment.

## Döntés

A működő esőt megtartjuk. A következő grafikai csomag a részleteket erősítse: változatos, megvilágításra reagáló esőcsíkok, cseppbecsapódások, anyagonként eltérő nedvesedés, széllel mozgatott felhők és felhőárnyék. Utána jöjjön a vihar: széllökések, hozzájuk igazított lombmozgás, nagyobb csapadékintenzitás és ritka villámok. Teljes térfogati felhőzet csak külön magas minőségi profilon és mérés után készüljön.

A terepasztal távolról is maradjon olvasható. Ne váljon a vihar egy egységes sötét vagy szürke képréteggé, ahogy a jelenlegi hó elvesztette a felszín részleteit. A korábbi színalapú mód és az önálló textúrakapcsoló megmarad.

## 1. Mit mondanak a források?

Az alábbiak elsődleges források. Külön jelölöm a tudományos publikációkat és a fejlesztői technikai anyagokat. A javasolt ForesTycoon-paraméterek és egyszerűsítések saját tervezési döntések, nem a tanulmányok mért eredményei.

| Forrás | Típus | Használható eredmény |
|---|---|---|
| Garg–Nayar, **Photorealistic Rendering of Rain Streaks**, SIGGRAPH/TOG 2006 | Tudományos paper | Az esőcsík kinézete nézet-, fény- és cseppalakfüggő; a csík nem homogén világos vonal. A cikk textúraadatbázissal közelíti ezt. |
| Lagarde, **Water drop 2a – Dynamic rain and its effects**, 2012 | Fejlesztői technikai cikk | Felülnézeti mélységtérképpel becsülhető a csapadék takarása és a becsapódások helye. |
| Lagarde, **Water drop 3a / 3b – Physically based wet surfaces**, 2013 | Fejlesztői elemzés és megvalósítás | A nedves megjelenés függ az anyag porozitásától és érdességétől; a vízréteg és a beivódó víz eltérő hatásokat okoz. |
| Schneider–Vos, **The Real-Time Volumetric Cloudscapes of Horizon Zero Dawn**, SIGGRAPH 2015 | Játékfejlesztői konferencia-előadás | Térfogati felhőforma Perlin–Worley zajjal, magassági profillal és időjárási térképpel; külön modellezés, megvilágítás és mintavételezés. |
| Zioma, **GPU-Generated Procedural Wind Animations for Trees**, GPU Gems 3, 2007 | Technikai könyvfejezet | A nagy erdők mozgását vertex shaderben, zajjal és egyszerű hierarchiával lehet hihetően közelíteni. |
| Kim–Lin, **Physically Based Animation and Rendering of Lightning**, Pacific Graphics 2004 | Tudományos paper | A dielektromos átütési modell elágazó kisülési csatornát ad; a megjelenített fényudvar külön probléma. |
| Kim–Lin, **Fast Animation of Lightning Using an Adaptive Mesh**, TVCG 2007 | Tudományos paper | Az adaptív rács gyorsítja a potenciálmező megoldását; a teljes modell még így is összetettebb egy egyszerű villámeffektnél. |

Pontos források:

- [Garg–Nayar paper PDF](https://cave.cs.columbia.edu/Statics/publications/pdfs/Garg_TOG06.pdf), [kiadói rekord és DOI](https://doi.org/10.1145/1179352.1141985).
- [CAVE esőcsík-adatbázis](https://www.cs.columbia.edu/CAVE/databases/rain_streak_db/rain_database.html). A közzétett minták egy rögzített cseppmérethez és expozícióhoz készültek; nem lehet őket skálázás nélkül minden nézetre alkalmazni.
- [Lagarde: dinamikus eső és takarás](https://seblagarde.wordpress.com/2012/12/27/water-drop-2a-dynamic-rain-and-its-effects/).
- [Lagarde: nedves felületek 3a](https://seblagarde.wordpress.com/2013/03/19/water-drop-3a-physically-based-wet-surfaces/), [3b és játékbeli közelítések](https://seblagarde.wordpress.com/2013/04/14/water-drop-3b-physically-based-wet-surfaces/).
- [Guerrilla: hivatalos felhőelőadás-oldal](https://www.guerrilla-games.com/read/the-real-time-volumetric-cloudscapes-of-horizon-zero-dawn), [teljes, 99 oldalas PDF](https://d3d3g8mu99pzk9.cloudfront.net/AndrewSchneider/The-Real-time-Volumetric-Cloudscapes-of-Horizon-Zero-Dawn.pdf).
- [Zioma: GPU-s lombmozgás](https://developer.nvidia.com/gpugems/gpugems3/part-i-geometry/chapter-6-gpu-generated-procedural-wind-animations-trees).
- [Kim–Lin 2004 paper PDF](https://gamma-web.iacs.umd.edu/LIGHTNING/lightning.pdf), [2007-es tanulmány szerzői projektoldala](https://gamma-web.iacs.umd.edu/FAST_LIGHTNING/index.html).

A Guerrilla előadás kb. 2 ms-os eredményét nem lehet a mi motorunkra átvenni teljesítményígéretként. A kutatás alapelvei hasznosak, de más hardveren és más képi kompozícióval kell ellenőrizni őket. A teljes villámmodell pedig részleges differenciálegyenletek megoldását tartalmazza; egy véletlenül tört vonal nem ennek az implementációja.

## 2. A jelenlegi kód korlátai

`Rendering/WeatherRenderer.cs` jelenleg instanced billboardokat rajzol. Az esőcsíkok színe és alakja közel állandó. A nap és a kamera relatív iránya nem változtatja a csík textúráját. Az emissziós terület a látható terephez igazodik, ezért mozgó kameránál a véletlen mintázat is elmozdulhat: ezt világkoordinátás cellákhoz kell rögzíteni.

`Terrain/Terrain.WeatherVisuals.cs` csempénként becsüli a felszín és a korona magasságát. Ez olcsó, de a korona réseit nem látja és a becsapódás helye nem pontos. `Rendering/SurfaceVisualRenderer.cs` nedvességi szorzót és ismétlődő vízgyűrűt használ. Nincs külön nedves érdesség, vízfilm vagy helyi pocsolyaállapot. A `Cloud` érték jelenleg fényátmenet, nem kirajzolt felhőzet. Nincs közös szélmező, villám vagy viharprofil.

A geometriákba korábban belekerült színárnyalás továbbra is megmarad. Emiatt a vihar sötétítését főleg a közvetlen napfény csökkentésével kell kezdeni, különben a fák túl sötétek lesznek. Az eredeti színek épségét külön képi regressziós ellenőrzés védi.

## 3. Eső: következő megvalósítás

### Esőcsíkok

Az első lépés egy kicsi, saját készítésű esőcsík-textúrakészlet vagy LUT: eltérő fényirányokra és 2–3 cseppméretre. A tudományos adatbázis referencia és összehasonlítási alap; külső textúrák átvételénél a használati feltételeket ellenőrizni kell. A felület változó fényét és a nézetirányt a billboard shader kapja meg.

Ortografikus kamera esetén a látható csíkhosszt a képsíkra vetített sebesség és egy virtuális expozíció adja:

`csíkhossz_pixel = pixelsPerWorldUnit × length(projectToCameraPlane(velocity)) × exposureSeconds`

A megvalósítás az expozíciót hangulati paraméterként használja, nem a render frame idejeként. Máskülönben kisebb FPS-nél hosszabb csíkok jelennének meg. A sebesség, expozíció, méret és alfa együtt legyen hangolható. A jelenlegi világkoordináta nem dokumentált méteregység: m/s vagy mm/h megadásához előbb egyértelmű világ–méter átváltás szükséges. Addig a rendszer fizikailag inspirált vizuális modell.

Világkoordinátás cellaazonosító + részecskeazonosító + seed határozza meg a mintát. A kamera csak a megjelenítendő cellákat választja ki, a cseppek helyét nem húzza magával. A különböző magasságú légrétegek szélsebessége külön szabályozható. Távoli zoomnál a felületi hatások domináljanak, közel több változatos csík látszódjon.

### Becsapódás és takarás

Elsőként pontosabb terepmintavétel és szelektív fröccsenések. Következőként egy külön, felülnézeti `RainOcclusionMap` kapjon terepet, fákat, járműveket és később épületeket. Ez nem ugyanaz, mint a nap árnyéktérképe: más a vetítési irány és más a célja.

A csepp ezen a felületen véget ér. Fröccsenésből csak néhány tucat közeli esemény kell képkockánként, nem minden látható csepphez egy új objektum. Vízre gyűrű, útra apró csillanó spray, talajra visszafogott nedves becsapódás kerüljön. A részecskéket poolból vagy shaderes életciklussal használjuk. A GPU-s takarási adatot lehetőleg közvetlenül GPU-n mintázzuk; ne olvassunk vissza minden frame-ben szinkron módon mélységet.

A térfogat csak a terepasztal fölött legyen, és ne a teljes ablakon. MSAA mellett később külön feloldott jelenetmélység szükséges a soft particle megoldáshoz. Ennek elkészültéig maradjon a jelenlegi normál mélységteszt.

### Nedves felszínek

Első változat: anyagonként `wetDarkening`, `dryRoughness`, `wetRoughness`, `waterFilm` paraméter, illetve Fresnel-függő, energia szempontjából korlátozott csillanás. Az avar és föld másképp reagáljon, mint a kavics, kéreg vagy festett jármű. A víz nem fém: a nedves talaj metallic értékét nem emeljük.

A helyi nedvesség kis felbontású, részlegesen frissített állapottextúrában maradhat, hogy ne építsen új VBO-t. A pocsolya maszkja mélyedésből, útfelületből és felhalmozódásból származzon. Egy zajtextúra világos foltja önmagában még nem pocsolya. Első körben csak vizuális tározás; a meglévő hidrológiát külön játékmeneti változtatás nélkül nem módosítjuk.

## 4. Felhőzet terepasztalon

### Ajánlott alap: mozgó felhőréteg és kapcsolt árnyék

Egy 2D időjárási mező tároljon fedettséget, vastagságot és felhőtípust. Saját 3D zajtextúrából lehet előállítani a formákat, de az első megjelenítés néhány rétegre egyszerűsített legyen. A napos állapot ritkább gomolyokat, a borult állapot összefüggőbb réteget, a vihar vastagabb és alul sötétebb felhőket kapjon.

A látható felhők és a talaj felhőárnyéka ugyanazt a mezőt, sebességet és időt használják. A felületből a nap irányában a felhőrétegig vetített mintapont:

`cloudXY = surfaceXY + (cloudHeight - surfaceZ) × sunXY / max(sunZ, epsilon)`

A napfény átengedését optikai vastagsággal közelítjük: `T = exp(-tau)`. A talajon a közvetlen napfényt módosítjuk, nem az egész képet szürkére szorozzuk. A térképen mozgó árnyékfoltok közel és távol is érzékeltetik a felhőket, és jobban illenek a magasból nézett maketthez, mint kizárólag egy égbolttextúra.

A fő nézetben a felhők a makett hátterében vagy felső sávjában jelenjenek meg; a felettünk lévő réteg hatását elsősorban az árnyék és fény adja. Ez tudatos, metszetszerű művészeti egyszerűsítés. Teljes, fizikailag takaró felhőtető könnyen elfedné az erdőt. A UI és az oldalfal ne kapjon esőpárát vagy felhőtakarás-overlayt.

### Későbbi magas profil: térfogati felhő

Korlátozott felhődobozban, fragment shaderes ray marching OpenGL 3.3-on is tervezhető; a javasolt első változat nem igényel compute shadert. Ortografikus nézetben a sugarak párhuzamosak, de origójuk pixelenként eltér: a perspektivikus égboltrenderelőt nem lehet változtatás nélkül átvenni.

Kiinduló saját mérési konfiguráció: 64³ alapzaj, 32³ részletzaj, fél felbontású színes/transzmittancia framebuffer, 24–48 szemirányú minta, 4–6 napirányú minta, üres tér kihagyása és korai megállás alacsony transzmittanciánál. Ezek kísérleti induló értékek. Időbeli mintamegosztás csak világkoordinátás visszavetítéssel és hibás history eldobásával kerülhet be; a jelenlegi rendererben nincs hozzá mozgásvektor/history rendszer.

A formai és fénymodell a Guerrilla előadásból adaptálható, de a zajos, kis mintaszámú felhő nem kerülhet a részletgazdag erdő elé. A nagy minőségi profil külön kapcsoló és külön GPU-mérés legyen.

## 5. Vihar

`StormState`: csapadékerősség, alap szél, széllökés, felhőfedettség, optikai vastagság és villámaktivitás. A mozgások közös szélmezőt olvassanak. Így ugyanarra fúj az eső, sodródik a felhő és hajlik a lomb. A vihar ne csak több esőrészecskét jelentsen.

Lombmozgáshoz a koronamesheknek kell egy faazonosító vagy stabil fázis, talppont és hajlítási súly. A világ Z-koordinátája önmagában nem jó súly, mert a dombtető fája másképp mozogna, mint az ugyanolyan fa a völgyben. A talp maradjon helyben; a magasabb ágak és korona mozduljanak. A fő render és az árnyékpass ugyanazt a deformációt használja. A régi mód változatlan, animáció nélkül is választható marad.

Villám első megoldása: előre generált vagy kis CPU-költségű, seedelt elágazó csatornák és rövid intenzitásgörbe. A háttérben felhőn belüli villanás is jelezhet távoli vihart, a látható csatorna ritkább legyen. Ha DBM alakokat használunk, azokat offline vagy eseményenként számoljuk, nem minden render frame-ben. Procedurális vonal esetén fizikailag inspirált közelítésként dokumentáljuk.

A villanás helyi fényt adjon a felhőnek, esőnek és tájnak, ne fedje le fehér képernyőquadként a képet. Saját kezdeti hangolási érték: 0,05–0,2 másodperces főimpulzus, esetleg gyenge utóimpulzus; világítási és fényudvar-erősség külön korlátozható. Ez nem meteorológiai mérési adat. A HDR/fényudvarhoz külön színes framebuffer kellhet; az első LDR változat csak korlátozott fényerő-növelést használjon. Hangrendszer későbbi csatlakozásakor a mennydörgés késleltetett esemény legyen, világ–méter átváltásból számolt távolsággal.

## 6. Megvalósítási csomagok

| Sorrend | Csomag | Ellenőrizhető eredmény |
|---|---|---|
| 1 | Világhoz rögzített eső, fény-/nézetfüggő csíkok, közös széladat | Pan/forgatás alatt nem csúszik a csapadékmező; napsütés és borult idő eltérő esőképet ad |
| 2 | Felhőréteg és felhőárnyék | A látható felhő és árnyéka ugyanarra mozog; a táj olvasható marad |
| 3 | Anyagonkénti nedvesedés, pontosabb becsapódás, ritka fröccsenés/gyűrű | Eső után változatos felületek maradnak; nincs teljes térképes csillogás |
| 4 | Viharprofil, szélroham és koronamozgás | Az eső, felhő és lomb együtt változik; az árnyék követi a mozgást |
| 5 | Villám és fényreakció | Az esemény látható, de nem veszi el a táj részleteit |
| 6 | Opcionális térfogati felhő magas profilon | Saját célhardveren mérve is belefér a kiválasztott frame-keretbe |

Javasolt új renderer-adatok: `WeatherLightingState`, `WindField`, `CloudCoverageMap`, `RainOcclusionMap`, `WetSurfaceState`, `LightningEvent`. A jelenlegi `WeatherVisualState` legyen ezek átmeneteinek forrása, ne egymástól független órák vezéreljék az effekteket. A szünet mindet megállítja. A véletlen vizuális seed ne legyen a növekedési szimuláció RNG-je.

## 7. Mérés és elfogadás

Referencia: száraz/enyhén esős/erősen esős/viharos állapot, mindegyik 3 zoom és 4 kamerairány, plusz folyamatos pan és forgatás. Kell erdő, tisztás, út, jármű, víz és meredek lejtő. Azonos kamera és rögzített vizuális idő mellett legyen képösszevetés.

GPU timer query külön az esőhöz, felhőhöz, becsapódásokhoz és fényudvarhoz; CPU frame-, p95-, max-, feltöltés- és mesh-rebuild adatok mellett. Saját kiinduló cél a teljes új időjárásra normál profilon 2–4 ms többlet a választott tesztgépen, de ez nem garantált vagy már megmért érték. A kis felbontású felhő is lehet drága sok áttetsző túlfestéssel.

Elfogadási feltételek: nulla időjárás miatti terep/erdő mesh-rebuild; stabil világkoordinátás részecskék; nincs fedetlen eső az oldalfalon; a korona mélységi takarása helyes; a megvilágítás és takarás együtt kapcsolható; az eredeti shaderkép visszaállítható. A hó a normál játékban és az automatikus ciklusban továbbra is tiltott.


## Megvalósított első lépcső (2026-10-03)

A Nézet menü új Vihar módja esőt, erősebb, időben változó szelet és determinisztikus kettős villámfényt kapcsol össze. A Felhőzet és Villámlás külön kapcsolható; az eredeti színalapú mód és az önálló textúrakapcsoló megmaradt. Az automatikus ciklusban a korábbi második esős szakasz vihar lett. Hó továbbra sincs a normál játékban.

- **Eső:** kamerafüggetlen világcellákból, determinisztikus részecskemagokkal keletkezik. A sebesség és a 25 ms-os virtuális expozíció adja a csík hosszát; ez nem függ a képkockasebességtől. A szél és a talaj/lombkorona magasságtérképe módosítja a cseppek pályáját és eltakarását. Nagy látható területnél ritkább cellák korlátozzák a részecskeszámot.
- **Felhők:** egy háromdimenziós, három oktávos procedurális zajjal kitöltött, vékony rétegen 12 mintás, elölről hátrafelé végzett Beer–Lambert integrálás. A háttérként rajzolt felhők nem fedik el a terepasztalt. A felszíneken mozgó zajmezőből számított transzmittancia ad puha felhőárnyékot.
- **Vihar:** fokozatos sötétedés, széllökések és rövid kettős fényimpulzus. A fény a felszínek tájolásától függ, a felhőréteget is megvilágítja. A szünet és az időjárás kikapcsolása a megjelenítési állapotot is kezeli.
- **Víz és nedvesség:** a vízgyűrűk szomszédos világcellák eltérő időpontú, eltérő helyű becsapódásai körül terjednek és elhalnak. Az utak és lapos talajrészek foltos, irányfüggő nedves csillanást kapnak.

Ez a kutatási terv első, egyszerűsített implementációja. Nem Garg–Nayar mért esőatlasza, nem a Horizon teljes felhőrendszere, és nem fizikai villámcsatorna-szimuláció. A felhőárnyék kétdimenziós közelítés, nem a háttér térfogati sűrűségének pontos integrálja. A lombkorona eltakarása továbbra is állományszintű magasságtérképre épül; fánkénti esőmélység, talajra rajzolt fröccsenés, szélben hajló fák, villámgeometria és időbeli felhő-rekonstrukció későbbi lépcső.

Ellenőrzés: 116 egységteszt sikeres; a grafikai OpenGL ellenőrzés az eső/vihar/felhő kapcsolókat, a szünet alatt változatlan képet, a geometriai cache megőrzését és az eredeti kép visszaállítását is vizsgálja. Az erdő grafikai és a teljes játék indítási ellenőrzése is sikeres. A játék normál, napos ellenőrzésében a simított teljes képkocka CPU-ideje 3,94 ms volt; ez nem viharos GPU-teljesítménymérés.


### Második lépcső: villámcsatorna és erdei köd

A villámfényhez most eseményenként azonos, determinisztikus, cikkcakkos és oldalágakkal kiegészített csatorna tartozik. Világkoordinátákban fut a felhőréteg magasságától egy látható fa koronájának csúcsáig; a koronacsúcsot ugyanaz a faj-, méret- és lombkorona-modell számítja, amely a fákat kirajzolja. Vékony világos mag és szélesebb áttetsző kék fényudvar teszi olvashatóvá. Ez továbbra is procedurális közelítés, nem dielektromos áttörés numerikus modellje.

A talajköd erdős csempék földfelszínéhez igazított, kamera felé forduló, puha szélű ködfoltokból áll. Mélységi teszt, hátulról előre rendezés, alacsony magasság és lassú világkoordinátás moduláció segíti a fák közötti megjelenést. Ez közelítés, nem teljes térfogati ködintegrálás. Külön kapcsolható és sűrűsége állítható; az időjárás felhőzetével együtt fokozatosan erősödik vagy eltűnik.

A grafikai ellenőrzés külön képpel vizsgálja a köd kikapcsolását, a villámcsatorna/fény kikapcsolását és a villámlás alatt szüneteltetett kép változatlanságát. A 116 egységteszt és az OpenGL ellenőrzés sikeres; a statikus geometria nem épül újra az effektusok miatt.


### Aktiválás javítása

A Talajköd immár kézi kapcsoló: napsütésben is azonnal látszik, nem függ a felhőzet erősségétől. Alapból ki van kapcsolva. A Villám most gomb vihar és várakozás nélkül is létrehoz egy látható csatornát és fényimpulzust; szünetben a megjelenő kisülés is megáll. A rendszeres viharvillám kettős impulzusa lassabban halványul el (legfeljebb 0,65 másodperc), hogy jobban észrevehető legyen.


### Soft particle köd

A korábbi statikus ködfoltokat háromrétegű, eltérő élettartamú (18–32 másodperces), világkoordinátákban sodródó részecskék váltották fel. Az életciklus elején és végén csökken az átlátszatlanság; közben változik a méret és a procedurális zajmintázat. Az erdős csempékből kiinduló pamacsok az erdőszélekre és a tisztásokra is átnyúlnak, pozíciójuk a helyi felszínmagassághoz igazodik.

A rendszer a jelenet mélységét külön, képernyőméretű mélységi textúrába másolja az átlátszatlan geometria után. A fragment shader a kamera inverz mátrixával visszaállítja a felület világpozícióját, és a részecske-felület távolság alapján 1,8 világegységen belül finoman halványít. A mélységi teszt megtartja a fák takarását. A textúra újraméreteződik a viewport változásakor; nincs képkockánkénti CPU mélység-visszaolvasás.

A soft particle elv forrása: [NVIDIA Soft Particles](https://developer.download.nvidia.com/SDK/10.5/direct3d/Source/SoftParticles/doc/SoftParticles_hi.pdf). Ez sprites alapú közelítés, nem teljes térfogati fényszórás.

Kijavítottuk a korábbi billboard irányhibáját: 45 fokos döntésnél a ködfoltok a kamera felé fordulás helyett gyakorlatilag élükkel látszottak. A kamera felé fordulást három különböző nézetre egységteszt ellenőrzi. A grafikai ellenőrzés már legalább 5000 érdemben megváltozott képpontot vár el a ködtől, így puszta kerekítési eltérés nem jelent sikert. A szüneteltetett köd és a viewport újraméretezése is ellenőrzött.


### Foltos köd és környezeti viselkedés

A ködforrások erdős csempékhez, vízhez/vízparthoz vagy helyi mélyedésekhez kapcsolódnak. A völgyesség a szomszédos csempék átlagmagasságából számított relatív mélység. Egy folytonos, világkoordinátás, lassan sodródó zajmező választja ki az aktív foltokat, így nem minden alkalmas hely ködös egyszerre. A talaj alapnedvessége és az eső után megmaradó Wetness növeli a ködöt; a szél erőssége és a napsütés csökkenti. A ködkapcsoló ezt a természetes eloszlást engedélyezi, a sűrűségcsúszka annak erősségét szabályozza.

A meteorológiai alap: [Met Office – ködtípusok és képződés](https://weather.metoffice.gov.uk/learn-about/weather/types-of-weather/fog), [NWS – völgyköd](https://www.weather.gov/safety/fog-mountain-valley). A vízközelség/nedvesség, a völgyek és a gyenge szél szerepét használjuk. Teljes hőmérsékleti, harmatpont- vagy nappal/éjszaka-modell még nincs; ez szemléletes közelítés.

125 teszt sikeres: az új vizsgálatok ellenőrzik a ködfoltok és tiszta területek együttes jelenlétét, az esőnedvesség erősítő és a szél gyengítő hatását, valamint a nagy erdők összefüggőségét, determinisztikusságát, fajösszetételét, tiltott területeit és mentési beállításait.
