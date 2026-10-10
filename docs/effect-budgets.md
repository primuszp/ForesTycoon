# Effektkeretek és mérések

Az időjárási megjelenítés nem változtatja a szimulációt. A részecskeszám és a felhőlépések kemény munkakorlátok, a CPU-idő minőségi célérték. A célérték túllépését a fejlesztői panel jelzi; a renderer nem szakít félbe GPU-hívást és nem garantál gépfüggetlen képkockaidőt.

| Minőség | Eső/hó maximum | Felhőminták pixelenként | CPU-cél, eső + felhő + köd | Ködforrások maximuma | Ködrétegek | Köd mélységadata | Rajzolt akciójelölők |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Low | 1500 | 6 | 0,5 ms | 256 | 2 | 16 MiB | 128 |
| Medium | 3000 | 8 | 1 ms | 512 | 3 | 32 MiB | 256 |
| High | 6000 | 12 | 2 ms | 768 | 3 | 64 MiB | 512 |

Az esőtervező legfeljebb 65 536 részecskét enged, a felhőrenderer legfeljebb 32 mintát pixelenként. A 0 keret letiltja az adott effektet. Az esőrács a világ celláihoz rögzített; apró keret esetén a beküldött példányok számát csökkenti, és nem próbálja végtelen ciklussal a geometriai minimum alá zsugorítani a rácsot. Üres nézet nem kér részecskéket.

A magasságmező legfeljebb 4 194 304 float-mintát tartalmazhat: 16 MiB CPU-adat és 16 MiB GPU-textúraadat. A driver maximális textúramérete is korlátozza a dimenziókat. Más méretű felületre váltáskor egy régi és egy új CPU-mező rövid ideig egyszerre élhet, így a csere további legfeljebb 16 MiB átmeneti CPU-adatot igényelhet. A shaderprogram és a driver belső könyvelése nem része a payloadszámlálóknak.

A cache-kulcs a felület azonosságát, a dimenziókat és a revíziót is figyelembe veszi. Azonos revíziójú másik világ vagy átméretezés is új feltöltést kér. Nem véges magasság nem kerül a GPU-ra. Hibás feltöltés után a revízió nem publikálódik sikeresként, ezért a következő hívás újrapróbálhatja a feltöltést. A felszabadítás törli a CPU-mezőt és a GPU-erőforrásokat; a felszabadított renderer használata elutasított.

A `WeatherRenderer`, `CloudRenderer` és `ForestWeatherRenderer` CPU-időt mér és közli a backend munkaszámlálóit. A host minden képkocka elején nullázza a munkaszámlálókat, ezért kihagyott pass után nincs előző képkockából maradt részecskeszám. A megtartott memória mérete továbbra is látszik. A GPU-idő a host `RenderPassProfiler` mérésében szerepel; a CPU-beküldési idő nem helyettesíti azt.

A köd teljes nézetméretű mélységtextúrája pixelenként 4 bájtos adatkeretet használ. A keretet vagy a driver méretkorlátját meghaladó nézetben a renderer felszabadítja a mélységtextúrát és a framebufferét, és a szokásos mélységteszttel rajzol ködöt, puha mélységi metszés nélkül. Kisebb nézetben újra létrehozhatja a textúrát. Minőségcsökkentéskor a megtartott mélységadat akkor is felszabadul, ha az adott képkockában a ködpass kimarad. A fejlesztői panel jelzi ezt a tartalék megjelenítést. A ködforrás-cache legfeljebb 4096 bejegyzést, a villámhoz gyűjtött koronák listája legfeljebb 2048 elemet tartalmaz. A köd példányszáma Low/Medium/High szinten legfeljebb 512/1536/2304.

Az akciójelölők állapota előre lefoglalt, 4096 elemű körpufferben él, 1 MiB alatti CPU-payloaddal. Telítődéskor a legrégebbi vizuális jelölő helyére kerül az új; a kimaradt jelölők számlálója látható. A létrehozás bemelegítés után nem allokál elemenként. Rajzoláskor a legújabb jelölők kapnak helyet a minőségi keretben, jelölőnként 40 vonalcsúccsal, High szinten legfeljebb 20 480 beküldött csúccsal. Régi, túlméretes checkpoint minden jelölőjét validáljuk, és a legújabb 4096 vizuális elemet tartjuk meg. Ez a migráció a pénzügyi, ökológiai és időállapotot nem módosítja.

## Ellenőrzés és fennmaradó feladatok

A `WeatherParticlePlanTests` a 0/1/25/49 és a minőségi kereteket több nézetmérettel vizsgálja, továbbá a hibás bemenetet, a cellához rögzített panninget és az idempotens tulajdonosi felszabadítást. A `--graphics-smoke-test` natív effektpróbája magasságtextúra-cserét, azonos revíziójú átméretezést, nem véges adatot, újrapróbálást, memóriahatárt és felhőlépéseket is ellenőriz.

A ködpróba valódi OpenGL-handle lekérdezésekkel ellenőrzi a mélységadat kerethatárát, felszabadítását, újralétrehozását és idempotens törlését. A forrásgyűjtés nagy térképen is korlátos. A jelölőtesztek telített körpuffert, lejáratot, sorrendtartó mentést, régi túlméretes állapot migrációját és rajzolási keretet vizsgálnak; az allokációmérés a többi teszttől elkülönítve fut.

A payloadszámlálók nem mérik a teljes processzmemóriát, a megosztott rajzolási infrastruktúra és a driver belső költségét. A több renderkörnyezet tulajdonlása és a rögzített GPU-n mért teljesítménykapu továbbra is nyitott motorfeladat.
