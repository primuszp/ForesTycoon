# Szabályok építőkövekből – elemzés és terv

Állapot: 2026-10-12. Cél: a játék szabályai C# nélkül, az editorban legyenek leírhatók, és a játék ezeket futtassa. A meglévő szabályokat egyenként költöztetjük át.

## 1. Mi van most?

A szabálykatalógus (`CurrentGameRules`) **83 szabályt** ír le 14 modulban. Ezekből:

| Szerkeszthetőség | Darab | Mit lehet vele |
| --- | --- | --- |
| Képlet-felülírás (`BehaviorHook`) | ~25 pont | Egyetlen kimeneti szám felülírása. A számítást a C# indítja, a bemeneteket a C# adja, és a C# dönti el, mire használja az eredményt. |
| Hangolható szám (`GameTuning`) | 55 | Egy állandó cseréje, korlát között. |
| Állapotgép (`BehaviorController`) | 1 | Gépek munkaciklusa. |
| Készlet–áramlás (`DynamicsModel`) | saját | Saját készletek és áramlások; a világra nem hatnak vissza. |
| Útkopásgráf (`RuleModel`) | 1 | Külön gráftípus egyetlen szabályhoz. |
| Csak natív C# | ~45 | Nem szerkeszthető. |
| Megjelenítés | 11 | Nem játékszabály. |

**Miért nem „akar átkerülni”?** A képlet-felülírás nem szabály, csak egy szám a C# szabály belsejében. Egy szabály négy kérdésre felel, és ebből hármat ma mindig a C# válaszol meg:

1. **Mikor** fut? (lépésenként, naponta, havonta, eseményre)
2. **Hol** fut? (a világban, minden csempén, minden fán, járművön, nyomon…)
3. **Ha** mi teljesül? (feltétel)
4. **Akkor** mi változik? (melyik állapot, hogyan; mi jön létre, mi szűnik meg)

A képlet csak a 4. pont *egy számát* adhatja. Új szabályt (például „ha a csempe 3 évig vizes, a talaj tőzegesedik”) így nem lehet leírni.

## 2. Az építőkövek

Egy **szabály** = *időzítés* + *hatókör* + *feltétel* + *hatások*. Minden rész adat (JSON a jelenet- vagy szabályprojektben), az editor szerkeszti, a játék determinisztikusan futtatja.

### 2.1 Hatókör (entitáshalmaz) – „hol”
Az a halmaz, amelynek minden elemén a szabály lefut: `world` (egyszer), `tile` (minden csempe), `trail` (minden közelítőnyom), és később `road`, `tree`/`stand`, `vehicle`, `mill`, `depot`, `stack`. Egy hatókör megmondja, milyen **mezők** olvashatók és írhatók rajta, és milyen **műveletek** végezhetők.

### 2.2 Mezők – „mit”
- **Beépített mezők:** a játék állapota, név szerint (például `nyom.kopás`, `nyom.tétlenség`, `gyökérzónavíz`, `világ.egyenleg`). Olvashatók, és ha a hatókör engedi, írhatók is. Az írás a játék saját állapotát módosítja, ezért a mentés és a visszajátszás változatlanul működik.
- **Saját mezők:** a tervező új állapotváltozót vehet fel egy hatókörre (név, kezdőérték, korlát, egység). A játék menti őket. Ez teszi lehetővé teljesen új szabályok leírását C# nélkül.
- **Hangolható számok és készletek:** a `tuning.*` és `stock.*` nevek olvashatók.

### 2.3 Kifejezések – „mennyi”
A meglévő képletnyelv: + − * /, összehasonlítás, `min`, `max`, `abs`, `exp`, `hatvány`, `ha(feltétel; akkor; különben)`, köztes nevek. Változatlanul a meglévő fordítót és a korlátos, IO nélküli kiértékelőt használja.

### 2.4 Időzítés – „mikor”
- `step`: minden szimulációs lépésben. A `lépés` név az eltelt időt adja erdőévben, így a sebességfüggő szabályok (`kopás − 0,4 × lépés`) pontosak.
- Később: `hour`, `day`, `month`, `year` határ, és **események** (fa kivágva, faanyag leszállítva, út épült, küldetés teljesült).

### 2.5 Hatások – „akkor”
- `set mező = kifejezés`
- `add mező += kifejezés`
- **művelet:** a hatókör saját, nevesített művelete, például `trail.remove` (a nyom megszűnik). Később: `pénz += …`, `üzenet`, `esemény kiváltása`, `fa ültetése`.

A végrehajtás **elemenként sorrendben** történik. Egy elemen előbb a feltétel értékelődik ki, majd a hatások sorban; egy későbbi hatás már a korábbiak eredményét látja. Az elemek bejárási sorrendje rögzített, így a futás determinisztikus. Mivel egyik hatókör mezői sem olvassák a szomszédos elemeket, az eredmény nem függ a bejárás sorrendjétől. Szomszédos elemeket olvasó mezőkhöz később kétfázisú végrehajtás kell.

### 2.6 Beépített szabályok mint adat
Az átköltöztetett szabály a **játék alapprojektjében** szerepel adatként (`Rules`, `builtIn: true`). Ha egy régebbi projektből hiányzik, a betöltő pótolja; ugyanígy pótolja ma a képletpontok alapgráfjait. A natív C# ág megszűnik, a szabályt a szabálymotor futtatja. Szerkeszthető, kikapcsolható és visszaállítható.

## 3. Átköltöztetési sorrend

Egyenként, mindegyik után mérve, hogy a játék ugyanúgy viselkedik:

1. **`trail.age` – nyom regenerációja és benövése** (ebben a lépésben). Hatókör: `trail`. Írja: `nyom.kopás`, `nyom.tétlenség`. Művelet: `trail.remove`.
2. `road.weather` – időjárási útromlás (hatókör: `road`).
3. `forest.stumps` – tönkök lebomlása.
4. `upkeep.service` / `upkeep.wear` – gépkopás (hatókör: `vehicle`).
5. `water.*` – a vízháztartás lépései (hatókör: `tile`, a vízmérleg megőrzésével: csak áthelyező `transfer` hatással).
6. `forest.health`, `forest.mortality`, `forest.regeneration` (hatókör: `stand`).
7. `economy.*`, `timber.deliver` (események és pénz).

A képlet-felülírások addig is működnek. Egy szabály átköltöztetése után a képletpontja a szabály egyik kifejezésévé válik.

## 4. Első szakasz – megvalósítva

- Modell: `RuleProject.Rules` (`WorldRule`: azonosító, név, leírás, be/ki, hatókör, időzítés, feltétel, hatások) és `RuleProject.Fields` (saját mezők).
- Futtató: `RuleEngine` a játékban. Beépített hatókörök: `world`, `tile`, `trail`. Mezőregiszter olvasással és írással. Saját mezők mentése a checkpointban.
- Átköltöztetve: `trail.age`. A natív `AgeSkidTrails` hívás megszűnt; az alapprojekt tartalmazza adatként.
- Editor: Szabályok › **Szabálykönyv** lap. Szabálylista, a négy rész („Mikor / Hol / Ha / Akkor”) űrlapként, mezőkatalógus a hatókör olvasható és írható neveivel, saját mezők felvétele, beépítettre visszaállítás.
