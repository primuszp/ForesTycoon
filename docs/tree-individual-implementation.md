# Faegyedek – első megvalósítási lépcső

Állapot: megvalósítva, 2026-10-03. Kapcsolódó terv: [tree-individual-lifecycle-plan.md](tree-individual-lifecycle-plan.md).

## Működő részek

Minden világban minden megjelenített fa saját `ForestTree` állapotot kap: stabil azonosító, faj, csempén belüli hely, alak-seed, születési idő, átmérő, magasság, koronasugár, egészség és növekedési horgony/ráták. A `ForestTreeStore` csak a foglalt csempékhez allokál tömböt; az egyedek nem külön managed objektumok. A túlélő fa helye és alak-seedje nem változik kitermeléskor.

A havi ökológiai lépés lezárja az előző növekedési intervallumot, közös állománypillanatképből számolja a versengést, majd új rátákat ad. A faj, termőhely, fény, vízstressz, egészség és szezon befolyásolja ezeket. Az eltelt hónaprészben a `ForestTree.At` folytonosan értékeli a méretet. Érettség után a vastagodás nem áll le. A magasság a faji burkolóhoz közelít. A paraméterek első játékbeli közelítések; nem hitelesített erdészeti növekedési táblák.

A zárult erdőév bruttó törzstérfogat-növedékét elszámoljuk. Az Erdészet ablak mutatja a faegyedek számát, a havi összesítésű élő törzskészletet és az előző évi növedéket. A csempelekérdezés és a kitermelési készlet az aktuális, hónapon belüli méretet használja. A hagyományos `ForestStand` minden világban összesített nézet; egy csempén az első változat még egyetlen fafajt támogat.

A kirajzolás a tényleges egyedeket olvassa. Külön törzsvastagság, törzsmagasság és koronaméret készül; a lombos fajok eltérő koronahossz/magasság arányokat kapnak. A fizikai méret és a dioráma-skála elkülönül. A fizikai átmérő jelenleg törzsátmérő-közelítés; a teljes DBH/gyökérnyak-törzsprofil későbbi kalibráció.

Hónapon belül nincs mesh-újraépítés: megtartott chunkos VBO-khoz külön növekedési attribútumok tartoznak. A shader a gyökérpont körül külön függőleges és vízszintes növekedést alkalmaz, ugyanazon eltelt erdőidővel a textúrázott, eredeti színalapú, kontúr- és árnyékpassban. A gyökérpont rögzített; a tönk növekedési rátája nulla.

A részrakodás egész faegyedeket vág ki és átadja a teljes törzstérfogatot a csempe rönkdepójának. A teherautó csak a kért mennyiséget veszi át, a maradék a depóban marad. A túlélő fák nem zsugorodnak. A tönk a kivágott fa pontos helyét és kivágáskori méreteit őrzi, és újratelepítéskor megmarad a korhadási idő végéig.

## Mentés

Minden világ az egyedi famodellt használja. A mentés egyetlen formátumverziót (`Version = 3`) tárol; nincsenek külön erdő-, környezet-, logisztika- vagy járműverzió-kapcsolók. Korábbi mentésformátumhoz nincs migráció vagy tartalék szimulációs ág. Az aktuális formátum betöltése minden rendszert azonos módon indít. A tartós telepítési területek és a versengés részletei az [erdődinamika leírásában](forest-plantation-dynamics.md) szerepelnek.

A mentés egyelőre továbbra is seedből és ticknaplóból játszik vissza. Ebből az egyedazonosítók, méretek, növekedési ráták és rönkdepók determinisztikusan újraépülnek azonos build mellett. Önálló állapot-snapshot és hosszú játékhoz checkpoint ebben a lépcsőben még nem készült. A régi környezeti állapotot új térkép létrehozása előtt leválasztjuk, hogy ne befolyásolhassa a kezdeti növekedési rátákat.

A ködforrások, villámcsapási koronapontok és az állatok szabad helyei is a tényleges faegyedeket használják; nem generálnak külön látványpopulációt.

## Ellenőrzések

```powershell
dotnet test ForesTycoon.sln --no-restore
dotnet run --project ForesTycoon/ForesTycoon.csproj --no-build -- --tree-growth-smoke-test
dotnet run --project ForesTycoon/ForesTycoon.csproj --no-build -- --forest-smoke-test
dotnet run --project ForesTycoon/ForesTycoon.csproj --no-build -- --logistics-smoke-test
```

A headless tesztek a folytonos méretet és havi határt, érett fák vastagodását, éves növedéket, stabil azonosítókat/helyeket, részrakodási anyagmérleget, eltérő képkockahosszokat, újratelepítést, tönkméretet és az egyedek determinisztikus újragenerálását vizsgálják.

Az új OpenGL-próba stabil és hónapon belüli frame-en nulla rebuildet vár. Átmenetileg felnagyított növekedési rátával a valódi framebuffer képpontjainak változását is ellenőrzi, ezért a CPU-méret változása önmagában nem elég a teszt sikeréhez. Havi lépést átlépő újvilág-mentés/visszajátszás, friss mentés és újragenerálás is szerepel benne. A próba három PNG-t ment az `artifacts/tree-growth` könyvtárba. A normál játék képei az `artifacts/tree-individual-world` mappába exportálhatók a `--capture-frame` kapcsolóval.

## Következő lépcsők és jelenlegi korlátok

- Az egyedi modellben az öregedés, természetes halál, álló/fekvő holtfa és kidőlés még nincs bekapcsolva. A következő életciklus-fejlesztés az egészségromláshoz stresszmemóriát és tényleges maradványállapotokat ad; nem törölheti közvetlenül az egyedet.
- A korona most a meglévő procedurális felületet használja, javított faji arányokkal. Az új vázág- és lombtömeg-modellek, részleges koronaszáradás és lombfenológia a következő grafikai szakasz.
- Az első új renderer Near részletességet használ. A növekedési adat csúcsonként ismétlődik; ez tudatos átmenet a későbbi instance-buffer felé, nem kész instancing.
- Havi állapotváltáskor és helyi szerkesztéskor az érintett chunk újraépítése szinkron. A korábbi állományszintű renderer és építési queue eltávolítva. Nagy térképre ezt tovább kell fejleszteni és mérni; a stabil frame-ek nulla rebuildje nem bizonyítja a havi csúcsok költségét.
- Az újulat jelenleg üres élő állományú csempén jelenhet meg. Vegyes fajok, élő fák közötti újulat, pontos lokális fényversengés és holtfa-ökológia még külön fejlesztés.
- A kitermelhető készlet a világokban fizikai törzstérfogatból származik, ezért eltérhet a korábbi biomassza×100 készlettől. A faji alakszám most egységes játékbeli 0,45; gazdasági és erdészeti hangolás szükséges.

A következő konkrét feladat az egyedi életciklus tárolása és menthető álló holtfa, majd az ezekhez tartozó száraz ágmodellek és kidőlés. Ezt a már működő egyedi növekedési és kitermelési alapra kell építeni.
