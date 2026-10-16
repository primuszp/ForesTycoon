# Természetes erdőmegújulás – 2026-10-16

A játék természetes újulata a projekt hét közös válaszgráfjából fut. A Szabályok
listából ugyanaz a gráfszerkesztő nyílik meg, mint a termőhelyi és versengési
válaszokhoz. Nincs külön újulatszerkesztő vagy natív tartalék szabály.
Forrás: `ForesTycoon.Rules/ForestRegenerationRules.cs`.

| Gráf | Mit dönt el? | Alapértelmezés |
| --- | --- | --- |
| `forest.seedCrowding` | A szomszédos lombterhelés válasza | Átlagos biomassza / 1,2, 0–1 közé korlátozva |
| `forest.seedSupply` | Egy szomszédos állomány mennyi magot ad | Érettség legalább 1, egészség legalább 0,45: egy egység mag |
| `forest.seedSelection` | Melyik magadó fafaja telepedjen meg | Termőhelyi alkalmasság × forrás egészsége × árnyéktűrési válasz |
| `forest.germination` | Legyen-e megtelepedés ebben a hónapban | Alkalmasság legalább 0,18; vízválasz legalább 0,3; sorsolás az éves esély időarányos részére |
| `forest.seedlingCount` | Hány új faegyed jelenjen meg | Egy csemete |
| `forest.seedlingAge` | A csemeték kezdő átlagéletkora | Egy erdőhónap |
| `forest.seedlingHealth` | A csemeték kezdő egészsége | 0,55 + alkalmasság × 0,35 |

Az éves alap-esély a gráfon látható minimum: 0,30 vagy a magellátottság × 0,055.
A képletek csak a kezdő gráfok létrehozására szolgálnak; játék közben a mentett
csomópontok futnak. Szerkesztéskor nincs szükség képletírásra. A forrásoknál
előbb objektumot, majd mezőt választasz; a számértékeket beírhatod vagy csúszkával
állíthatod, az összefüggést a közös művelet- és görbecsomópontokkal alakíthatod.
A nulla kimenettel a magadás, kiválasztás, csírázás, létszám vagy egészség
külön is leállítható; nem veszi át a helyét beégetett számítás.

## Elérhető objektumok és tényleges bemenetek

- **Magadó állomány:** `source.age`, `source.maturity`, `source.health`,
  `source.biomass`, `source.distance`. A távolság a két csempe középpontja közötti méter.
- **Célcsempe:** `tile.moisture`, `tile.fertility`, `tile.elevation`,
  `tile.suitability`, `tile.waterResponse`, `tile.crowding`, `tile.neighborBiomass`.
  Az alkalmasság és a vízválasz a már meglévő közös projektgráfok eredménye.
- **Fafaj:** `species.matureAge`, `species.shadeTolerance`, `species.maximumBiomass`.
- **Környezet:** `environment.radiation`, az elmúlt erdőhónap átlagos besugárzása.
- **Magok:** `seeds.supply`, `seeds.random`. A kiválasztásnál az adott forrás
  magmennyisége, a csírázásnál és kezdőállapotnál az összes forrás magmennyisége
  szerepel. A véletlen világseedből, célcsempéből és hónapból számolt, 0 ≤ érték < 1.
- **Időlépés:** `step.years`, egy erdőhónap hossza erdőévben.

A lombterhelésgráf a forráskiválasztás előtt fut, ezért csak a célcsempe
nedvességét és a szomszédok átlagos biomasszáját kínálja fel. A többi gráf
konkrét magadó állományt és fafajt kap. A névjegyzéket a
`RegenerationRuleSchema.cs` annotált interfészeiből a közös objektumfelderítő
olvassa; nincs második, kézzel karbantartott mezőválasztó.

## Mi marad a motorban?

A megtelepedés üres, erdőt fizikailag fogadni képes csempére történik; az út,
víz és épület jogosultságát a közös habitat határ biztosítja. A terjedési tér
**a négy közvetlen szomszéd**, nem tetszőleges sugarú keresés. Ezen belül a
forrásgráf távolságfüggő súlyt vagy kizárást adhat. Nincs többhónapos magkészlet,
szél szerinti távoli magterjedés vagy újulat már foglalt csempén.

A pozitív magadó pontszámú jelöltek közül a legnagyobb kiválasztási pontszám
nyer. Egyenlőségnél a stabil szomszédsorrend dönt. Ez a redukció és a
létrehozási művelet motorprimitív; a pontszám összefüggését a gráf adja.
A csírázásgráf pozitív értéke megtelepedést enged, nem automatikusan sorsolt
valószínűség: a sorsolás és az összehasonlítás maga is szerkeszthető csomópont.

A csemeteszám egészre kerekített, legfeljebb 128 egyed megtelepedésenként;
ez a létrehozási munka fix felső határa. Az újulat létrehozásánál a régi,
biomasszából és érettségből becsült létszámképletet nem futtatjuk.
A közös `ForestTreeGrowth.Initial` továbbra is a faj és a kezdő életkor alapján
ad kezdő méretet; a seedelt hely-, méret- és életkoreltérések is megmaradtak.
Ezek nem külön újulati növekedési képletek, de a kezdő méretgörbék általános
gráfosítása még külön feladat. Az induló erdő és a játékosi ültetés létszáma
szintén nem része ennek az átállításnak.

## Szeletelés, mentés, visszajátszás

A hónapzárás a magadó állományokról csempénként másolt, közös pillanatképet
használ. A későbbi elhalás vagy egy most születő csemete nem módosítja ugyanannak
a hónapnak a magellátottságát. Nincs csempesorrendtől függő, azonnali továbbterjedés.
A célcsempék és a szomszédok vizsgálata külön munkaegységeket ad; a létrehozott
csemeték erőforrás-frissítése is a szeletelt útvonalat használja. Nincs
korlátlan befejezési adapter az újulat futási ágában. A teljes csempetér
bejárása is szeletelt, ezért a fiatal/gyenge állományokat nem vágja le
láthatatlan, régi magadó-előszűrés.

Mentés a meglévő `AtTickBoundary` határon történik: a hónap közben kért mentés
várakozik, és nem fejezi be korlátlan munkával a hónapot. A pillanatkép ideiglenes
munkamemória; nincs félig lefutott iterátor szerializálása. A kész ticken belüli
havi előkészítési gyorsítótár továbbra is menthető. A futtató verziója
`forestycoon-simulation/2026-10-16.8`; nincs régi újulati szabályokat átvezető
betöltési ág.

A szállított jelenet és a referencia saját pályája, küldetései és eddigi gráfjai
megmaradtak. A frissen, külön mappába rögzített nyitóállapotban csak a
`Behaviors` lenyomat változott. A helyi editorprojekt frissítése előtti másolat:
`artifacts/regeneration-project-backup/project.before-regeneration.json`.

## Ellenőrzés

A `ForestRegenerationGraphTests` a fiatal magadó engedélyezését, minden érdemi
szakasz letiltását, a nedvességből vett kezdőegészséget, távolsági kizárást,
kezdő életkort, pontos egyedszámot és több faj közötti kiválasztást is vizsgálja.
A közös diagnózis ugyanazt a lombterhelésgráfot futtatja. Az azonos hónapon
belüli továbbterjedés tiltása, a három egységes munkakeret, a hónaphatár közben
kért mentés, a visszatöltés és a parancsnaplós visszajátszás is tesztelt.
A felfedezett objektum-/mezőneveket a teljes válaszgráfkészleten ellenőrizzük.

Az editor füsttesztje mind a hét új gráfot megnyitja és lefényképezi:
`artifacts/rule-editor/regeneration-*-graph.png`.
A végső teszt- és mérési naplók: `artifacts/regeneration-tests-final.txt`,
`artifacts/regeneration-editor-smoke-final.txt`, `artifacts/regeneration-benchmark.txt`.
A nagy pályás CPU-mérés parancsa: `--regeneration-benchmark`.


## Nagy pályás CPU-mérés

Release, .NET 8.0.32, `DOTNET_TieredCompilation=0`; 120 játék-másodperces erdőév,
60 bemelegítő és 330 mért tick, sűrű LargeMixed erdő. A teljes tickes adapter
csak a headless összehasonlítás része; az interaktív út 64 egységes munkaszeleteket
futtat. A benchmark a teljes szimulációt és egy erdőhónap-határt méri, a
memóriafoglalással és GC-vel együtt, nem kizárólag az újulat gráfjait.
A tesztcsomaggal való párhuzamos futást leállítottuk; az alábbi végső kör
külön, futó tesztcsomag nélkül készült.

| Pálya, időjárás | Fák | Teljes tick p95 / p99 / max (ms) | 64 egységes szelet p95 / p99 / max (ms) | Állapotazonosság |
| --- | ---: | --- | --- | --- |
| 64×64, eső | 15 797 | 17,086 / 53,876 / 641,368 | 0,045 / 0,062 / 15,334 | Pontos |
| 64×64, hó | 15 797 | 30,017 / 92,081 / 660,667 | 0,053 / 0,102 / 17,833 | Pontos |
| 256×256, eső | 247 238 | 273,600 / 885,143 / 10 520,390 | 0,046 / 0,064 / 23,094 | Pontos |
| 256×256, hó | 247 238 | 249,340 / 927,637 / 12 276,570 | 0,047 / 0,094 / 34,576 | Pontos |

A teljes checkpoint és a vízkészlet-/áramlásnapló-/útállapot lenyomata minden
esetben bitpontosan egyezik. A szeletelés a munkát osztja fel, nem csökkenti
a teljes CPU-igényt. **Ritka 34,576 ms-os kiugrás továbbra is előfordult**;
ez nem kemény képkockaidő-garancia és nem az akadás teljes megszűnésének bizonyítéka.
A mérés nem választja szét a GC-t, az operációs rendszer ütemezését és az egyes
munkaegységeket, ezért a kiugrás okát külön profilozás nélkül nem állítjuk.
A természetes alapgráfokkal mért sűrű pályák nem tömeges, kényszerített újulatpróbák;
a tényleges megtelepedést és a szerkesztett csemeteszámot a célzott tesztek fedik.

A teljes tesztcsomag 1852/1852 tesztje, az utolsó metaadat-frissítés után a
12/12 célzott teszt és mindkét editorfüstteszt sikeres. A helyi projektet nemcsak
validáltuk, hanem beágyazott állapotával betöltöttük és egy ticken át futtattuk
(`artifacts/regeneration-local-trace.json`). Ennek során a korábbi termőhelyi és
versengési gráfok szöveges enumait is javítottuk a helyi snapshot előírt numerikus
formátumára. A gráfok tartalma megmaradt; ez egyszeri adatjavítás, nem új legacy
betöltési ág.


## Későbbi kezdőméret-átvezetés

A fent leírt közös fajméret-inicializáló időközben szintén három projektgráfra
váltott: kezdő magasság, átmérő és koronasugár. A natív méretképlet megszűnt,
a létrehozás egyedenként folytatható. A korábbi „kezdő méretgörbék még külön
feladat” megjegyzés történeti. A kezdő koreltérés, az induló állomány létszáma
és az ültetvény elrendezése változatlan. [Részletek](tree-initial-size-rules.md).
