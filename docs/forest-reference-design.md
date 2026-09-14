# Erdőmegjelenítés – terv a 2026-09-13-i képreferenciához

Állapot: megvalósítási terv. Ez a dokumentum az új képreferencia szerinti vizuális irányt rögzíti; a korábbi forest-visual-plan.md eltérő papírdioráma-, őszi parcella- és háttérjavaslatai ehhez a fejlesztéshez nem irányadók.

## Cél és keretek

A mellékelt képhez hasonló, összefüggő, rajzolt hatású erdő: egymást takaró lombkoronák, változatos sziluettek, természetes tisztások, ritkuló szegélyek. A kép alapján megfigyelhető látványt követjük; a kép eredeti renderelési technikája nem állapítható meg biztosan.

A meglévő út- és terepmegjelenítés marad. Megmarad a forgatható kamera, a három dőlésszög, a determinisztikus szimuláció és a chunkos láthatóságkezelés. Most terv készül, nem új grafikai implementáció.

## A referencia lényeges elemei

| Látható jellegzetesség | Tervezett megoldás |
| --- | --- |
| A sűrű erdőben alig látható talaj | Átfedő koronák, összefüggő állományfoltok, sötét erdőalj |
| Gömbölyded, nyúlánk és tűszerű fák keveréke | Négy jól elkülönülő fafaji sziluett, fafajon belül több alakváltozat |
| Olívazöld és sötétzöld tömegek, világos nyírek | Fafaji paletta, állományonként összefüggő színeltérés, kevés egyedi színzaj |
| Vékony, sötét koronahatárok | Képernyőmérethez igazított, távolodással halványuló sziluettkontúr |
| Szabálytalan tisztások és erdőszélek | Több léptékű, világkoordinátás sűrűségmező és fokozatos szegélyátmenet |
| A szélén látható törzsek és kisebb fák | Változó korosztály, alacsonyabb szegélysűrűség, elszórt újulat |
| Finom belső tónusok | Kevés tónusból álló matt árnyalás; nincs erős csillogás |

A cél nem pusztán a fák számának emelése: a koronafedést, a csoportokat és a tisztásokat együtt kell kialakítani.

## 1. Grafikai irány: stilizált 3D

Elsődleges megoldás: egyszerű 3D koronamodellek, rajzolt hatású árnyalással. Ez illeszkedik a meglévő OpenGL-motorhoz, és minden kamerairánynál térbeli marad. A pusztán kamerára forduló képlapok forgatáskor és döntéskor kevésbé követnék a terepet; ezért ezek legfeljebb későbbi távoli LOD-kísérlethez tartoznak.

- Luc: keskeny, szabálytalan tűszerű forma, sötét hidegzöld korona.
- Nyír: világos törzs, nyúlánk, levegősebb, világosabb korona.
- Tölgy: széles, kerekded és enyhén karéjos korona, mély olívazöld.
- Bükk: magasabb tojásdad korona, összefüggő középzöld tömeg.
- Induló mintakészlet: fafajonként 4 koronasziluett, magasság- és szélességváltozással. A kis fák ugyanebből készülhetnek, de korfüggő arányokkal.
- Koronánként egy összefüggő külső felület; a kontúr ne rajzoljon fekete gyűrűt minden belső lebenyre.
- 2–3 széles fénytónus, visszafogott, világkoordinátához kötött felszíni színzaj. A zaj ne változzon képkockánként vagy kameramozgáskor.
- A sötét kontúr színezett mélyzöld/barna. Induló vastagság kb. 0,7–1 framebuffer-pixel, távolodással csökkentve. Ez hangolandó paraméter, nem végleges specifikáció.

Kontúrprototípus: kifelé bővített, fordított lapkivágású koronahéj, simított normálisokkal és képernyőtérhez igazított vastagsággal. A fő és kontúrpassz ugyanazt a szél-deformációt használja. Vizsgálni kell a koronák átfedésénél keletkező fekete foltokat; ha ez zavaró, a kontúrt gyengíteni vagy koronamaszk-alapú sziluettpasszra cserélni kell. Az összes poligonél kirajzolása nem része a tervnek.

## 2. Generálás: összefüggő erdőfoltok

A jelenlegi csempénkénti hash-alapú foglaltságszűrő helyére térben összefüggő mezők kerülnek. A növekedési szabályokat kezdetben nem változtatjuk meg.

1. Termőhelymaszk: víz, utak, határok és alkalmatlan területek kizárása a meglévő szabályokkal.
2. Nagyléptékű erdősültség: induló hullámhossz 12–30 csempe; ez adja az összefüggő erdőket és nagy tisztásokat.
3. Kisebb léptékű szegélyváltozatosság: 2–6 csempe; öblök, kis ligetek és kiugró facsoportok.
4. Fafaji dominancia és korosztály: külön, térben összefüggő mező. Szomszédos állományok rokon karakterűek, de nem teljesen azonosak.
5. Szegélyátmenet: jellemzően 1–3 csempén belül csökkenő koronafedés, majd néhány elszórt kis állomány.

A számok induló művészeti paraméterek. Több seed alapján kell hangolni őket; minden mező világkoordinátát és külön hash-csatornát kap, ezért chunkhatáron sem törhet meg.

A valódi fafajkeveréket első körben a szomszédos állományok mozaikja adja. Egy tölgyállományba nem rajzolunk megtévesztően nyírfákat, amíg a szimuláció és a kitermelés nem tud több fafajt kezelni ugyanazon a csempén.

Mentéskompatibilitás: a jelenlegi mentések a generált kezdőállapotból játsszák vissza a parancsokat. Új generátor csak mentett generátorverzióval vezethető be. A verzió nélküli régi mentés a régi generálást használja; külön teszt szükséges a kitermelt faanyag és visszajátszott állapot egyezésére.

## 3. Faelhelyezés és erdőalj

- A szimuláció egysége továbbra is a ForestStand. A kirajzolt törzsek vizuális példányok, nem külön szimulációs entitások.
- A fahelyek és sorrendjük seed alapján állandók. Növekedéskor új helyek aktiválódnak; a meglévő fák nem ugranak át másik cellába.
- Csempék között is ellenőrzött minimális távolság, determinisztikus prioritással. Ne jelenjen meg se csemperács, se rés a chunkok között.
- Az erdő belsejében a koronák átfednek; a szegélyen nagyobb rés marad, a törzsek és az újulat jobban látszanak.
- A törzsalap a terep tényleges háromszögén legyen, annak átlóválasztását követve. A mostani bilineáris mintavételt ehhez javítani kell.
- Erdőalj: a lombfedésből számolt sötét, tompa zöld talajtónus, lágy átmenettel; ne legyen külön-külön sötét négyzet minden csempe alatt.
- Közelnézetben kevés cserje és talajrészlet. Apró elemeket ne rendereljünk tömegesen a takart erdőbelsőben.
- Árnyalás első körben olcsó talpközeli árnyékokkal és erdőalj-sötétítéssel. Minden fára külön dinamikus árnyéktérkép nem szükséges.

## 4. Renderelés, animáció és időjárás

A már elkészült chunkcache és LOD a prototípus alapja. A végleges, sok mozgó fát kezelő megoldás közös modellkészletet és GPU-példányosítást használjon:

- Fafaj/változat/LOD szerint közös modellek, chunkonként tömör példányadatok: pozíció, magasság, szélesség, elfordulás, színeltérés, szélfázis.
- Rajzolás modellenként és látható chunkonként csoportosítva; egy fának nincs saját draw callja. Az instancing a jelenlegi legfeljebb két chunkonkénti draw callnál több hívást is jelenthet, ezért a darabszámot és a teljes frame-időt együtt mérjük.
- Szél: shaderben számolt, magasságfüggő koronahajlás, rögzített törzstalppal. Globális szélirány és erősség, térben összefüggő széllökések, fánként eltérő fázis.
- Szél és évszakváltás nem épít újra chunkgeometriát. Idő és globális paletta shaderparaméter; a lokális egészségváltozás a példányadatot frissítheti.
- Kivágás: átmenetileg külön animált példány, az állomány stabil példánylistájából eltávolítva. Csak az aktív események igényelnek CPU-állapotot.
- Eső/hó később külön, korlátozott részecskekerettel és kameraközeli térfogattal. Az erdőanyag kapjon évszak-, nedvesség- és hóparamétert, de teljes időjárásrendszer nem feltétele az első látványnak.
- Széllel megnövelt culling-határok szükségesek, hogy a mozgó korona ne tűnjön el a képernyő szélén.

## 5. Részletesség

| Nézet | Megjelenítés |
| --- | --- |
| Közeli | Teljes stilizált korona, törzs, finom kontúr, szél és néhány talajrészlet |
| Közepes, szokásos játékzoom | Egyszerűbb, de azonos méretű sziluett; kevés belső részlet, olcsóbb kontúr |
| Távoli | Koronacsoportok vagy egyszerű koronák, közös sötét erdőalj; a lombfedés és fafaji színmező megmarad |

Távolodáskor az erdő ne ritkuljon ki látványosan. Az átmenet ugyanazokat a fahelyeket és befoglaló méreteket kövesse. A meglévő hiszterézis megmarad; az összetettebb áttűnés csak szükség esetén kerül be, mert növeli az átlátszó képpontok többszörös feldolgozását.

## 6. Megvalósítási sorrend és elfogadás

### A. Vizuális mintaterület

Egy rögzített seedű, 16×16 csempés tesztterület: négy fafaj, vegyes szegély, sűrű erdőbelső és tisztás. Egyszerű új koronák, paletta, kontúr és erdőalj a jelenlegi gyorsítótárral. Ellenőrzés négy fő kamerairányban, mindhárom dőlésszöggel és három zoomon.

Kimenet: összehasonlítható játékképek, amelyek alapján a sűrűség, a kontúr és az arányok elbírálhatók. Ez a következő konkrét fejlesztési egység; a nagy renderelőátalakítás csak a látvány beállítása után következik.

### B. Természetes eloszlás és stabil elhelyezés

Verziózott foltgenerátor, koronafedés, szegélyek és terephű törzsmagasság. Tesztek: seedazonosság, chunkhatár-folytonosság, alkalmatlan területek kizárása, régi mentések visszajátszása, állandó fahelyek növekedés közben.

### C. Közös modellek és GPU-példányosítás

A mintaterület jóváhagyható látványát megtartó modellkészlet és példánybufferek. Költségkeretes feltöltés a hideg cache és zoomváltás egyszeri megakadásainak mérséklésére. Telepítés, kitermelés, terepszerkesztés és pályacsere érvénytelenítési tesztjei.

### D. Szél és távoli erdőkép

GPU-s szél, kontúr/deformáció egyezése, távoli koronacsoportok. Tesztek: stabil frame-ben nincs erdőgeometria-feltöltés; animáció nem változtatja a szimuláció vagy a mentés eredményét.

### E. Terhelés és hangolás

Rögzített kamerával 10 ezer, 50 ezer és 100 ezer látható fapéldány; külön hideg cache, nyugalmi állapot, folyamatos zoom, széles terület kivágása és sok átmeneti effekt. Mérés: CPU/GPU frame-idő, p95, draw call, feltöltött bájt, memória és allokáció. A GPU-időt időzítő lekérdezéssel külön kell mérni a CPU beadási idejétől.

Tervezési cél: a felhasználó gépén 1080p-ben 60 FPS-hez 16,7 ms teljes frame-keret, ebből kezdetben legfeljebb 4 ms GPU-keret az erdőnek. Ez mérendő cél, nem jelenleg igazolt teljesítmény. Ha nem teljesül, először a kontúr, a takart részletek és a távoli koronageometria költségét csökkentjük.

## Érintett kódrészek

- Simulation/Forestry/ForestSystem.cs: verziózott kezdőeloszlás, továbbra is állományalapú szimuláció.
- Simulation/Persistence: generátorverzió és régi mentések kompatibilitása.
- Terrain/Terrain.PropRender.cs: új koronasziluettek és stabil, terephű elhelyezés.
- Terrain/Terrain.ForestGeometry.cs: mintaterületi cache, majd példánybufferek és feltöltési keret.
- Rendering/ForestVisualState.cs: vizuális állapot, LOD és paraméterek.
- Rendering: külön erdőshader és később ForestInstanceBuffer; a terep saját shaderének viselkedése marad.
- Diagnostics és ForesTycoon.Tests: vizuális mintaterület, mérési jelenetek és regressziók.

## Fő kockázatok

Túl erős kontúr esetén a sűrű erdő fekete folttá válhat; túl sok apró tónus zajossá teszi. Az erdőszélek és utak környezetének olvashatósága a mintaterület kötelező része. A közeli erdő által takart járművek megjelenítési szabálya külön későbbi játékmeneti döntés. Az első változat prioritása a szokásos játékzoomon jól olvasható, egybefüggő lombkorona.

## A szakasz megvalósítása – 2026-09-13

Elkészült a `--forest-preview` nézet és a 36 képes export. Az új koronák egyetlen zárt felületből készülnek, fafajonként négy determinisztikus alakváltozattal. A külön erdőshader a simított normálisokat használó, fordított héjjal rajzolja a közel 0,75 pixeles kontúrt; közepes LOD-ban 0,45 pixel, távol nincs kontúr. A framebuffer 4 mintás élsimítást kér.

A koronapaletta, a lombos fák arányai és a matt tónusok a referenciához igazodnak. Lágy, talajra vetített talpközeli árnyékok sötétítik az erdőaljat, és a törzsek a tényleges terepháromszögek magasságát követik. A minta külön rögzített kezdőállapot, a normál játék generátora és mentése változatlan. A grafikai fejlesztés a normál játékban is aktív.

Az új grafikai részletesség és a külön kontúrpassz növeli a kirajzolt geometria mennyiségét; ez látványprototípus, nem az instancing szakasz teljesítményígérete. Változatlan állapotban a chunkcache továbbra sem épít új geometriát. A jelenlegi erdőalj talpközeli árnyékokból áll; a teljes lombfedési mezővel számolt, összefüggő talajárnyalás a következő fejlesztés része.

Következő lépés a B szakasz: térben összefüggő generálás, csempék közötti faelhelyezés és mentéskompatibilitás. A mintaterületi négy domináns fafaji régió a sziluettek összehasonlítását szolgálja, nem a végleges természetes eloszlás.
