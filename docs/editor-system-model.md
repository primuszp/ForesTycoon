# Rendszermodell-alapú editor: értékelés, célarchitektúra, ütemterv

> **Elavult (2026-10-12):** a rendszermodell-térkép és a szabályműhely kikerült a felületről, mert nem volt használható. Az aktuális editor: [Világszerkesztő](rule-editor.md). A rendszermodell-szótár (`system-model.json`) és az elemzés a kódban és a CLI-ben megmaradt.


Állapot: 2026-10-11. Az első szakasz (közös rendszermodell, rendszertérkép,
gazdasági visszacsatolás, projektszintű próba és alkalmazás) megvalósult; a
további szakaszok tervek. Használat: [editor útmutató](rule-editor.md),
futtatási szerződés: [rendszerdinamika](system-dynamics.md).

## Cél

Az editorban fejlesztjük a játék teljes belső modelljét: talaj, klíma, időjárás,
erdőállomány, évszakok, utak, kitermelés, növedék, faegészség és gazdaság
összefüggéseit. A játék ezt a modellt futtatja. Később ugyanitt szerkesztjük a
fafajokat és famodelleket, a terepet és a terepjellemzőket, végül a pályákat és
a missziókat. Ember és AI ugyanazt a dokumentumot, ugyanazokat az azonosítókat és
ugyanazt az ellenőrzést használja.

## Értékelés: mennyire intuitív és logikus az alap?

Erős alapok:

- Egy közös `forest-rule-project` dokumentum a játéknak, az editornak és az AI-nak;
  külön munkapéldány, atomikus közzététel, külső módosítás elleni védelem, előzmény.
- Az átvétel visszaigazolt; az összehasonlító próba azonos checkpointból fut.
- Determinisztikus, naplózott projektcsere; tickkel késleltetett készlet–áramlás integrátor.
- Veszteségmentes algoritmusgráf és fordító; headless CLI az AI-nak.

A használatot rontó, logikai hibák (a javítottak jelölve):

| Probléma | Következmény | Állapot |
| --- | --- | --- |
| Nem volt rendszerdinamikai gerinc. A 82 szabály 95 mezőt olvas/ír, de a mezők csak szöveges azonosítók voltak: nem derült ki, mi készlet, mi áramlás, mi paraméter, és hol záródik visszacsatolás. A Dinamika csak a *saját* készleteket mutatta. | A felhasználó azt láthatta, hogy a játéknak nincs SD-modellje; az összehangoláshoz nem volt áttekintés. | **Javítva:** rendszermodell-szótár, elemzés, rendszertérkép. |
| Két eltérő taxonómia: a szabálytérkép 6 tartománya mást csoportosított, mint a műhely 7 témája (pl. Faanyag és logisztika: „Gazdaság” vs. „Kitermelés és szállítás”). | Ugyanaz a szabály két helyen mást jelentett. | **Javítva:** egy alrendszerlista, adatfájlban. |
| A Megértés lap „Mi mivel függ össze?” része a fanövekedésre volt bekódolva (vízválasz → növekedés → térfogat); más szabálynál az első mezőt mutatta. | Félrevezető kiemelés. | **Javítva:** adatvezérelt bemenet / szerep / kimenet és a szabályon átmenő körök. |
| A Próba és az Alkalmazás szabályonkénti fülként jelent meg, pedig mindkettő a teljes projektre vonatkozik. | „Az Egyedi fanövekedés próbája” valójában az egész projekt próbája volt. | **Javítva:** a felső sáv projektállapot-lánca (Munkapéldány → Próba → Játék) nyitja őket; a szabálynál csak Megértés / Szerkesztés van. |
| A mezők magyar neve kódba égetett `switch` volt (95-ből ~40). | Nyers azonosítók a felületen; az AI nem bővíthette. | **Javítva:** a szótár adja. |
| A pénz nem hatott vissza semmire. | A gazdaság nyitott hurok volt: nem lehetett gazdasági egyensúlyt hangolni. | **Első lépés kész:** hitelkeret és piaci árszorzó (lásd lent). |
| Négy gráfnyelv: útkopás (`RuleModel`), képlet (`BehaviorGraph`), munkaciklus (`BehaviorController`), algoritmus-szintaxisgráf, plusz külön `tuning` szótár. Az útkopás saját, külön gráftípust használ. | A „szabály” fogalma technikai alapon szétesik. | Nyitott – 2. szakasz. |
| Halott régi fülrendszer (fejléc, fájlmenü, állapotsor). | Karbantartási zaj. | **Javítva:** eltávolítva. |
| Fafaj-, talaj- és klímaadat csak tájékoztató; nincs helyük a projektben. | Nem lehet pálya- és tartalomszerkesztő felé bővíteni. | Nyitott – 3. szakasz. |

## A rendszermodell

`ForesTycoon.Rules/Catalog/system-model.json` (`forest-system-model`, 1. verzió):

- **Alrendszerek** – az egyetlen taxonómia: Erdő és élővilág; Időjárás, talaj és víz;
  Kitermelés és szállítás; Utak és járművek; Gazdaság és fenntartás; Világ és idő;
  Látvány. Mindegyik felsorolja a hozzá tartozó katalógusmodulokat.
- **Mennyiségek** – minden katalógusmező: név, alrendszer, szerep, egység (csak ha
  biztos), képletkulcs (`observe`, pl. `world.treeCount`), leírás.
  Szerepek: készlet, áramlás, segédváltozó, paraméter/adat, térkép és szerkezet,
  külső bemenet/óra, megjelenítés. Visszacsatolásban csak az első három vesz részt.

`SystemModelAnalysis` a katalógusból (és a projekt saját elemeiből) számol:

- folyamatonként: mit olvas, mit ír, mely készletek áramlása;
- hatások: A → B, ha egy folyamat A-t olvassa és B-t írja;
- **összecsatolt csoportok** (erősen összefüggő komponensek): a jelenlegi játékban
  egy 45 mennyiségből álló mag köti össze a vizet, az erdőt, a kitermelést, az utakat
  és a gazdaságot, külön csoport a vad–táplálék pár;
- **körök**: minden készlet legrövidebb köre (fő körök), illetve egy mennyiségen vagy
  szabályon átmenő legrövidebb körök; a teljes felsorolás a logisztikai állapotgép
  sűrűsége miatt használhatatlanul hosszú lenne;
- **nyitott készletek**: amelyek értéke nem hat vissza a saját alakulásukra (jelenleg
  holtfa és tönkök).

A katalógus adatkapcsolatot rögzít, nem végrehajtási sorrendet és nem előjelet. A
felület ezért nem nevez meg erősítő/kiegyenlítő kört; ezt az összehasonlító próba
mutatja meg.

A projekt saját készletei, áramlásai és a mennyiséget olvasó képletei ugyanebbe a
modellbe kerülnek. Egy képlet ahhoz a szabályhoz kötődik, amelynek eredményét
helyettesíti vagy paraméterét számolja; így egy szerkesztett visszacsatolás a
beépítettek mellett, ugyanazon a térképen jelenik meg.

## Gazdaság a modellben

Első futtatói lépés, alapértékkel a korábbi játékmenet változatlan:

- **Hitelkeret** (`CreditLimit`, eFt, alapértelmezés: gyakorlatilag korlátlan). Ha az
  egyenleg túllépte a hitelkeretet, új út, közelítőnyom és útjavítás nem indul; a
  parancs determinisztikusan elutasítódik, a játék üzenetet ad. Kör: egyenleg →
  kiadás → egyenleg, illetve egyenleg → útállapot → szállítás → bevétel.
- **Faanyag piaci árszorzó** (`TimberPriceFactor`, ×, alapértelmezés 1): a malomnál
  elszámolt bevételt szorozza.

Mindkettő képlettel számolható (hangolási kötés), olvashatja az egyenleget, a
bevételt, a kiadásokat vagy saját készletet. Így piaci kínálat–kereslet vagy
adósságkészlet saját készlettel és áramlással modellezhető, ugyanúgy, mint a többi
dinamika.

## Célarchitektúra

A projekt rétegei, mind ugyanarra a mennyiség- és alrendszer-szótárra hivatkozva:

1. **Modell** – mennyiségek, alrendszerek, folyamatok és hatásaik (megvan, `system-model.json`
   + katalógus). Később: a projektben bővíthető szótár saját mennyiségekkel és
   annotált előjelekkel, amelyeket a próba igazol.
2. **Szabályok** – a folyamatok megvalósítása: képlet, munkaciklus, belső algoritmus,
   készlet–áramlás (megvan). Cél: egy szabálygráf-nyelv; az útkopás `RuleModel`-je
   `BehaviorGraph`-ra költözik, a `tuning` a paraméter-csomópontok alapértéke lesz.
3. **Tartalom** – fafajok, talajprofilok, klímák, járműtípusok, árak: adatgráfok,
   amelyek a modell paraméter-mennyiségeit töltik (ma tájékoztató adat).
4. **Pálya** – terep és terepjellemzők, induló erdő, utak, telephelyek: a modell
   szerkezet-mennyiségei egy konkrét térképen.
5. **Misszió** – célok és események mint feltételek a modell mennyiségein
   (pl. „10 erdőév alatt `forest.volume` ≥ X, `economy.result` ≥ 0, `road.condition`
   átlaga ≥ 0,6”). Mivel a mennyiségek közösek, ugyanaz a mérés szolgálja a próbát,
   a misszió kiértékelését és az AI-t.

## Ütemterv

1. **Kész – rendszermodell:** szótár, elemzés, rendszertérkép, adatvezérelt
   Megértés lap, egy taxonómia, projektállapot-lánc, gazdasági visszacsatolás,
   `--export-system-model`.
2. **Egységes szabálynyelv:** az útkopás és a hangolás `BehaviorGraph`-ra; a
   rendszertérképről közvetlen „visszacsatolás hozzáadása” művelet (készlet +
   áramlás + képlet egy lépésben); próbamérések a kör mennyiségeire (idősor).
3. **Gazdaság bővítése:** járműbeszerzés és malomépítés költsége a hitelkeret alatt,
   kamat mint áramlás, beépített piaci kereslet–kínálat készlet, erdőápolási kiadás.
4. **Tartalom:** fafaj-, talaj- és klímadefiníciók szerkeszthető adatgráfként a
   projektben, újvilág-szabályként jelölve.
5. **Pálya- és misszószerkesztő:** terep és induló állapot szerkesztése, missziócélok
   a modell mennyiségein, ugyanazzal a próbával és közzététellel.
