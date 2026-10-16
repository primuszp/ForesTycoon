# Gráfos szabályok és egyenletes motorfuttatás — 2026-10-16

Forráskód-átvizsgálás a korábbi szabályátvezetések eltávolítása után. Ez állapotfelmérés
és megvalósítási sorrend, nem új futtatómotor bevezetésének beszámolója.

**Későbbi termőhelyi átvezetés:** az alkalmasság, fajfüggő vízválasz, aszály- és pangóvízsimítás közös projektgráfokból fut. A régi Fitness, WaterResponse, külön fajajánló-képlet és WaterFormulas törölve. A lekérdezések egy objektumra korlátozottak; a bejárások szeleteltek, a mentés és a naplóból visszajátszás tesztelt. [Részletek](site-water-response-rules.md). A katalógus 15 szerkeszthető gráfos folyamatot és 51 natív folyamatot tartalmaz; ez nem jelenti azt, hogy az egész játék szabályosítása kész.

**Későbbi forgalmi átvezetés:** az út- és nyomkopás közös `traffic.wear`
áthaladási szabályból fut. A projekt `Road` ága, a külön útkopás-fordító és
szerkesztő, a `road.trafficWear` viselkedésfelülírás és a natív `DriveSkidTrail`
számítás törölve. Az eseményfuttatás a közös RuleEngine szeletelt munkáját
használja; a mentés és a projektcsere teljes művelethatárra vár. A katalógus
most 81 tétel: 56 NativeCode, 8 EditableGraph, 5 EditableExpressions,
1 EditableController, 11 Presentation. Az alábbi eredeti audit forgalmi sora,
első átfedése, külön útkopásszerkesztője és kizárólagos `step` időzítése már
meghaladott; a többi átvezetési feladat továbbra is hátravan.
Lásd [a megvalósított eseményszabályt és ellenőrzéseit](traffic-rules.md).

**Későbbi megvalósítás ugyanazon a napon:** a folyamatos szimuláció fő folyamatai
közös, logikai ticken belüli munkaszeletelést kaptak. Az alábbi táblák az átépítés
előtti auditot őrzik; a mostani végrehajtás, mentési határ és a megmaradó korlátok
az [engine architektúrában](engine-architecture.md#egyenletes-szimulációs-munkavégzés-2026-10-16)
olvashatók. A beégetett játékszabályok gráfos átvezetése ettől még hátravan.

## Jelenlegi lefedettség

A projekt szabálykönyvéből fut a hét vízáramlási minta, az időjárási útromlás,
a nyomregeneráció, a tönklebomlás, a közös forgalmi kopás, valamint a fák
növekedése, egészsége, stressze, elhalása és a holtfa eltűnése. Ezeknél a hatások, görbék, feltételek és az
eltávolítás szerkeszthetők; a régi natív megfelelőjük már nincs bekötve.

A katalógus 83 bejegyzése közül 53 `NativeCode`, 13 `EditableGraph`,
5 `EditableExpressions`, 1 `EditableController`, 11 `Presentation` jelölésű.
Ez **nem készültségi százalék**: a natív tételek között motormechanizmus és
katalógusadat is van; a szerkeszthető jelölés pedig felülírási pontot is jelenthet.
A katalógus nem automatikus bizonyíték a gráfos felület elérhetőségére.

A fák havi gráfjainak szerkesztése, futtatási sorrendje és ellenőrzése: [faéletciklus](tree-vitality-rules.md).

A növekedés és a közös szerkesztő friss állapota: [fanövekedési gráf](tree-growth-rules.md).
A lenti eredeti editor-audit történeti: a külön képletes szabálypanel megszűnt,
a folyamatkapcsolatok, vezérlők, készletek és áramlások a közös szabálylistából nyílnak.
A növekedési döntés és a `forest.growth` natív felülírás szintén megszűnt.

## Ami még hiányzik

| Terület | Jelenlegi helyzet, forrás | Szükséges átvezetés |
| --- | --- | --- |
| Újulat | `ForestSystem.cs`: `IsSeedSource`, `TryRegenerate`. Beégetett fajválasztás, alkalmassági küszöbök és havi esély. | Magadó feltétel, fajjelöltek kiválasztása, stabil sorsolás, új egyed létrehozása. |
| Termőhely és versengés | `ForestSystem.cs`: `Suitability`; `ForestCompetition.cs`; `ForestHydrology.cs`. Natív fény-, tér-, lombborítás- és LAI-válaszok. | Geometriai lekérdezések motorprimitívként; az ökológiai válaszgörbék projektadatként. Fafaj és talaj kapcsolt objektumként. |
| Vízválasz és talajpuhulás | `EnvironmentSystem.cs`: `WaterStep`, `WaterResponse`, `Trafficability`. Natív aszály-/pangóvíz-simítás és járhatósági bemenetek; `water.stress` felülírás is létezik. | Származtatott mező gráfból, időbeli közelítés művelettel; a havi és pillanatnyi fogyasztó ugyanazt a definíciót használja. |
| Hóátrendezés | `EnvironmentSystem.cs`: `RedistributeSnow`. Még natív, szél-, domborzat- és növényzetfüggő hómozgatás. | Szélmezők, irány szerinti szomszédválasztás, megmaradó készletátadás; a meglévő vízáramlás ne számolja még egyszer. |
| Időjárás és helyi klíma | `WeatherSystem.cs`, `RegionalClimate.cs`. Évszakok, eseményesélyek, időtartam, hőmérséklet és párologtató képesség kódból. | Időzített események és menthető sorsolás, évszakgörbék, helyi klímahatások. Az idő és a véletlengenerátor a motoré. |
| Forgalmi út- és nyomkopás | `GameWorld.Rules.cs`: `ApplyTrafficWear`; `TerrainMap.SkidTrails.cs`: `DriveSkidTrail`. Külön útkopásgráf, utána viselkedésgráf; nyomon natív vályúnövelés. | Áthaladás esemény, jármű/út/nyom kapcsolat; egyetlen gráf a kopásra és a használat óta eltelt idő nullázására. |
| Fenntartás | `VehicleUpkeep.cs`. Kopás, kopásnégyzetes meghibásodási esély, javítás és szerviz kódban; a számok hangolhatók. | Terhelésből kopás; mentett véletlenes meghibásodás; javítási idő, állapot és költség hatásgráfból. |
| Járműmozgás és üzemanyag | `VehicleDynamics.cs`, `Vehicle.cs`. Paraméterezhető műszaki adatok, de a menetellenállás, sebességválasztás és fogyasztási összefüggés natív. | Sebességcél, tapadás-, terhelés- és fogyasztásválaszok gráfból; korlátos fizikai integráció maradhat motorfeladat. |
| Gépek, rakodás, malom | `ForestryLogistics.Machines.cs`, `.Fleet.cs`, `ForwarderLoading.cs`, `ForestryLogistics.cs`. Vezérlő- és eredményfelülírások vannak; a natív automata és a malom 0,25 m³/s alaptempója megmaradt. | Ugyanazon felületen események, állapotok, átadási sebességek és munkaigény; az alapviselkedés is látható projektgráf. |
| Gazdaság és építés | `GameWorld.cs`, `ForestryLogistics.Stacks.cs`, `RoadCosts.cs`. Ár-/költségparaméterek vannak, jogosultság és elszámolási döntések kódban. | Építés/telepítés/javítás/eladás eseményfeltételei és költség/bevétel gráfból; mennyiség–érték könyvelés motorprimitív marad. |
| Vadak | `WildlifeSystem.cs`. Éhség, táplálékfogyás és regeneráció, élőhely, célválasztás kódban. Nincs hozzá konkrét szabálykönyvi objektumnézet. | Állat és táplálékkészlet, kapcsolt csempe, szükséglet- és választási gráf; a járás interpolációja megjelenítési feladat. |
| Kezdővilág és küldetések | A generálás natív; a küldetések külön metrika/küszöb/határidő adatok, nem általános hatásgráfok. | Ha ezek is a „minden szabály” részei: generálási profilok és cél/esemény/siker/kudarc műveletek ugyanabban a szabálymodellben. |

## Igazolt átfedések és eltérő utak

1. `ApplyTrafficWear`: előbb `roadRule.Evaluate`, majd `behaviorPolicy.Evaluate`
   ugyanahhoz a kopáshoz. Ez egymásra épülő két szerkeszthető számítás, nem két
   összeadott útkár; az egységesítésnél mindkettőt ki kell váltani.
2. `tree.drought` saját szabály írhatja a fa egészségét, miközben a natív havi
   folyamat is írja az egészséget és stresszt. További általános gráfokkal a méret
   is írható, miközben a natív növekedés tovább működik. Ez ma engedélyezett
   együttműködés, de nem teljes szabálytulajdon-átruházás.
3. `GrowthFactor` alkalmazhatja a `water.stress` felülírást; a
   `CurrentWaterFactor` közvetlenül `WaterResponse`-t hív. Helyi beavatkozás utáni
   `currentConditions` rátafrissítés így más definíciós utat követhet.
4. Két készletáramlás-végrehajtó él: `DynamicsRuntime.Step` globális készletekre,
   `RuleEngine.Move` objektumkészletekre. Az előbbi közös előző állapotból arányosan
   korlátoz, az utóbbi sorban, az addigi állapoton ad át. Nem szabad pusztán egy
   függvényre cserélni őket: előbb a konfliktuskezelés jelentését kell rögzíteni.
5. A közvetlen mezőolvasók és az objektumnézetek két hozzáférési út ugyanahhoz
   az állapothoz, teljesítménycélból, egyezést ellenőrző tesztekkel. Ez önmagában
   nem párhuzamos játékszabály.

## Mi hiányzik az egységes szerkesztőből?

- Egyetlen szabálylista és gráffelület a hatásokhoz, eseményekhez, származtatott
  értékekhez és vezérlőállapotokhoz. Ma a projekt `Road`, `Behaviors`, `Dynamics`
  és `Rules` adatot is ment; a Szabályok felület fő útja csak a szabálykönyvet mutatja.
- A `WorldRule.Schedules` jelenleg kizárólag `step`. Kell eseményindítás
  (áthaladás, kivágás, rakodás), havi/órás időzítés és egyszeri állapotbelépés.
- Objektumválasztás → kapcsolt objektum → mező, angol stabil azonosítóval,
  kijelzett névvel, mértékegységgel és segítséggel. Csak a sémában jelölt mezők
  jelennek meg automatikusan, nem a játék minden C# tulajdonsága.
- Műveletek: fa létrehozása, elhalás, holtfa eltávolítása, állapotváltás,
  pénzügyi tranzakció; jelenleg a konkrét sémában csak nyom- és tönkeltávolítás van.
- Emlékezet: időzítő, késleltetés, közelítés, előző érték, integrál és
  objektumhoz/eseményhez kötött determinisztikus sorsolás, képletírás nélkül.
- Összetett feltételek, irányított szomszédválasztás és fajválasztás vizuálisan.
- Állapotírási tulajdon és konfliktusok: a natív folyamatokat is feltüntető
  „ki írja ezt?” nézet; sorrend és összeadás/felülírás/átadás világos jelentése.
- A képletes szabályoknak még külön szerkesztési ága van
  (`RuleEditorView.RuleBook.cs`); az `Influence == null` szabály nem a közös
  hatásgráfot kapja. Ezt és a külön útkopás-/viselkedés-/dinamikaszerkesztőt a
  közös felületnek kell kiváltania, a működő projektadatok megőrzésével.
- A próba mutassa a teljes tényleges hatást, a többi íróval együtt, és a szeletelt
  végrehajtás állását. A gráfmagyarázat, előnézet és játék azonos definíciót használjon.

## A szétosztott végrehajtás jelenlegi határai

| Folyamat | Most | Hiány |
| --- | --- | --- |
| Csempés gráfszabály | `Begin` / `Continue`; stabil csempe-ID; mentett kurzor és olvasott világértékek. | A szabálykezdés, gyűjtemények és összesítések költsége is legyen korlátos. |
| Út, nyom, fa, tönk, gép, teherautó gráf | `Stable` hamis; a megkezdett szabály teljes elemhalmaza lefut. | Stabil objektumazonosítós munkalista; eltűnés/létrejövés kezelése és menthető kurzor. A `Stable = true` egyszerű beállítása hibás lenne: fánál/tönknél tömbindexek változhatnak. |
| Esedékes új szabálylépés | `GameWorld.WeatherRoads` előbb `Continue(int.MaxValue)`-val befejezi az előzőt. | Torlódási szabály; ne lehessen a szeletelést korlátlan befejezéssel megkerülni. |
| Szabályprojekt cseréje | `FinishRuleStep` szintén egyszerre fejezi be a függő lépést. | Előkészített csere, biztonságos publikálási határ és látható alkalmazási állapot. |
| Erdőhónap | `ForestMonthlyPreparation` szétosztja a geometria/versengés előkészítését. | `StepIndividualMonth` még teljes ciklusokban zár, frissít, elhaltakat töröl, újulatot képez és statisztikát számol; érvénytelen előkészítésnél teljes újraszámítás lehet. |
| Környezet | `WaterStep`, `RefreshVegetation`, `RedistributeSnow`, `Summarize` teljes raszterciklusok. | Környezeti intervallumon belüli munkaszeletek, következetes havi integrál és hóátadás. |
| Logisztika, hálózat, vadak | Szinkron frissítések/útkeresések; `WorldSystemCollection` sorban hívja az egész `Update`-eket. | Útkeresés és hálózati frissítés folytatható állapottal; a flotta és élőhely növekvő munkája is korlátos. |
| Globális dinamika | `DynamicsRuntime.Step` minden készletet és áramlást egy tickben végigjár. | Méretnövekedésre korlátos kiértékelés és közös állapotból történő publikálás. |

Az ökológiai `EcologicalProcessRuntime` függőségi sorrendet és időhatárokat kezel,
de a folyamatok `Advance` hívását egészben futtatja: nem általános CPU-munkaütemező.
A 30 Hz-es fix tick és a járművek 1/30 s al-lépése numerikus időfelbontás;
önmagában egyik sem osztja el a sok objektumon végzett munkát több tickre.

## Kötelező motorirány és megvalósítási sorrend

Minden növekvő szimulációs munka szétosztható legyen, függetlenül attól, hogy
gráfból vagy motorprimitívből ered. Azonnali lekérdezés csak korlátos költséggel
futhat. Az időérzékeny mozgás minden tickben frissülhet; az ütemezés nem ritkíthatja
észrevétlenül a fizikát, nem dobhat el szimulációs időt és nem változtathat rátát.

1. **P0 — folytatható munkamodell.** Stabil ID, folyamat/állapot/kurzor,
   függőségek, bemeneti időintervallum, átadási napló és torlódási szabály.
   A költségkeretbe a munkalista-előkészítés és a végső publikálás is számítson bele.
   A determinisztikus munkakeret döntsön a szeletekről, ne a gépen mért milliszekundum.
   A tényleges idő mérés és visszajelzés, nem visszajátszást módosító vezérlés.
2. **P0 — dinamikus objektumok és hónapzárás.** Először a meglévő szabályok
   szeletelési kivételei, korlátlan befejezései és az erdő havi nagy ciklusai
   szűnjenek meg. Az aktuális szeletelt, élő állapotot olvasó jelentéstől való
   eltérést külön modellváltozásként kell kezelni; teljes pillanatképre váltás nem
   puszta optimalizálás. Kötelező az intervallumvégi függőségek helyessége.
3. **P1 — egységes gráf és események.** Egy állapotváltozást egy definíció
   birtokoljon; az átvitt natív alapképlet törlendő. Első csomag: egészség,
   stressz, elhalás és holtfa. Következő: növekedés/újulat, hó és vízválasz,
   forgalom/fenntartás, majd logisztika/gazdaság/időjárás/vadak.
4. **P1 — közös átadás és diagnosztika.** Az objektum- és világkészletek
   konfliktuskezelését egyeztetni; teljes írólista és hozzájárulások az Editorban.
5. **P2 — generálás és küldetésesemények.** A végső teljes lefedettséghez,
   ha a világ keletkezésének és céljainak döntései is ugyanitt szerkesztendők.

Minden átvezetés kész feltétele: szerkeszthető alapgráf, közös kijelzett nevek és
görbék, nincs második aktív natív képlet, kikapcsolási próba, folytatható végrehajtás,
közbenső mentés/betöltés és determinisztikus visszajátszás. Tömeg-/térfogat-/érték-
megmaradás tesztelendő. Törlés, létrehozás, hónaphatár, gyorsított idő és projektcsere
közben is működjön. Nagy pálya/erdő/flotta mellett folyamatonként p95/p99/max
tickköltség és hátralék mérendő; az új követelményhez most nem készült új benchmark.

A cél nem minden C# eltávolítása: gráfkiértékelés, készletkönyvelés, geometriai
lekérdezés, útkereső algoritmus, fizikai integrátor, mentés és rajzolás marad motor.
A játékmeneti döntések és válaszfüggvények kerülnek a projektbe. Egy új folyamat
gráfos átvezetése az egyenletes futtatás nélkül nem tekinthető késznek.
