# Renderkörnyezetek tulajdonlása

A `RenderEnvironment` egy szálhoz kötött grafikai backend, transzformációs állapot és render-szolgáltatástár tulajdonosa. A `RenderDevice` megmaradó statikus belépési pontjai az aktív környezethez irányítanak. Az explicit környezet aktiválása scope-ot ad; a scope-ok fordított sorrendben zárhatók. A környezet nem vihető át másik szálra.

Külön környezeti állapotot kap a kamera és modellmátrix, a LOD-intervallum, a felületi shaderkapcsolat, a fény és diagnosztikai felülírás, a rajzolási számlálók és passmérés callbackje, a dinamikus primitívek munkalistája, a shader uniform-cache, a modellek/effektek/scene backendgyárai és az ablakplatform. A járműrenderer importált modellje, körvonal-kerete és textúrázott modellbeállítása is környezetenként külön tárolódik. Egy környezet törlése a saját szolgáltatásait és backendjét szabadítja fel.

Natív host esetén a konstruktor kap egy ellenőrző callbacket, amely igazolja, hogy a hozzá tartozó OpenGL-kontextus az aktuális. A managed scope váltása önmagában nem tesz egy natív kontextust aktuálissá. A hostnak előbb az ablak kontextusát kell aktiválnia, majd a hozzá tartozó renderkörnyezet scope-jában dolgoznia. A scope lezárása után a korábbi natív kontextust is vissza kell állítani a következő GPU-hívás előtt.

A `VertexBuffer` a létrehozó környezetét őrzi. Feltöltés, rajzolás, GPU-visszaolvasás és törlés előtt megköveteli annak aktív állapotát és natív kontextusát. A lapozott feltöltés minden `MoveNext` előtt ellenőriz, mert maga az iterátor továbblépése végzi a GPU-munkát. Hibás tulajdonos esetén a backend nem kapja meg a következő feltöltést. A buffer másodszori felszabadítása idempotens.

A közvetlen OpenGL-backendek `RenderResourceOwner` ellenőrzést használnak. A CPU-konstrukció nem kötődik GPU-környezethez; az első backendhasználat rögzíti a tulajdonost. Modell, modellbatch, eső, felhő, köd, felületi shader, erdőanyag, postprocess, UI, erdőállapot-buffer, primitívbackend és állapotscope nem használható vagy törölhető másik aktív környezetből. Az elutasítás a GPU-hívás és a felszabadított állapot publikálása előtt történik. Ezért hibás környezetből indított törlés után a saját környezetben még rendesen felszabadítható az objektum. A megjelenítéssel rendelkező világ frissítése, rajzolása, parancsvégrehajtása és cseréje is ellenőrzi a megjelenítés tulajdonosát; a grafika nélküli világ nem igényel ilyen környezetet.

Az ImGui-controller a saját UI-kontextusát teszi aktuálissá frame-előkészítés, renderelés és input előtt. Sikertelen tulajdonosi ellenőrzés nem semmisíti meg a hozzá tartozó UI-kontextust vagy a megtartott glyphmemóriát.

Az explicit környezet megszüntetése előtt a hostnak fel kell szabadítania a hozzá tartozó világot és megjelenítési objektumokat. A `RenderDevice.Dispose` a korábbi egyszerű alkalmazások újrainicializálható eszköz-életciklusát tartja meg; az explicit `RenderEnvironment.Dispose` végleges, utána újraaktiválás elutasított.

## Bizonyíték és hátralévő integráció

A `GraphicsBackendTests` két környezetben vizsgálja a külön kamera-, fény-, gyár- és számlálóállapotot, a környezetenként beágyazható primitívrajzolást, a másik környezetből történő bufferhasználat és törlés elutasítását, a rossz natív kontextust, a száltulajdont és a lapozott feltöltés folytatását.

A `--render-environment-smoke-test` két külön natív OpenGL-ablakot használ. A driver azonos programneveket ad a két kontextusban; az egyik programban meglévő, a másikban hiányzó uniformhoz helyes, elkülönített cacheérték tartozik. Piros és zöld képpontminták ellenőrzik a rajzolást. Mindkét környezet saját játékvilágot, modellrenderelőt, modellbatchet, esőt, felhőt, postprocesst és UI-t is futtat. Másik környezetből a rajzolás és törlés elutasított. A második környezet törlése után az első tovább rajzol és saját UI-frame-et készít; a driver program-, VAO-, buffer- és textúralekérdezései igazolják a modellek és az időjárás külön felszabadítását. Ez a próba a közös natív ellenőrző része.

A többablakos hostintegráció még nincs lezárva. A játék, preview és editor alkalmazásablakait explicit saját környezethez kell kötni, a frame-, input- és felszabadítási callbackjeikben a megfelelő natív és managed környezetet kell választani. A mostani kétablakos próba a külön környezetekben futó jelenetek és render-backendek izolációját bizonyítja; az alkalmazásablakok teljes callback-életciklusának regressziója még szükséges.
