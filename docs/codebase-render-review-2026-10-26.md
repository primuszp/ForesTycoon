# Kódszerkezet és renderelés – 2026. október 26.

Kiinduló revízió: `82d0130`, tiszta munkakönyvtár. A cél a párhuzamos
megvalósítások kivezetése és a felesleges renderelési munka csökkentése volt,
a modellek, LOD-határok, árnyékok és időjárási effektek megtartásával.

## Kivezetett és közösített megvalósítások

- A `ForestCrownMesh`, `SpruceCrownMesh` és `ForestTreeAppearance` régi koronaépítőit
  a forráshivatkozások alapján már csak prototípustesztek hívták. A játék a
  `DendroTreeGenerator` hálóit használja. A régi generátorok, körvonaltömbök és
  használaton kívüli modellmezők kikerültek. A tönkök, talajfoltok és időjárási
  források által használt méretek és színek megmaradtak.
- Az életfázisokhoz a játék közvetlenül az ökológiai `TreeLifeStages` szabályokat
  használja; megszűnt az ezeket továbbhívó régi megjelenítési közvetítő.
- A játék és a szerkesztő kéregmintái egyetlen `OpenGlBarkShader` forrásból
  épülnek. A játék korábbi mintái maradtak a közös változatban; a szerkesztő
  általános és vágottfelület-mintája is ezekhez igazodik. A használaton kívüli
  háromdimenziós shaderzaj kikerült.
- A GLB-konténer, a méretkorlátok, a szerkezeti ellenőrzés és az adatmezők
  eltolásai a `ForesTycoon.Models/GlbDocument` felelőssége. Az animált és a
  lapított járműmodellek ugyanazt az ellenőrzött leírást használják. A jármű
  csúcsainak olvasása közben megszűnt a JSON-adatleírás ismételt feldolgozása.
  A két importáló továbbra is a saját modellformáját állítja elő.
- A kamera és a terep közös `Engine/Graphics/IsometricView` transzformációt
  használ. A láthatóságkezelés külön `Terrain.Visibility.cs` fájlba került.

A szöveges ismétlődésvizsgálat mellett a hívási helyeket is ellenőriztem.
A backendfüggetlen szerződések és az OpenGL-megvalósítások külön rétegei
megmaradtak. Az egyező UI-rácsbejárások eltérő felületeket állítanak elő;
az ismétlődő mátrixkonstruktorok adatátalakítások, külön algoritmust nem tartanak
fenn. A vizsgálat nem bizonyítja minden lehetséges szemantikai ismétlődés hiányát.

## Renderelési változások

1. Az elfordulás és dőlésszög trigonometrikus értékei előkészítve szerepelnek.
   A térképdarab vetített határait a szélső koordinátákat adó sarkokból számoljuk,
   nyolc teljes saroktranszformáció helyett, az eredeti pontossággal.
2. A fő kamera és a kibővített árnyékterület két legutóbbi láthatósági eredménye
   újrahasználható. A kulcs az elfordulást, dőlést és a négy látómezőhatárt
   tartalmazza. A darabok befoglalói a teljes megengedett magasságot lefedik;
   terepszerkesztéskor a listák élő csempehivatkozásai érvényesek maradnak.
3. A látható darabok tagsági vizsgálata hashhalmazt használ. A lista továbbra is
   megtartja a rajzolás sorrendjét.
4. Az objektumok gömbalapú láthatóságvizsgálata előkészített kamerasíkokat használ.
   A normálhosszakat kameraváltáskor számolja; az eredeti homogén összehasonlítások
   változatlanok. A gyorsítótár renderkörnyezetenként külön él.
5. Az erdő shaderadatait programonként egy `OpenGlForestUniforms` kezeli,
   az azonnali és tárolt geometria számára közösen. A változatlan növekedési,
   évszak- és színadatokat nem küldjük újra. Programtörléskor a hozzájuk tartozó
   gyorsítótár is törlődik. A megosztott textúrakötéseket továbbra is beállítjuk.
6. A modellkontúrok a meglévő rajzolási köteg állapotmentését használják.
   Megszűnt a teljes állapot ismételt lekérdezése és egy új állapotobjektum
   létrehozása minden kontúrozott részmodellhez.

## Ellenőrzés és reprodukálás

```sh
dotnet build ForesTycoon.sln -c Release --no-restore -warnaserror
dotnet test ForesTycoon.Tests -c Release
dotnet run --project ForesTycoon -c Release --no-build -- --dendro-tree-smoke-test
dotnet run --project ForesTycoon -c Release --no-build -- --render-throughput-benchmark artifacts/render-throughput
dotnet run --project ForesTycoon -c Release --no-build -- --smoke-test
```

A kiinduló 2030 teszt sikeres volt. A régi generátorokhoz kapcsolódó 519
teszteset kikerült; az aktív generátor meglévő geometriai és életciklustesztjei
megmaradtak. Új ellenőrzések fedik a vetített befoglalókat, a vágósíkokat,
a renderkörnyezetek elkülönítését, a kamera/árnyék gyorsítótárát és a két
importáló közös hibakezelését.

A végső teljes futtatás: **1519 sikeres, 0 hibás, 0 kihagyott teszt**.
A Release-fordítás `-warnaserror` mellett is sikeres: **0 figyelmeztetés,
0 hiba**. A tesztriport: `ForesTycoon.Tests/TestResults/codebase-render-review-final.trx`.

A natív lombellenőrzés sikeres: 16 különböző fajanyag, változatlan sziluett és
mélységi lefedés, évszakos színek, egész téli lombtalanság, örökzöld tűlevelek,
egyező árnyékok és a következő nyári kép pontos visszaállása. A fényviszonyok
miatti aszinkron hálócserék után a geometria stabilan újrahasznosul.

A valódi játék 120 képkockás próbája sikeresen lefutott. A mentett képek:
`artifacts/codebase-render-review/game/`. Ez indítási és rajzolási ellenőrzés;
a teljes játékmenet kézi elfogadási próbáját nem helyettesíti.

A külön szerkesztő teljes smoke-próbája is sikeres: a faelőnézetek, módosított
faalakok, kéreg- és évszakszínek, életmenet, átlátszó koronák, szerkesztési
visszajátszás és az önálló ablak. Képek: `artifacts/rule-editor/`.

### Meglévő diagnosztikai korlátok

Két natív ellenőrzés ugyanott hibázik a kiinduló revízió külön másolatán
és a módosított kódon is:

- `--material-alpha-smoke-test`: `BLEND must mix foreground and background`.
- `--render-environment-smoke-test`: `Legacy truck retained its drawn buffer.`

Ezeket a teljes natív ellenőrzőcsomag sikeres eredményeként nem számoljuk el.
A kijelző alvó állapotában a GLFW macOS monitorlekérdezése az ablak létrehozásakor
is összeomlott mindkét változaton. Ébren tartott kijelzővel a renderpróbák
és az összehasonlító mérések végrehajthatók voltak.

## A sebességmérés értelmezése

A mérés teljesen előkészített, rögzített geometriát használ: 1304 fa,
1280×720 képpont, 4× MSAA, High grafikai minőség. Külön méri a CPU
parancskiadását és a befejezett GPU-rajzolást. A sima, napos, viharos és
25 kontúrozott modellt tartalmazó jelenetből képet is ment; a JSON tartalmazza
a csúcs-, rajzolási és effektszámokat. A háttérben befejezetlen geometria
érvényteleníti a mérést. A teszt nem tartalmaz szimulációt, HUD-ot,
dioráma-utófeldolgozást, ablakcserét vagy képkockaütemezést.

A teljes képkocka gyorsulását külön kell megítélni a CPU-munka és az
allokációk csökkenésétől. A részletes erdős jelenetekben a GPU ideje marad a
meghatározó; a későbbi GPU-optimalizálást is azonos geometriával és képi
összehasonlítással kell mérni.

### Mért eredmények: Apple M5, két elkülönített futás változatonként

Az alábbi tartományok a két futás képkocka-mediánjai. A kiinduló revízió
külön forrásmásolatába ugyanaz a mérőosztály és indítókapcsoló került be;
a mérési módszer mindkét változatban azonos. A végső mérések alatt más
tesztcsomag vagy másik renderpróba nem futott.

| Vizsgálat | Előtte | Utána |
| --- | ---: | ---: |
| 512×512-es térkép, mozgó kamera láthatóságválasztása | 0,175–0,178 ms | 0,0435–0,0441 ms |
| Ugyanez álló kamerával | 0,169–0,170 ms | 0,000115 ms |
| 25 kontúrozott modell, memóriafoglalás/képkocka | 103 384–103 448 bájt | 17 488–17 552 bájt |
| Napos jelenet, befejezett rajzolás | 17,08–17,67 ms | 17,12–17,55 ms |
| Viharos jelenet, befejezett rajzolás | 19,49–19,75 ms | 19,58–19,63 ms |
| 25 kontúrozott modell, befejezett rajzolás | 18,24–18,28 ms | 18,20–18,60 ms |

A mozgó kamerás láthatóságválasztás körülbelül **4× gyorsabb**, a kontúrozott
modellteszt képkockánkénti memóriafoglalása **83%-kal kisebb**. Az álló kamera
találatot kap a gyorsítótárból. A GPU által meghatározott teljes rajzolási idő
nem mutat egyértelmű javulást; ezekből a számokból általános FPS-növekedést
nem állítunk.

Mind a **nyolc végső előtte/utána PNG-összehasonlítás bájtra azonos**.
A csúcsszám, rajzolásszám, faszám, eső- és ködrészecskeszám, valamint a
felhőlépésszám is egyezik. A viharos jelenet 5400 csapadékrészecskét,
207 ködrészecskét és 12 felhőlépést használ mindkét változatban.

Mérési fájlok: `artifacts/codebase-render-review/{before,after}-final-{1,2}/`.
Az összesített adatok és képfájlkivonatok: `artifacts/codebase-render-review/comparison.json`.
