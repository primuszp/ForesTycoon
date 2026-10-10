# Modellimport és erőforrás-tulajdon

Az `AnimatedGlbModel` a [Khronos glTF 2.0 specifikáció](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html) korlátozott GLB-részhalmazát olvassa. Ez CPU-művelet, és nem igényel grafikai kontextust. A modell nem futtat külső kódot és nem tölt le külső puffereket vagy képeket.

## Támogatott bemenet

- GLB 2, JSON- és BIN-chunkkal, egy beágyazott pufferrel; legfeljebb 128 MiB fájl.
- Indexelt háromszögek; FLOAT VEC3 pozíció és normál; opcionális VEC2 UV. A byte/ushort UV-k normalizáltak.
- TRS vagy mátrix node-transzformáció; véges komponensek, egységhosszú forgatási kvaternió. A hierarchia ciklusmentes, legfeljebb 16 384 node; feldolgozása iteratív.
- A kijelölt alapértelmezett scene hálói; scene-index hiányában az első scene. Ha nincs scenes mező, minden mesh-node a modellhez tartozik. A többi node transzformációja megmarad a skin/póz számára.
- Legfeljebb 64 egyedi joint skin-enként, csúcsonként négy hatás; a joint-, súly-, normál- és UV-accessor darabszáma a pozíciókhoz igazodik.
- Beágyazott, nem interlace-elt PNG a dekóder támogatott színformátumaiban, legfeljebb 4096×4096 pixelenként.
- OPAQUE, MASK és BLEND anyagfedettség, alapszín és alapszíntextúra; a támogatott kötelező kiterjesztés `KHR_materials_pbrSpecularGlossiness`.
- Translation, rotation és scale animáció LINEAR, STEP vagy CUBICSPLINE interpolációval; szigorúan növekvő, nem negatív keyframe-idők. Egy clip nem célozhatja kétszer ugyanazt a node/tulajdonság párt. A clip-neveknek egyedieknek kell lenniük.

A sparse accessor, a morph target, a további skin-hatások és az ismeretlen kötelező kiterjesztések explicit elutasítást kapnak. A kamera, fény, PBR metallic/roughness részletek és további textúrák nem tartoznak e dioráma-renderelő anyagmodelljéhez. Ez nem általános glTF-megjelenítő.

Az accessorok pufferhatára, igazítása, típusa és szemantikai mérete ellenőrzött a geometria felépítése előtt. Hibás szerkezet vagy adat `InvalidDataException`; nem támogatott formátum vagy túllépett erőforráskeret `NotSupportedException`. A fájl elérésének I/O-hibája megmarad I/O-hibának.

## Memória és tulajdon

A dekódolt asset adatkerete 256 MiB. Az accessorok dekódolt tárolását, a csúcs- és indexmásolatokat, valamint a PNG-adatot és a dekóder konzervatívan becsült átmeneti tárolását az allokáció előtt számolja. Ez az adatkeret nem teljes processzmemória-korlát: objektum- és gyűjteményfejlécek, a JSON-dokumentum, későbbi pózok és a driver belső tárolása ezen kívül esnek.

Egy CPU-modellből több egymástól független `Pose` készülhet. Egy `AnimatedModelRenderer` egy backendet birtokol, és annak GPU-geometriáját több póz rajzolásához használja. A renderer felszabadítása nem érvényteleníti a CPU-modellt. Másik modellhez tartozó póz nem rajzolható vele. Az injektált backend tulajdona átkerül a rendererhez; ugyanazt a backendpéldányt ne adjuk több tulajdonosnak.

Az OpenGL backend késleltetve foglal. Félbeszakadt feltöltés után minden addig létrehozott objektumot töröl, és ugyanaz a renderer újrapróbálhatja a feltöltést. A felszabadítás idempotens. A natív életciklusteszt a program-, buffer-, VAO- és textúrahandle-ek tényleges megszűnését is lekérdezi a drivertől. Az OpenGL-objektumok létrehozása, használata és törlése a megfelelő aktuális kontextusban szükséges; a renderer első backendhasználata rögzíti a renderkörnyezeti tulajdonost, és idegen környezetből a használat és törlés elutasított. Ugyanaz a CPU-asset több környezetben külön GPU-renderelőt kaphat; a [kétkontextusos próba](render-environments.md) ezt és a külön felszabadítást is ellenőrzi.

## A régi színezett teherautó

A `GlbTruckModel` a konvertáló által előállított, lapított, skin nélküli, FLOAT RGBA csúcsszíneket és `extras.category/pivot` metaadatot tartalmazó járműváltozatot olvassa. A közös GLB-előellenőrzést használja; transzformált vagy skinnelt node-okat nem értelmez csendben. A geometria normáljai, színei és pivotjai végesek. A GPU-buffer csak sikeres feltöltés után kerül a rajzolási csoporthoz; sikertelen feltöltés felszabadul és újrapróbálható. Felszabadított jármű nem rajzolható tovább.

## Ellenőrzés

A `GlbValidationTests` hibás szerkezeteket, méret- és típuseltéréseket, ciklust, túlméretes fájlt, mély hierarchiát és scene-kiválasztást vizsgál. A telepített GLB-k mindegyike átmegy a véges póz és konzisztens csúcselrendezés próbáján; a helyileg jelen lévő licencelt modellek is részt vesznek benne. A `ModelRenderBackendTests` a pózok önállóságát és a renderer tulajdonát vizsgálja. A `GraphicsBackendTests` feltöltési hibát injektál a régi teherautóba.

A `--material-alpha-smoke-test` a valódi OpenGL életciklust és az anyagfedettség képi eredményét ellenőrzi; a `--truck-smoke-test` és `--wildlife-smoke-test` az alkalmazás valódi modelljeit rajzolja. Mindhárom próba szerepel a közös `tools/verify-engine.ps1 -Native` ellenőrzésben.
