# Editor–motor ellenőrzés, 2026-10-23

Az ellenőrzés azt követte, hogy amit a motor futtat, az az editor valamelyik oldalán
elérhető-e, és hogy a két oldal ugyanazokat a fogalmakat (hook, időzítés, hatásmód,
hangolható szám, motorszabály) ugyanúgy nevezi-e meg. Az előző kör:
[2026-10-21](editor-engine-review-2026-10-21.md).

## Automatikus lefedettség-ellenőrzés

```powershell
dotnet run --project ForesTycoon.Editor -- --check-editor-coverage
```

Az `EditorCoverage` a futó modellből számol, nem kézi listából:

- minden `WorldBehaviorPolicy` hook: a tiszta válaszgráfok témacsoportja az
  **Összefüggések** oldalon, a többi a **Folyamatkapcsolatok** oldalon; a projekt nem köt
  a motor által nem ismert hookra;
- minden `WorldRule.Schedules` időzítésnek van felirata, minden `RuleEffect.Modes`
  hatásmódnak van szerkesztője;
- mind az 51 `GameTuning` szám megjelenik a **Járművek** vagy a **Játékparaméterek** oldalon;
- minden szerkeszthetőnek jelölt katalógusszabályhoz tartozik szabály, gráf,
  munkaciklus vagy hangolható szám a projektben.

Hiány esetén nem nulla kilépési kóddal tér vissza; az editor smoke tesztje is
megbukik rajta. A `RuleWorkshopTests` ugyanezt a katalógus-tulajdonlást grafika nélkül ellenőrzi.

Jelenlegi eredmény: 205 hook, 51 hangolható szám, 61 szerkeszthető motorszabály,
hiány nincs. Motoroldali, szándékosan nem szerkeszthető: időütemezés, parancsok,
checkpoint, gráfkötések futtatása, terep- és vízmedence-generálás, terepszerkesztés,
talaj- és fafajkatalógus, vízmérleg-zárás, útvonalkeresés, sarang-mérleg (12 szabály).

## Javított eltérések

- **Motorszabály ↔ gráf hozzárendelés.** A katalógus a regionális klímát, az időjárási
  eseményeket, a hó átrendezését, a lombborítást, a fák kezdő méretét, a versengést és a
  természetes újulatot szerkeszthető gráfként jelölte, de a műhely `HookBelongsTo`
  leképezése nem ismerte a hozzájuk tartozó hookokat. A rendszermodellben ezek a gráfok
  nem kapcsolódtak motorszabályhoz. Most témacsalád szerint kapcsolódnak.
- **Időzítések felirata.** A mezőhasználati áttekintés az állati, állomány- és
  létrehozási időzítést „időszakonként”-nek írta. A kártya, az áttekintés és az
  ellenőrző most ugyanazt a feliratkészletet használja.
- **Halott hivatkozás.** A Játékparaméterek oldalon a szabálynevek gombnak látszottak, de
  egy már nem megjelenített áttekintést kértek. Most a számokat használó szabályt vagy
  gráfot nyitják meg; ha nincs ilyen, egyszerű szöveg.
- **Ablakméret a smoke tesztben.** A `Draw` felülírta a hívó ablakméretét, így a teszt az
  önálló ablak `imgui.ini`-ben tárolt méretét örökölte.

## Nyitott pontok

- Az `AlgorithmEditorView` (a teljes játékkód szerkezeti gráfként) és a motorszabály-katalógus
  részletes nézete (`CurrentRulesView.Draw`) nincs bekötve a felületbe. Az algoritmusgráfok
  revíziója a játékban érvényesül (`RequireAlgorithmRuntime`), de a felületen csak a
  munkapéldány mentése érinti őket.
- A **Logisztika** témacsoport 55 gráfja egy listában van; géptípus szerinti
  alcsoport segítené a keresést.
