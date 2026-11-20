# Gyérítési kísérlet nyír- és erdeifenyő-állományban

A kérdés az volt, hogy a gyérítés után meghagyott fa tovább él-e, mint ugyanaz az egyed az érintetlen állományban. A három generált pályán az ötödik évben mindkét fafajnál több meghagyott törzs élt a gyérített változatban. A tizedik évben már egyik változatban sem élt nyír a kiinduló egyedek közül.

## Kísérleti elrendezés

A 17-es, 42-es és 83-as kezdőértékkel létrehozott, 16 × 16 csempés világokban összesen 13 nyír- és 11 erdeifenyő-csempét követtünk. A kezdőállományban 113 nyír és 87 erdeifenyő szerepelt. Mindegyik világ ugyanabból a kezdeti állapotból futott négy alkalommal. A kontrollt érintetlenül hagytuk, a kezelésekben egyszer, a nulladik évben az élő körlapösszeg 10, 30 vagy 50%-át adtuk meg felső határként.

A játék csempénként a vékonyabb, vágható törzsekkel kezdi a kijelölést. Egy törzset csak egészben lehet kivágni. A megvalósult százalékot az összes kivágott törzs körlapösszegének és a fafaj teljes induló körlapösszegének hányadosaként számoltuk. A kísérlet a játék kijelölési parancsát és a processzor kitermelési műveletét használja. Minden kezelést azonos időpontra tettünk, így a gépek kiérkezési ideje, az üzemanyag és a bevétel kívül esik az összevetésen.

## A kiinduló fák túlélése

Az évoszlopok első száma a meghagyott egyedek közül életben lévők száma. A második ugyanazoknak az azonosítóknak a száma a kezeletlen párban. Így minden sor a saját összehasonlító csoportjához tartozik.

| Fafaj | Cél | Kivett körlapösszeg | Kivágott fa | Meghagyott fa | 5. év, kezelt / kontroll | 10. év | 30. év |
|---|---:|---:|---:|---:|---:|---:|---:|
| Nyír | 0% | 0,0% | 0 | 113 | 55 / 55 | 0 / 0 | 0 / 0 |
| Nyír | 10% | 6,1% | 8 | 105 | 53 / 52 | 0 / 0 | 0 / 0 |
| Nyír | 30% | 27,4% | 35 | 78 | 44 / 39 | 0 / 0 | 0 / 0 |
| Nyír | 50% | 43,6% | 56 | 57 | 35 / 27 | 0 / 0 | 0 / 0 |
| Erdeifenyő | 0% | 0,0% | 0 | 87 | 10 / 10 | 3 / 3 | 2 / 2 |
| Erdeifenyő | 10% | 7,8% | 8 | 79 | 12 / 10 | 3 / 3 | 2 / 2 |
| Erdeifenyő | 30% | 24,7% | 25 | 62 | 15 / 7 | 2 / 2 | 2 / 2 |
| Erdeifenyő | 50% | 43,4% | 43 | 44 | 22 / 5 | 3 / 1 | 1 / 1 |

Az ötödik évben a 30%-os kezelés mellett öt olyan nyír és nyolc olyan erdeifenyő élt, amelynek a kezeletlen párja már elpusztult. Fordított eset ennél a kezelésnél nem fordult elő. A harmincadik évben a kiinduló nyírcsempéken egyik kezelésben sem találtunk élő új nyírt. Mindkét eredmény ezekre a pályákra és a modell jelenlegi szabályaira vonatkozik. A hosszú távú elhalás értelmezéséhez az induló kor, a termőhely és az újulat sorsának külön vizsgálata szükséges.

### Mi történt a kezeletlen nyírekkel?

Ugyanezen pályák havi egyedadatain az első 113 nyír elhalása a 49. és a 106. hónap közé esett. A medián a 49. hónap volt. Az elhalási hónapban 44 fánál a fényválasz, 36-nál a vízellátás, 33-nál a növőtér adta a modell legkisebb erőforrásértékét. Ekkorra minden egyednél 4,04–4,09 évnyi modellbeli stressz gyűlt össze. Az utolsó havi minimum egy állapotfelvétel. Az egyes tényezők oksági hatását külön beavatkozásokkal lehet elkülöníteni. A csomagban a `control-mortality.csv` a két fafaj összes induló egyedének utolsó havi megfigyelését tartalmazza, a harmincadik év végén életben lévő fákéval együtt.

A modell érzékenységét további tizenkét, tízéves futásban vizsgáltuk. A három pályán külön-külön maximálisra állítottuk a fához jutó fény, a szabad növőtér vagy a faj vízválaszának szabálykimenetét. Ebben a kísérletben nem gyérítettünk. A táblázat a teljes pályákon élő fákat összesíti, beleértve a közben megtelepedett újulatot is.

| Egyetlen feloldott korlát | Nyír a 10. évben | Erdeifenyő a 10. évben |
|---|---:|---:|
| Kontroll | 1 | 4 |
| Fény | 24 | 6 |
| Növőtér | 1 | 5 |
| Vízellátásra adott stresszválasz | 41 | 13 |

A vízválasz feloldása változtatta meg legnagyobb mértékben a tízéves létszámot mindkét fajnál. Ez a próba a szabály kimenetét rögzíti 1-re, és a teljes állomány dinamikája tovább működik. Terepi kalibráláshoz ezért különösen értékes lenne az induló kor és törzsátmérő mellett a termőhely vízellátottságát és a később elpusztult egyedeket is ismerni.

## Megismétlés

A projekt gyökérkönyvtárából a következő parancsokkal készíthető el a három CSV.

```sh
dotnet build ForesTycoon.sln -c Release
for seed in 17 42 83; do
  dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --thinning-trial 30 "$seed" > "seed-$seed.csv"
  dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --forest-followup 30 "$seed" monthly
  for condition in control forest.light forest.space water.stress; do
    dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --forest-demography 10 "$seed" "$condition" > "factor-$seed-$condition.csv"
  done
done
```

A fájlok csempénként, kezelésenként és évenként egy sort tartalmaznak, a nulladik évvel együtt. A `cut_m2` és a `cut_m3` az egyszeri kitermelést írja le, így összesítéskor ezeket egy évből kell venni. A `paired_alive` a mindkét változatban élő párok száma. A `sum_paired_dbh_delta_mm` és a `sum_paired_space_delta` ezekre a párokra összegzi az átmérő-, illetve a tenyészteret jellemző modellérték eltérését. A `new_living_same_species` az eredeti csempén élő új, az induló állománnyal azonos fajú fákat számolja. A kísérlet minden futásban ellenőrzi az induló egyedek egyezését, a kijelölt és kivágott törzsek azonosságát, a körlapösszeg korlátját és a kitermelt térfogatot.
