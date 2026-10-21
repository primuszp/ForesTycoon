# Editor–motor ellenőrzés, 2026-10-21

Az ellenőrzés a projektbetöltés, az állatok osztályalapú objektummodellje,
a szimulációs órák, a járművek útfelület-bemenete és a mentés kapcsolatait követte.
A teljes, tételes leltár továbbra is a [szabálylefedettség](rule-runtime-audit.md).

## Javított eltérések

- Az állatok idejét a motor fix négyszeres szorzóval léptette. Most a
  `wildlife.timeScale` projektgráf számolja a világ korából és az állatok számából.
  A kiinduló projekt konstans 4-et ad; állományfüggő vagy leállított idő is megadható.
- A földfelszín menetállapotának `0,2 × (1 − kopás) × járhatóság` képlete
  C#-ban maradt. Most a `trail.drivingCondition` gráf eredményét kapja a jármű.
  A felület típusa, a mozgás integrálása és a készletkönyvelés motorfeladat marad.
- A legelső projektverzió hiányzó induló szabályait a játék visszapótolta a
  migrációhoz, az editor nem. Most mindkét útvonal azonos szabálykönyvet állít elő,
  a régi törlésjelöléseket is figyelembe véve.
- A műhely közös tulajdonosi leképezést használ a gameplay kötésekhez; az új
  gráfok a megfelelő katalógustételhez tartoznak.

Az új definíciók az Editor `GameRules/default.project.json` dokumentumában
vannak. A projektverzió 5, a motorazonosító `forestycoon-simulation/2026-10-21.1`.
Az előző motor teljes checkpointja importálható. Korábbi motorhoz tartozó,
checkpoint utáni történeti lépéssor nem játszható vissza észrevétlenül az új motorral.
Az 5-ös projektből törölt gráfot sem normalizálás, sem mentés/betöltés nem pótolja
vissza az eredeti képlettel.

## Új állattípus és kapcsolatok

Az `Animal` leszármazottait a séma automatikusan felismeri. A publikus szám,
logikai és enum mezők, az örökölt mezők, a `DisplayName`, a `RuleRelation` és a
`RuleCollection` adják az editor modelljét. Egy új állattípushoz nem kell
editorlista vagy fajonkénti gyár. Újrafordítás és újraindítás szükséges;
tetszőleges forráskód-változás futás közbeni újratöltése nincs megvalósítva.

A közös állathatókör a leszármazottakon is fut. Az objektumhoz kapcsolt csempe
mezői, a gyűjtemények összesítései és a címezhető gyűjteményeken átadott
készletek használhatók új összefüggésekhez. Új típus felismerése nem jelent
automatikus egyedlétrehozást vagy külön 3D-megjelenést. A populáció és a
kezdőállapot a projekt `population` és `birth` szabályainak feladata.

## Ellenőrzési helyek

- `SimulationPolicyTests`: szerkesztett időszorzó és menetállapot alkalmazása,
  editoros újranyitás, mentés utáni folytatás, törlés utáni inaktivitás,
  4-es projekt és az előző motor checkpointjának migrációja.
- `EditorProjectLoaderTests`: régi wildlife képletek megőrzése, azonos régi
  szabálykönyv, félkész projektek újranyitása.
- `AutomaticAnimalModelTests` és `RuleCompositionTests`: új osztály és magyar
  mezőnevek felismerése, öröklött szabályok, kapcsolat mentén végzett írás,
  megőrző áramlás, populáció, mentés utáni azonos folytatás.
- Az editor grafikus smoke tesztje a tényleges betöltési és rajzolási útvonalat futtatja.

## Eredmény

A végleges Release build 0 hibával és 0 figyelmeztetéssel készült el.
A teljes futás 1993 tesztből 1992-t teljesített; az egyetlen eltérés a
`CurrentGameRulesTests` korábbi, a gameplay-kötéseket külön kezelő felsorolása volt.
Az elvárás frissítése után az érintett katalógus-, műhely-, migrációs és új
szimulációs tesztcsoport mind a 23 tesztje sikeres. A többi 1992 sikeres tesztet
követően csak ez a tesztelvárás és a dokumentáció változott.
Az editor grafikus és önálló ablakos smoke próbája, valamint a generált
szabályleltár eltérésellenőrzése sikeres.

Naplók: `ForesTycoon.Tests/TestResults/simulation-policy-full.trx`,
`ForesTycoon.Tests/TestResults/simulation-policy-final.trx`,
`artifacts/simulation-policy-editor-smoke.log`.
