# Editor: felderíthető objektummodell és szabályszerkesztés

Állapot: 2026-10-13. Előzmény: [rule-building-blocks.md](rule-building-blocks.md) (a szabály = mikor · hol · ha · akkor).

## 1. A probléma

A szabálykönyv eddig lapos listákra épült. A hatókörök (`world`, `tile`, `trail`) és mezőik kézzel voltak felsorolva a
`RuleEngine`-ben, a „Használható nevek” panel pedig egyetlen hosszú lista volt. Ebből nem derült ki:

- milyen objektumok vannak a játékban, és ezek hogyan kapcsolódnak egymáshoz (*a fa egy csempén áll*);
- melyik mező honnan jön (*a fa kora a „növény” tulajdonsága, nem csak a fáé*);
- mit lehet írni, és mit csak olvasni;
- milyen szabályok futnak már egy adott objektumon.

## 2. A cél: a játék kódja írja le önmagát

A játék kódjában minden olyan objektumfajta, amelyre szabályt lehet írni, egy **megjelölt interfész**. Az öröklés
maga a hierarchia. Az editor reflexióval felderíti ezeket, így egy új mező vagy objektumfajta editorkód nélkül jelenik meg.

```
Világobjektum (absztrakt)          IWorldObject
├─ Hely (absztrakt)                IPlace
│  └─ Csempe környezete            ITileEnvironment   gyökérzónavíz, vízellátottság, pangóvíz…
├─ Növény (absztrakt)              IPlant             kor, egészség✎, → csempe
│  └─ Minden fa                    ITree              magasság, átmérő, fény, víz, stresszév, árnyéktűrés
└─ Jármű (absztrakt)               IVehicle           sebesség, megtett út, üzemanyag, útkopottság
   └─ Minden teherautó             ITruck             rakomány, telítettség, üres tömeg
      └─ (következő) Kihordó       IForwarder         daru, rakfelület…  → egy konkrét gép
```

✎ = írható (az interfész tulajdonságának van settere).

### 2.1 A szerződés (`ForesTycoon.Rules/RuleSchema.cs`)

| Attribútum | Hová | Jelentés |
| --- | --- | --- |
| `[RuleEntity(id, név)]` | interfész | Objektumfajta. `Abstract = true`: csak mezőket csoportosít; különben szabály-hatókör. |
| `[RuleField(név, súgó)]` | tulajdonság | Szám. `Unit`, `Min`, `Max`; setter esetén írható, az írás a `Min–Max` közé szorul. |
| `[RuleRelation(név, súgó)]` | tulajdonság (másik objektum) | Kapcsolat. A cél mezői `név.mező` alakban olvashatók: `csempe.vízellátottság`. |
| `[RuleAction(név, súgó)]` | `bool Metódus()` | Művelet; `true`, ha a világ szerkezete változott. |

`RuleSchema.Discover(assembly)` adja a fajták listáját: szülők, öröklött és saját mezők (melyik szintről jönnek),
kapcsolatok és műveletek. Ez a `ForesTycoon.Rules` könyvtárban van, így a játék és az editor ugyanazt a modellt látja.

### 2.2 A játék oldala (`ForesTycoon/World/RuleEntities.cs`)

- Az interfészek mellett egy-egy **nézetosztály** (`TreeView`, `TruckView`, `TileView`) valósítja meg őket az élő
  világon; elemenként újra van kötve (`Bind(world, id)`), nem foglal memóriát elemenként.
- `GameWorld.DiscoveredScopes()` a sémából állítja elő a `RuleScope`-okat: a getterekből/setterekből lefordított
  delegáltak, a kapcsolatokból előtagos mezők. A `RuleEngine` ezeket a kézi hatókörök mellé teszi.
- A szabálymotor elemenként csak a szabályban **ténylegesen említett** mezőket olvassa. Egy fán futó szabály több
  ezer elemet jár be, ezért ez fontos.

Új objektum felvétele: interfész attribútumokkal → nézetosztály → egy sor a `DiscoveredScopes`-ban. Az editorhoz nem kell nyúlni.

### 2.3 Nevek

Az objektumok és mezők neve angol, és a kódból származik: a képletnév a tulajdonság neve camelCase alakban
(`WaterSupply` → `waterSupply`), a megjelenítési név szavakra bontva („Water supply”), a fajta neve az interfészé `I` nélkül
(`ITree` → „Tree”). A `Name` és a `Display` attribútumparaméter felülírhatja őket.

Az ok-okozati térkép és a görbecsaládok terve: https://claude.ai/artifact/Ff57msZgw3tck42Z4ePCF5

## 3. Az editor kinézete

**Szabályok › Objektumok** (új lap):

```
┌ Objektumok a játékból ──┐┌ Világobjektum › Növény › Minden fa ─────────────────────────────┐
│ ▾ Világobjektum         ││ Egyedi fa: méretei, erőforrásai és stressze…                     │
│   ▾ Hely                ││ Hatókör: „Minden fa” · most 1834 fa · kód: ITree                 │
│      Csempe környezete  ││ Mezők                                                           │
│   ▾ Növény              ││  Név          Egység  Írható  Örökölt innen  Leírás              │
│    ▸ Minden fa  ◀       ││  azonosító            –       Világobjektum  …                   │
│   ▾ Jármű               ││  kor          erdőév  –       Növény         …                   │
│      Minden teherautó   ││  egészség     0–1     igen    Növény         …                   │
│                         ││  magasság     m       –       saját          …                   │
│ (dőlt = csoportosító)   ││ Kapcsolatok:  csempe → Csempe környezete                         │
│                         ││   Képletben: csempe.gyökérzónavíz, csempe.vízellátottság, …       │
│                         ││ Műveletek: nincs                                                │
│                         ││ Szabályok ezen az objektumon:  Aszálykár a fákon (kikapcsolva)   │
│                         ││ [Új szabály ezen: Minden fa]                                     │
└─────────────────────────┘└─────────────────────────────────────────────────────────────────┘
```

**Szabálykönyv** – a „HOL” választó két csoportot mutat: *Térkép* (a régi kézi hatókörök) és *Objektumok* (a felderített
fa, csak a konkrét fajták választhatók). Alatta a származási lánc és egy „objektum” gomb, amely az Objektumok lapra ugrik.

Következő lépések az editorban:
1. A „Használható nevek” panel csoportosítása: *saját · örökölt (Növény) · kapcsolat (csempe →) · hangolás · készletek*.
2. Kapcsolatok követése több szinten (`csempe.út.állapot`), és fordított irányban összesítéssel (`csempe.fák.átlag(egészség)`).
3. Konkrét példány kijelölése a 3D nézetben (*ez a fa / ez a kihordó*), és a szabály kiértékelése rajta élőben.
4. Szabály szűrése objektumtípusra: `Minden fa` → `Minden kocsánytalan tölgy` (faj mint altípus vagy szűrő).

## 4. A minta: „Aszálykár a fákon” (`tree.drought`)

Beépített, **alapból kikapcsolt** szabály, így a nyitó jelenet és a játék változatlanul fut. Az editorban
bekapcsolható.

| | |
| --- | --- |
| MIKOR | minden lépésben |
| HOL | Minden fa (Világobjektum › Növény › Minden fa) |
| HA | `tile.waterSupply < 0,2` — *kapcsolaton át* olvassa a fa csempéjének talajvizét |
| AKKOR | `health += -0,3 * (1 - tile.waterSupply / 0,2) * (1 - 0,5 * shadeTolerance) * lépés` |

Mit mutat meg:
- **öröklés:** az `egészség` és a `kor` a *Növény* mezője, a fa örökli;
- **kapcsolat:** a fa a környezetét a `csempe` kapcsolaton át látja;
- **írás a natív állapotba:** a havi natív növekedés és pusztulás a csökkent egészséget látja, a mentés változatlan.

## 5. Átköltöztetés, lépésenként

1. ✅ Séma és felderítés, `tree` és `truck` hatókör, Objektumok lap, mintaszabály.
2. A `tile` és `trail` kézi hatókörök átírása interfészre (`ITile : ITileEnvironment`, `ISkidTrail`); a meglévő
   mezőnevek megmaradnak, a projektek kompatibilisek.
3. `IVehicle → ITruck → IForwarder`, `IMachine` (feldolgozó): írható mezők (kopás, üzemanyag) és műveletek (javítás).
   Itt jutunk el a konkrét kihordó gépig.
4. `IStump`, `IDeadTree` (`forest.stumps` átköltöztetése), `IRoad`, `IDepot`, `ISawmill`.
5. Időzítés (`month`, `year`) és események; ezek a fák havi szabályaihoz kellenek, a lépésenkénti bejárás helyett.

## 6. Korlátok (jelenleg)

- A fák elemsorszáma lépésenként újraszámolódik (csempe, majd tárolási sorrend). Saját mező (`saját.*`) fán ezért még
  nem tartós: fapusztulás után eltolódhat. Ehhez stabil fa-azonosító szerinti tárolás kell (a fának van `Id`-ja).
- A kapcsolatok egy szint mélyek és csak olvashatók.
- A teherautó mezői csak olvashatók.

## 7. Görbék és ok-okozati térkép (megvalósítva)

- **Görbék a képletnyelvben** (`ForesTycoon.Rules/RuleCurve.cs`, `RuleFormula`): `curve.linear`, `curve.step`, `curve.smooth`,
  `curve.saturate`, `curve.decay`, `curve.bell`, `curve.points`. Első érték a bemenet, utána a pontok. A meglévő korlátos
  műveletekre fordulnak, a futtatónak nem kellett új elem. A képlet a görbe után szorzót kaphat (`* lépés`).
- **Görbeszerkesztő** a Szabálykönyvben és a térképen: családválasztó képekkel, húzható pontok, egérrel leolvasható érték,
  pontos számok. A tárolt alak a képletszöveg; a „képletként” gomb azt mutatja.
- **Ok-okozati térkép** (Szabályok › Ok-okozati térkép, `RuleCausalMap`): a csomópontok az objektummodell mezői, a nyilak
  a szabályok. A natív kapcsolatokat a kód deklarálja (`[RuleCause("tree.light", +1, "forest.growth")]` a mezőn), a
  szabálykönyv kapcsolatait a képletekből olvassa ki; görbénél az előjel a görbe irányából jön, máskor „±”.
  A visszacsatolási hurkok listázva, kiemelhetők; egy szabálykönyv-nyílra kattintva a görbe ott helyben szerkeszthető.
- Az aszálykár-minta képlete: `curve.linear(tile.waterSupply; 0; -0,3; 0,2; 0) * (1 - 0,5 * shadeTolerance) * lépés`.
