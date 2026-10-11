# Közös szabályprojekt és rendszerdinamika

A játék, a grafikus editor és az AI ugyanazt a `forest-rule-project` JSON-dokumentumot
használja. A projekt együtt tartalmazza az útkopási gráfot (`road`), a hangolási
értékeket (`tuning`), az objektumokhoz kapcsolható képlet- és gépvezérlési gráfokat
(`behaviors`), a készleteket és áramlásokat (`dynamics`), valamint a teljes
algoritmusgráfok ellenőrzött csomagjára mutató hivatkozást (`algorithms`).

## Indítás és mindennapi szerkesztés

```powershell
dotnet run --project ForesTycoon
dotnet run --project ForesTycoon.Editor
```

A közös fájl alaphelye `%LOCALAPPDATA%/ForesTycoon/rules/project.json`.
A `FORESTYCOON_RULE_PROJECT` környezeti változóval mindkét program ugyanahhoz a másik
projekthez kapcsolható. A játék első használatkor az aktív világ szabályaiból hozza
létre a hiányzó fájlt; az editor induláskor a létező közös projektet nyitja meg.
A normál játék első használatkor a munkaterület teljes alap-algoritmusait is
gráfokba olvassa és lefordítja. A régi, `algorithms` nélküli projekteket ugyanígy
kiegészíti. Az editor a hiányzó algoritmusokat szerkesztési példányként olvassa be.

Az editor tematikus **Szabályműhely** felületének használatát az
[editor útmutató](rule-editor.md) írja le. A **Mentés / Ctrl+S** kizárólag külön
munkapéldányt készít; nem publikál a játékhoz. A **4. Alkalmazás → Alkalmazás
a játékban** ellenőrzi és atomikusan közzéteszi a teljes projektet.
A képlet- és dinamikai változásokat a tesztvilágban is alkalmazza.
A teljes algoritmusgráf-változás előbb háttérben fordul; csak sikeres fordítás után
kerül a közös fájlba. A futó játék fél másodpercenként
ellenőrzi a fájlt. Egy változás egyetlen `SetRuleProject` parancsként kerül a
parancsnaplóba; a szabályok, hangolás és dinamika együtt lépnek érvénybe a következő
parancsvégrehajtási határon, szüneteltetéskor is. A hibás fájl nem módosítja az aktív
szabályokat, a betöltési hiba az F12-panelen és az editor kapcsolatjelzésében látható.
Javítás után a kapcsolat magától újrapróbálkozik.

A **Próba → Helyi tesztvilág és gyors ellenőrzés → Projekt alkalmazása csak a
tesztvilágban** csak az elkülönített tesztvilágot módosítja.
A **Mentés** a `.draft.json` példányt írja: hiányzó képletkapcsolatokkal
is menthető és újranyitható, a játékhoz kapcsolt fájl változatlan marad.
A hangolásoknak és az alap dokumentumszerkezetnek megnyitáskor is érvényesnek kell lenniük.

## Objektumok a játékból

A játék három másodpercenként publikálja a `project.json.world.json` katalógust.
Ez a tényleges világ fa-, út-, csempe-, malom-, forwarder-, processzor- és
teherautó-, vad-, telephely- és rakat-azonosítóit, a támogatott folyamatokat, összesített világmezőket és
az aktív dinamikai készleteket és a tényleges projekt tartalmi lenyomatát tartalmazza.
Az editor az **Objektum kiválasztása** munkatérben
kereshető listát mutat. Az első 200 keresési találat látható, a keresés az egész
katalógusban dolgozik. Leállított játéknál a legutóbbi katalógus megmarad, a
kapcsolatjelzés 15 másodperc után jelzi a szünetelő kapcsolatot.

Az objektum kijelölése után az **Egyedi felülírás létrehozása és szerkesztése** az adott folyamat
egyedi gráfját nyitja meg. Ha még nincs ilyen, a típusszintű gráfból készül
független másolat. Az egyedi gráf helyettesíti a típusszintűt; a típusszintű
szabály az újonnan létrejövő objektumokra is érvényes. A közös **Szerkesztés** lépésben
csomópontok, kapcsolatok, feltételek és kimenet, továbbá gépvezérlési állapotok
és átmenetek szerkeszthetők. Van húzás, vászonmozgatás, nagyítás, visszavonás,
újra és próbaszámítás.

A **Világ beolvasása** valódi játékmentést tölt a tesztvilágba, az eredeti
objektumazonosítókkal. A **Világ szabályainak átvétele** külön művelet: egy világ
beolvasása nem írja felül a szerkesztés alatt álló projektet. Ugyanez indításkor:

```powershell
dotnet run --project ForesTycoon.Editor -- --world mentés.json --project projekt.json
```

A **Tesztvilág mentése** a megadott játékmentés-útvonalhoz `.editor.json` utótagot
fűz, így a betöltött játékmentés külön megmarad.

Az objektum-ID csak a hozzá tartozó világban értelmezhető. Más seed vagy egy
újonnan generált világ objektumait ne tekintsük az előző világ egyedeinek.
Az élő katalógus megfigyelés, nem teljes világmentés; a tesztvilág nem követi
automatikusan a futó játék összes állapotváltozását. A teszt és a játék külön
futtatja a közös projektet.

## Készletek, áramlások, visszacsatolás

A **További eszközök → Saját készletek és áramlások** alatt egy készletnek stabil ID-ja, neve, egysége, kezdőértéke,
minimuma és maximuma van. Egy áramlásnak forrása, célja és szerkeszthető
rátaképlete van. Az üres végpont a környezet: külső forrás vagy nyelő.
Két készlet közötti átadásnál az egységeknek egyezniük kell.

Az áramlás gráfja `flow.<áramlás-ID>` kötésű, `dynamics` típusú képlet.
Az áramlási ráta **készletegység / szimuláció-másodperc**; nem erdőév és nem
járműmásodperc. A `Delta` az aktuális lépés hossza, a `Time` az eltelt
szimulációs idő másodpercben. A gráfkimenet 0–10¹² közé korlátozott ráta.

Az új **Készlet / világmező** (`NamedValue`) csomópont `key` mezője például
`stock.wearMultiplier`, `world.rain`, `world.treeCount` vagy `world.balance`.
A fordító csak az adott projektben létező készletet és a játék által deklarált
világmezőt engedi. Ugyanez a csomópont a viselkedési képletekben is használható,
így a saját dinamika közvetlenül szabályozhatja például a növekedési tényezőt,
az útkopást, a rakodást vagy a gépek tempóját. A világmezők megfigyelések;
`world.time` erdőév, a többi mező a játék eredeti egységében értendő.
A képletek nem végeznek automatikus mértékegység-átváltást.

Minden fix tick elején az összes áramlás az előző készletállapotból és a tick
előtti világmezőkből számol. A megengedett átadások egyszerre kerülnek a
készletekbe, utána fut az ökológia, logisztika és a többi játékrendszer.
Ez valódi, tickkel késleltetett visszacsatolást enged, miközben egy képleten
belüli azonnali kör továbbra is hibás.

A közös forrásért versengő kiáramlások arányosan osztoznak az előző tickben
rendelkezésre álló készleten; a közös célt feltöltő áramlások arányosan
osztoznak annak szabad kapacitásán. Egy átadás mindkét végpont korlátozását
figyelembe veszi. Az ebben a tickben érkező anyag még nem adható tovább,
és az ebben a tickben felszabaduló kapacitás még nem tölthető fel újra.
Ez konzervatív, determinisztikus integráció, nem maximális átfolyást kereső
optimalizáló. A forrásból elvett mennyiség pontosan a célhoz adott mennyiség,
a lebegőpontos kerekítés szokásos pontosságán belül.

Számítási hibánál a dinamika teljes tickje megtartja a korábbi készleteket,
és hibát jelez. A viselkedési képletek a meglévő biztonsági szabály szerint
az adapter beépített eredményére térnek vissza, diagnosztikával. A készlet
állapota projektcserekor azonos ID és egység mellett megmarad, az új
határokhoz korlátozva; új készlet a kezdőértékével indul.

## AI-ügynök felülete

Ezek a parancsok grafika nélkül, ugyanazzal a fordítóval és GameWorlddel futnak:

```powershell
dotnet run --project ForesTycoon.Editor -- --new-project projekt.json
dotnet run --project ForesTycoon.Editor -- --export-rule-api rule-api.json
dotnet run --project ForesTycoon.Editor -- --export-world mentés.json objektumok.json
dotnet run --project ForesTycoon.Editor -- --validate-project projekt.json
dotnet run --project ForesTycoon.Editor -- --simulate-project projekt.json 300 eredmény.json
dotnet run --project ForesTycoon.Editor -- --simulate-project projekt.json 300 eredmény.json --world mentés.json
dotnet run --project ForesTycoon.Editor -- --compare-project projekt.json.draft.json 300 próba-mappa --world mentés.json
dotnet run --project ForesTycoon.Editor -- --project-file-revision projekt.json
# A kiolvasott fájlrevízióval (új fájlnál: missing):
dotnet run --project ForesTycoon.Editor -- --publish-project projekt.json.draft.json projekt.json --expected <fájlrevízió>
```

Az API-leírás tartalmazza az összes támogatott kötést, korlátot, bemeneti
jelentést, ütemezést, műveletet és egy alapprojektet. Az ügynök szerkeszti a
JSON-t, ellenőrzi, teszteli, majd atomikus fájlcserével a közös projektútvonalra
menti. Nincs tetszőleges C#- vagy szkriptvégrehajtás a gráfban.
A szimuláció 30 Hz-en fut, másodpercenként és a végén készletidősort exportál,
a végső világkatalógussal együtt. Hiba esetén a parancs hibával tér vissza.

A validálás, fordítás és összehasonlítás az editor `draft` algoritmushivatkozását
is kezeli: külön, immutábilis futtatási példány készül, az aktív projekt változatlan
marad. A publikálás fordítás után ismét ellenőrzi a célfájl revízióját. Eltérésnél
nem publikál. A revízió itt a fájl bájtjainak SHA-256 lenyomata; külön fogalom a
játék által visszaigazolt, normalizált runtime projekt tartalmi lenyomatától.

A projekt `authoring` mezőjében közös témabesorolások, jegyzetek és sablonok
tárolódnak. Az AI ezeket ugyanazzal az ID-val szerkeszti, mint a felület.
A szerkesztői metaadatot a runtime nem hajtja végre és kihagyja saját lenyomatából.

Egy verziózott, betölthető példa: [`feedback-project.json`](../ForesTycoon.Rules/Examples/feedback-project.json).
Ebben az útkopási szorzó saját készlet, amely 0,1/s sebességgel nő, és az
útkopás képlete a pillanatnyi készlettel szorozza az adapter eredményét.
300 tick után a szorzó 1-ről 2-re nő; a modell az editorban tovább szerkeszthető:

```powershell
dotnet run --project ForesTycoon.Editor -- --project ForesTycoon.Rules/Examples/feedback-project.json
```

## Futtatási és mentési szerződés

A függőség iránya `Editor -> játék -> Rules`. A játék nem hivatkozik az editor
UI-jára. A GameWorld nem olvas fájlt szimulációs tick közben: a `RuleProjectLink`
a játékablakban végzi a fájlfigyelést és a katalóguspublikálást. A headless
tesztek és a visszajátszás ettől függetlenek.

A projekt minden támogatott folyamatához explicit típusszintű gráfot fordítunk.
A változatlan alapszabály egy `Native` csomópont; ezt lehet lecserélni saját
képletre. A futtató független, ellenőrzött pillanatképet használ. A dokumentum
utólagos módosítása nem változtatja meg az aktív világot.

A 17-es mentés a teljes aktív dokumentumot, projektnevet és dinamikai készleteket
is rögzíti. A checkpoint utáni függő projektváltás függőben marad. Checkpoint
nélküli visszajátszáskor a naplóban rögzített projektváltások az eredeti tickben
történnek; a külső fájl nem szól bele. Az élő játékban betöltés után a közös
projekt ismét naplózott váltással kerül alkalmazásra. A 4–16-os mentések
továbbra is betölthetők, hiányzó dinamika esetén üres készletmodellel.

## Teljes algoritmusgráfok

A szabály **Szerkesztés** lépésének belső algoritmuskötése, illetve a
**További eszközök → Összes belső algoritmusgráf** a játékmenet teljes belső szerkezetét nyitja meg:
ökológia, erdőbiológia, klíma és víz, vadak, terep, járműfizika, útkeresés,
logisztika, pénzügyek, mentési eljárások és a kapcsolódó könyvtárak algoritmusai.
A talajprofilok és a klímadefiníciók is adatgráfok. Nincs forráskódszerkesztő.
A fordító a szerkezeti gráfokból állítja elő a programot; a teljes
algoritmuscsomagból nem hiányozhat a munkaterület egyik játékmeneti fájlja sem.
Az editor, a fordító, a programindítás és a projektkapcsolat képezi a stabil
infrastruktúrát. Ezek nem szerkeszthető játékszabályok.

A fájlok és eljárások kereshetők; az áttekintés forráshivatkozásánál az
**Algoritmusgráf szerkesztése** közvetlenül az eljárás blokkjaiba lép.
Az aktív fájl csomópontjai ID, művelet és érték szerint is kereshetők.
Dupla kattintás bontja ki a részgráfot, **Vissza** lép a szülőhöz. Középső
egérgomb mozgatja a vásznat, görgő nagyít, a csomópontok húzhatók.

A terminálok egyetlen számot, szövegértéket, nevet vagy operátort tartalmaznak.
Az összetett csomópontok rendezett portjai fejezik ki az operandusokat,
feltételt, ciklustörzset, hívásparamétereket és az utasítások sorrendjét.
A **Kapcsolat forrásának kijelölése**, majd **Csere** vagy **Forrás csatolása**
módosít kapcsolatot. Blokknál a csatolás a zárójel elé teszi az utasítást.
Portot fel/le mozgatni és törölni is lehet. A **Részgráf másolása** új,
független azonosítókat hoz létre. Sablonok vannak feltételhez, ciklushoz,
bejáráshoz, értékadáshoz, híváshoz, változóhoz és képlethez; az összes további
szerkezeti művelet külön is létrehozható. Visszavonás és újra fájlonként működik.
Az adatgráfok mezői és skalárjai szerkeszthetők, részgráfjaik másolhatók.

Ezek a gráfok közvetlenül módosíthatják a valódi objektumállapotokat és belső
algoritmusokat. A könnyű, egyedi képletkötésekhez továbbra is a 72
`BehaviorHook` használható; a teljes algoritmusok szerkesztését nem korlátozza
ez a lista. Vadaknál, telephelyeknél és rakatoknál az objektumböngésző közvetlen
algoritmusgráf-gombot ad. Egyedi belső algoritmushoz az eljárásban hozzáférhető
objektumazonosító feltételként használható.

### Fordítás és állapotátadás

A `forest-algorithm-graphs` csomag fájlonként stabil csomópontazonosítókat és
rendezett kapcsolatokat tárol. A fordítás hiányzó kapcsolatot, ciklust,
szintaktikai és típushibát is jelez. A csomag SHA-256 revízióját az előállított
algoritmusok határozzák meg; a vászonpozíció nem változtatja a futtatási revíziót.
A mentett csomag neve a szerkesztési tartalom lenyomatát is tartalmazza, ezért
a pozíciók megmaradnak, és a korábbi csomagok változatlanok maradnak.

Fordításhoz a teljes ForesTycoon munkaterület és .NET SDK kell. A generált
források, erőforrások, obj/bin állományok és napló a
`%LOCALAPPDATA%/ForesTycoon/algorithm-runtime` külön mappájába kerülnek.
A munkaterület eredeti fájljai és az editor futó binárisai nem változnak.
A gyorsítótár a grafikus algoritmusrevíziót és az infrastruktúra tartalmát
együtt figyeli. Azonos revízió párhuzamos fordítását fájlzár sorosítja.

A játék sikeres fordítás után végrehajtja a függő parancsokat, aktuális
checkpointot ment, majd betöltésre elindítja az új futtatót. Az átadás idejére
a régi világ szimulációja és kezelőszervei szünetelnek. Az új játék a
projektet alkalmazza, előkészíti a pályát, és nyugtázza a betöltést. Csak ekkor
zárul be a régi játék. Fordítási vagy betöltési hiba esetén a régi játék marad.
Az eredeti checkpoint külön fájlként megmarad. A mentéshez csatolt algoritmus-
revízió eltérését a szokásos betöltés elutasítja; tudatos algoritmusváltás csak
aktuális, függő parancs nélküli checkpointból migrálhat, korábbi tickeket
eltérő algoritmussal nem játszik újra. A változtatásnak kompatibilisnek kell
lennie a mentett állapottal; új állapotsémához saját gráfos migráció szükséges.

A beépített editor-tesztvilág az editorhoz fordított algoritmusverziót futtatja.
Más algoritmusrevízió teljes próbáját a külön gráffuttató és a CLI szimuláció
végzi. Az editor **Próba → Összehasonlító próba indítása** ugyanazt a futtatót indítja,
választható tick-számmal, a tesztvilág pillanatfelvételéből vagy a megadott
játékmentésből. Két futás indul ugyanabból a checkpointból: az alapváltozat
annak aktív projektje, a másik a munkapéldány pillanatfelvétele. Mindkettőhöz
idősor, végső világkatalógus és revízió tartozik; a `comparison.json` a közös
mérési mezők alapértékét és szerkesztett értékét tárolja. A felület témánként
szűrt vagy teljes eredménytáblát és külön eltérést mutat. A próba fájljainak
helye a Próba részleteiben látszik. A mérés nem lépteti előre az editor világát.
A fordítás nem garantálja egy saját algoritmus biológiai vagy fizikai
helyességét; a szimuláció eredményeit is ellenőrizni kell.

### AI és parancssori használat

```powershell
dotnet run --project ForesTycoon.Editor -- --new-project projekt.json
dotnet run --project ForesTycoon.Editor -- --export-algorithm-graphs C:/Projects/ForesTycoon algoritmusok.json
# Az AI a csomópontokat és kapcsolataikat szerkeszti a JSON-ban.
dotnet run --project ForesTycoon.Editor -- --attach-algorithm-graphs projekt.json algoritmusok.json C:/Projects/ForesTycoon
dotnet run --project ForesTycoon.Editor -- --validate-project projekt.json
dotnet run --project ForesTycoon.Editor -- --compile-project projekt.json
dotnet run --project ForesTycoon.Editor -- --simulate-project projekt.json 300 eredmény.json
```

A teljes gráfszimuláció eredménye a revíziót, világmezőket, készleteket és
objektumkatalógust is tartalmazza. Meglévő világ `--world mentés.json` opcióval
próbálható, az eredeti algoritmusrevízióval. Tudatos migrációra a lefordított
játék `--run-rule-project projekt.json 300 eredmény.json --world mentés.json --migrate`
kapcsolója szolgál. A szerkesztési mentés a teljes, akár hibás vagy hiányos
algoritmusgráfokat is külön `.algorithms.draft.json` fájlban tartja meg.

## Ellenőrzés

```powershell
dotnet test ForesTycoon.sln
dotnet run --project ForesTycoon.Editor -- --smoke-test
```

A tesztek ellenőrzik a versengő átadások korlátait és készletmegőrzését,
a visszacsatolást, az élő mezőolvasást, a hibás modell elutasítását, az
atomikus projektcserét, a fájlkapcsolat helyreállását és a mentési/visszajátszási
útvonalakat. Az algoritmustesztek a teljes import veszteségmentességét,
kapcsolatmódosítást, másolást, valódi fordítást, gráfból módosított gazdasági
képletet, állapotmigrációt, élő futtatóátadást és hibás fordítás elutasítását
is ellenőrzik. A grafikus próba készletet integrál, visszatölti, majd az editor
képletvásznát és inspectorát is kirajzolja. A lokális példa és képek:
`artifacts/rule-editor/feedback-project.json`, `dynamics.png`, `algorithms.png`.
