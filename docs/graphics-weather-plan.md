# Terepasztal-grafika és időjárás – fejlesztési terv

Dátum: 2026-10-02. Az alábbi tervhez az első grafikai megvalósítás már elkészült; az aktuális állapotot a következő rész rögzíti.

### Irányváltás – 2026-10-03

A hó egyelőre kikerült a normál játékból: a megjelenés túl egységesen fehér lett. Sem a menüből, sem az automatikus ciklusból nem kapcsolható be; a kísérleti kód belső engedélyezéssel megmarad. Az alábbi havas részek az eredeti terv és prototípus dokumentációi, nem jelenlegi játékfunkciók. Az új prioritás az eső, vihar és felhőzet; részletes kutatás és sorrend: [rain-storm-cloud-research.md](rain-storm-cloud-research.md).

### Megvalósítási állapot – 2026-10-02

Az első működő grafikai változat elkészült: kapcsolható textúrázás, külön visszaállítható eredeti renderelés, állítható irányfény és PCF napárnyék, földréteges oldalfal, nedves felületek, eső/hó részecskék, koronahó és fokozatos olvadás. A normál játék Nézet menüjében érhetők el. A textúrák jelenleg 256²-es, generált anyagrészlet-textúrák; az eredeti vertexszínek adják az anyagok alapszínét. A régi geometria CPU-oldali színárnyalása megmaradt a visszakapcsolhatóság érdekében; az új fény ehhez mérsékelt modulációt ad.

Ez a megvalósítás vizuális időjárás, nem naptári éghajlatmodell. Nincs még talajvízmérleg, növekedési módosító, fizikailag felgyűlő pocsolya, hódeformáció, évszakos lombhullás, külön környezeti háttérsík vagy volumetrikus köd. A víz esőgyűrűi shaderes közelítések. A csapadék koronatakarása csempénkénti magassági becslés; nem pontos levélütközés. Nedvesség és hófedettség globális vizuális állapot, helyi felületi maszkolással; nincs csempénként mentett éghajlati állapot. Mentés betöltése/terepcsere visszaállítja az időjárási átmeneteket; a grafikai beállítások a nyitott alkalmazásban megmaradnak.

A későbbi szakaszok az alábbi terv szerint bővíthetők. Az eredeti grafika továbbra is az eredeti shadereken keresztül működik; a textúrázás kikapcsolása az új módban a fényt és időjárást önállóan megtartja.

## 1. Javasolt látvány

Stilizált, részletgazdag erdészeti terepasztal: jól megkülönböztethető fafajok, finom talajtextúrák, meleg napfény, hűvös árnyékok, összefüggő erdőtalaj. A meglévő facsoportok sziluettjét és a terep szerkeszthetőségét meg kell őrizni. A textúra közelről anyagot adjon, távolról ne legyen zajos. Az időjárás a teljes táj fényét és felületeit alakítsa, a csapadék csak egyik összetevője legyen.

A megvizsgált `artifacts/forest-after-perf/forest-yaw-45-tilt-45-zoom5.png` exporton a fajok és koronák már felismerhetők; a tisztás egységes zöld, a rács erős, az oldalfal homogén. Ez korábbi export, nem a most futó alkalmazás képe.

| Állapot | Fény és hangulat | Felületek | Mozgás |
|---|---|---|---|
| Napos | Meleg irányfény, hűvös égboltfény, olvasható vetett árnyék | Matt fű és avar, visszafogott vízcsillanás | Enyhe lombmozgás |
| Borult | Gyengébb nap, erősebb szórt fény, kisebb kontraszt | Kevés csillanás | Széllel mozgatott felhőárnyék |
| Esős | Hűvösebb fény, gyenge árnyék, enyhe pára | Sötétedő talaj, nedves út, vízgyűrűk | Ferde esőcsíkok, ritka fröccsenések |
| Havas | Világos, szórt fény; fehérben is látható formák | Hó a talajon és felfelé néző koronafelületeken | Lassú, oldalra sodródó pelyhek |
| Olvadó | Fokozatosan melegedő fény | Foltos hó, nedves talaj, megmaradó csillanások | Csapadéktól független olvadás |

## 2. A jelenlegi motor adottságai

- C# / .NET 8 / OpenTK, OpenGL 3.3 core. A terv ezen az alapon valósítható meg, motorcsere nélkül.
- `Rendering/RenderDevice.cs`: a közös shader csak pozíciót és vertexszínt használ; nincs benne dinamikus fény vagy textúramintavétel.
- `Core/Vertex.cs` és `Rendering/VertexBuffer.cs`: normáladat és annak attribútuma már van, UV és anyagazonosító nincs. A normálok érvényességét minden geometriaépítőnél ellenőrizni kell.
- `Rendering/ForestMaterial.cs`: külön koronashader és kontúr működik, de a megvilágítás itt is a kapott színre épül. A korábbi CPU-oldali árnyalást az új fénymodellel össze kell hangolni, különben dupla árnyalás keletkezik.
- `Terrain/Terrain.TerrainRender.cs`: meglévő terepfelszín, rács és terepasztal-oldalfal. `Terrain.WaterRender.cs`: időfüggő hullámzás már van.
- `Rendering/RenderPipeline.cs` és `RenderLayer.cs`: rendezett passok vannak; árnyéktérképhez külön fénykamerás pass és framebuffer-kezelés szükséges.
- A terep és erdő chunk-cache/LOD rendszerét meg kell tartani. Az időjárás uniformokkal és külön állapottextúrával változzon, ne okozzon teljes geometria-újraépítést.
- `Simulation/FixedStepClock.cs`: fix lépéses idő. `ForestSystem.cs`: havi növekedés, alapból 30 valós másodperc egy év. Emiatt a napi időjárást nem lehet egyszerűen valós időben rákapcsolni a naptárra.

## 3. Textúrák és anyagok

Első készlet: fű, avar/erdei talaj, csupasz föld, kavicsos erdészeti út, szikla, kéreg és hó. Fűből és avarból kevés, világkoordinátás variáció elegendő. A lombkoronák első körben megtartják a geometriájukat és fajszíneiket; finom procedurális változatosságot kapnak, nem külön levelekből épülnek.

Javasolt induló textúraméret 512–1024 pixel anyagonként, ismételhető alapszínnel és érdességgel; normáltérkép csak ott, ahol a próbakép alapján segít. Színtextúrák sRGB, adattextúrák lineáris értelmezéssel. A megvilágítás lineáris térben történjen, a végső kép egyszer kapjon kijelzőre alakítást. Mipmap és elérhető anizotrop szűrés kell a ferde kameranézethez.

A talajhoz világkoordinátás XY-vetítés illik, mert a projektben Z a magasság. Meredek sziklákra és oldalfalakra külön vetítés vagy később triplanáris leképezés kell. Az anyagok átmenetét magasság, lejtés és tereptípus alapján súlyozzuk; a textúraminta ne induljon újra minden csempénél. Az erdőtalaj maszkját az állományfedettségből számoljuk és ritkán frissítjük.

Kezdetben egyszerű diffúz + szabályozott csillanás, anyagonkénti érdesség elegendő. Teljes PBR és környezeti visszaverődés későbbi, méréssel indokolt bővítés. Textúratömb használható az anyagokhoz; azonos méretű és formátumú rétegek szükségesek. A világkoordinátás talajhoz nem kötelező minden vertexbe UV-t tenni; a kéreghez és járműhöz külön UV-s formátum célszerű.

## 4. Napfény, árnyék és terepasztal

Egy világkoordinátában rögzített irányfény legyen a nap, két színnel közelített égbolt/talaj kitöltőfénnyel. Kameraforgatáskor a nap ne forduljon a kamerával. Délben tiszta formák, reggel és este hosszabb, melegebb árnyékok. Kezdetben rögzített nappali idő és kézzel állítható napirány; később külön hangulati nappalciklus.

Első árnyékmegoldás: egy ortografikus fénykamera, egy 2048² mélységi textúra és kis PCF szűrés. A lefedés a látható terephez igazodjon, de a képen kívüli, árnyékot vető fák is legyenek benne. Texelhez igazított kamera és finom lefedésváltás csökkentse a zoom/forgatás közbeni remegést. Lejtésfüggő bias kell az önárnyékolási hibákhoz. Nagy zoomtartományon csak mérés után vezessünk be két árnyékkaszkádot.

A meglévő talpközeli árnyék és az új napárnyék ne sötétítsen kétszer túl erősen. A koronák kontúrja havas/borult állapotban is a végső színhez igazodjon. A terepasztal oldala kapjon visszafogott földrétegeket; egy külön matt háttérsíkra vetett árnyék helyezze térbe a makettet.

A csapadék csak a térkép feletti térfogatban jelenjen meg. Ne essen hó az oldalfalra és ne ázzon el a felhasználói felület. A pára a tájra hat, a háttérre és az oldalfalra külön szabály szerint. A távolsági köd izometrikus nézetben féloldalasan kimoshatja a makettet, ezért gyenge magassági párát és kontrollált távolsági hatást javaslok. Alapnézetben nincs mélységélesség; külön fotómódban később használható.

## 5. Időjárási rendszer

Javasolt új elemek, még nem létező típusnevekkel:

- `Simulation/Weather/WeatherSystem`: seedelt, fix lépésű állapot, naptár és átmenetek.
- `WeatherState`: felhőzet, csapadékintenzitás, hőmérséklet, szélirány/sebesség, állapotváltás célja és ideje.
- `SurfaceClimateState`: csempénként talajnedvesség és hóvízegyenérték; szükség esetén külön vizuális hófedettség.
- `Rendering/WeatherRenderer`: eső/hó részecskék és becsapódási jelek.
- `LightingState` és anyag-uniformok: közös fény-, szél-, nedvesség- és hóparaméterek a terep, korona, jármű és víz számára.
- Kis felbontású állapottextúra: nedvesség/hó/fedettség, részleges feltöltéssel, külön a statikus mesh-cache-től.

Állapotmenet: napos ↔ borult ↔ eső; hideg és megfelelő évszak esetén havazás. Az átmenetek interpoláltak: előbb a fény és felhőzet változik, aztán a csapadék; utána a nedvesség és hó tovább megmarad. Első körben a teljes terepasztal egy időjárási régió, később lehet térbeli csapadékmező.

Két időlépték szükséges. A naptári modell havi/évszakos csapadék- és hőmérsékletösszegekkel dolgozik. A grafika ezekből lassabban változó, több másodpercig jól látható jeleneteket képez, hogy a 30 másodperces év ne eredményezzen villódzó évszakokat. Ez tudatos időbeli stilizálás: a felület állapotának és az erdő növekedési hatásának forrása továbbra is a szimuláció. A fejlesztői preview külön rögzíthet időjárást és időpontot.

Mentés/visszajátszás: seed, generátorállapot vagy seed+lépésszám, aktuális/cél időjárás, naptár, nedvesség és hó tárolása. Új modellverzió szükséges; régi mentés alapértelmezett időjárást kapjon és a korábbi szimulációs eredményeit őrizze meg. A részecskék nem játékmeneti objektumok, nem kell egyenként menteni őket. Szünetben az éghajlati állapot nem változik; a preview opcionálisan külön animálható.

## 6. Eső

Világkoordinátás, a látható terep körül újrahasznosított részecsketérfogat. Vékony, enyhén áttetsző, szélirányba dőlő billboard-csíkok; instancing és vertex shaderből számolt mozgás, compute shader nélkül. A kamerát követő lefedés határát puhán kell változtatni, a részecskék ne tapadjanak a képernyőhöz.

Normál mélységteszt, kikapcsolt mélységírás; a részecskék ne világítsanak át a fákon. A talaj/víz magasságán megszűnnek, koronákhoz kezdetben alacsony felbontású fedettségi/magassági maszk elegendő. Ez közelítés, nem pontos levélütközés. A részecskevéletlen külön legyen a szimuláció véletlenétől.

Nedvesség hatására a föld és kéreg sötétedik, az utak visszafogottan csillannak. A fű csak kevéssé legyen fényes. Pocsolya a mélyedésekben és keréknyomokon jelenjen meg, ne egyforma fényes réteg borítson mindent. A vízen ritka, összevont hullámgyűrűk jelezzék az esőt. Távoli zoomnál kevesebb csík, a felületi és fényváltozás maradjon domináns.

Az NVIDIA esőtanulmánya részecskékből és nézet/fényfüggő csíkokból épít esőt, és rámutat a képernyőre helyezett textúrák gyenge mélységérzetére. Itt ezt egyszerűbb, OpenGL 3.3-as instanced megoldásra adaptáljuk; a Direct3D mintakódot nem vesszük át.

## 7. Hó és olvadás

A havazás és a hótakaró külön állapot. A hópelyhek lassabban esnek, különböző méretűek, széllel és kis örvényléssel sodródnak. A távoli nézetben pelyheik száma és mérete korlátozott, hogy ne takarják el a gazdálkodási információt.

Első változatban shaderes hóanyag-keverés. A maszkot a világkoordinátás normál Z-komponense, helyi zaj, hóállapot és koronafedettség szabályozza. A törzs és meredek oldalfal nem lesz fehér. Fenyőn a felfelé néző ág-/koronaszintekre, lombos fán a felső koronarészekre ül a hó. Ez a meglévő koronageometria vizuális közelítése. Lombhullás külön későbbi fejlesztés, geometria- és LOD-tervezést igényel.

Az erdő alatti hó kevesebb és késleltetett, a tisztáson összefüggőbb. Hőmérséklet és besugárzás hatására foltosan olvad; olvadáskor nedvességet ad a talajhoz. Kezdetben csak anyagváltozás: a hó nem emeli meg az utat és nem változtatja a járművek vezetőfelületét. Hógeometria, nyomok és hókotrás későbbi bővítés. A GDC deformálhatóhó-előadása ilyen későbbi irányt mutat; a jelenlegi maketthez elsőként egyszerű fedettségi maszkot javaslok.

## 8. Kapcsolat az erdőgazdálkodással

Az első grafikai mérföldkő csak látványt ad. A következő szimulációs lépésben a havi vízmérleg összeköti a csapadékot, párolgást, talajnedvességet és olvadást. A fajok meglévő termőhelyigénye ezekből kap módosítót; a látvány ugyanebből az állapotból olvas. Szárazság, tartós víztöbblet és hideg hatása hangolható, tesztelhető paraméter legyen.

Később nedves földutak sebességkorlátozása, fagy és szezonális munkatervezés vezethető be. Ezek külön játékmeneti döntések; az első grafikai változat nem módosítja automatikusan a fakitermelést vagy szállítást.

## 9. Megvalósítási sorrend és készültségi feltételek

| Szakasz | Munka | Akkor kész, ha… |
|---|---|---|
| 0. Referencia és mérés | Rögzített tisztás, sűrű erdő, út, folyó, lejtő; azonos kameraképek és baseline | A jelenlegi kép és teljesítmény összehasonlítható |
| 1. Anyag és napfény | Normálok auditja, lineáris színkezelés, talajanyagok, korona/törzs/jármű világítás | Nincs dupla árnyalás, textúravarrat vagy zoom közbeni vibrálás |
| 2. Árnyék és víz | Napárnyék, háttérsík, víz normal/csillanás, halványítható rács | Forgatás/zoom stabil, vízpart és koronák helyesek |
| 3. Eső | Kézi napos/borult/esős preset, csapadék, nedvesség, hullámgyűrű | Átmenet folyamatos; eső után marad nedves felszín |
| 4. Hó | Havazás, hómaszk, koronahó, olvadás | Nincs hó oldalfalon/törzsön/vízfelületen; a hó nem tűnik el azonnal |
| 5. Szimuláció | Seedelt évszakos modell, vízmérleg, mentés és erdőkapcsolat | Azonos seed/parancsok azonos eredményt adnak; régi mentés kompatibilis |
| 6. Hangolás | Minőségi profilok, vizuális összevetés, kamera- és időjárásbenchmark | A választott célhardveren teljesül a mért frame-keret |

Az első bemutatható csomag az 1–2. szakasz: textúrázott, napsütötte terepasztal. Utána eső, majd hó. Minden szakasz saját preview-presettel legyen ellenőrizhető.

Javasolt renderfolyamat: nap mélységi pass → átlátszatlan terep/fák/járművek → áttetsző víz és csapadék a szükséges mélységi sorrenddel → kijelölések → UI. A mostani víz-először rétegrendet az áttetsző víz és objektumok közös tesztjével kell felülvizsgálni. MSAA mellett a részecskékhez olvasható mélység külön resolve/másolatot igényelhet; kezdetben hagyományos depth test használható, soft particle később.

## 10. Teljesítmény és ellenőrzés

Javasolt cél: a meglévő 30 FPS / 33,3 ms keret megtartása, erősebb gépen 60 FPS. Kiinduló időjárási GPU-keret normál profilon 2–4 ms; ez célérték, nem mérés. GPU-időmérés nélkül CPU frame-adatból nem állapítható meg az effektek tényleges GPU-költsége.

Alacsony profil: 1024² árnyék vagy meglévő talpárnyék, kevesebb részecske, egyszerű víz. Normál: 2048² árnyék, nedvesség/hómaszk, mérsékelt csapadék. Magas: több becsapódás, finomabb víz és opcionális második árnyékkaszkád. Induló részecskekeret 1000–4000 látható elem, de a végső számot a képernyőfedés és a GPU-mérés döntse el. A nagy áttetsző felületek túlfestése veszélyesebb lehet, mint önmagában az elemszám.

Statikus időjárás ne építse újra a terep vagy erdő VBO-it. A meglévő cache/LOD-smoke teszt és kamerabenchmark maradjon érvényes. Bővített mérés: 64×64 világ, sűrű fixture, folyamatos kameraforgatás/zoom, erős eső/hó, hónapváltás és terepszerkesztés. Rögzítendő: median/p95/max frame-idő, GPU-passidő, draw call, feltöltött bájt és mesh-rebuild szám; az induló betöltés külön mérendő.

Vizuális ellenőrzés azonos kameraállásokon: napos, borult, esős, frissen havas, olvadó; közel/távol és több forgatás. Ellenőrizni kell a textúrák skáláját, árnyékremegést, kontúrokat, csempeszéleket, víz áttetszőségét, csapadék takarását és kijelölések olvashatóságát. Szimulációs tesztek majd az új logikához: seedelt visszajátszás, mentés/load, szünet, időgyorsítás, vízmérleg és hóolvadás.

## 11. Kutatási források és korlátok

- [Khronos: Texture Shader Binding](https://wikis.khronos.org/opengl/Example/Texture_Shader_Binding): textúrák és shadow sampler bekötése. A konkrét rendererintegráció saját tervezési javaslat.
- [NVIDIA: Rain, Sarah Tariq, 2007](https://developer.download.nvidia.com/SDK/10.5/direct3d/Source/rain/doc/RainSDKWhitePaper.pdf): GPU-részecskék, szél, nézet- és fényfüggő esőcsíkok. Régi Direct3D technikai minta; a vizuális elveket használjuk.
- [NVIDIA GPU Gems: Effective Water Simulation from Physical Models](https://developer.nvidia.com/gpugems/gpugems/part-i-natural-effects/chapter-1-effective-water-simulation-physical-models): összegzett hullámfüggvények és finom normal-map részletek a vízen. Terepasztalhoz kicsi amplitúdó javasolt.
- [GDC: Deformable Snow Rendering in Batman: Arkham Origins](https://gdcvault.com/play/1020379/Deformable-Snow-Rendering-in-Batman): további kutatási irány hódeformációhoz. A keresés igazolta az előadás létezését, de a teljes előadás tartalmát nem vizsgáltam; a hómaszkterv saját javaslat.

A terv elkészítésekor a repository kódját és a fenti szakmai forrásokat vizsgáltam. Az első megvalósítás összehasonlító képei az `artifacts/graphics-weather` könyvtárban találhatók. A grafikus smoke teszt ellenőrzi a megjelenítési kapcsolókat, az eredeti kép visszaállítását és a cache megtartását; az egységtesztek a csapadékátmenetet, száradást, olvadást és szünetet is vizsgálják. A nagyobb térképek részletes CPU/GPU-időjárásprofilozása továbbra is külön feladat.
