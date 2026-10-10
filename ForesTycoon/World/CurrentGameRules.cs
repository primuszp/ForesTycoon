using System;
using System.Linq;

namespace ForesTycoon
{
    /// <summary>Inventory of implemented behavior. Values with public constants are read from the runtime itself.</summary>
    internal static class CurrentGameRules
    {
        internal static GameRuleCatalog Build(double forestYearSeconds = EcologyTime.DefaultGameSecondsPerYear,
            ClimateDefinition climate = null, SoilLandscapeDefinition soils = null, RuleModel roadTraffic = null, GameTuning tuning = null)
        {
            tuning ??= GameTuning.Default;
            var catalog = new GameRuleCatalog { RoadTrafficModel = roadTraffic?.Clone() ?? RuleModel.Default() };
            GameRuleDefinition Add(string id, string name, string module, string scope, string schedule, string file, string symbol,
                string reads, string writes, string description, string formula = "", GameRuleExecution execution = GameRuleExecution.NativeCode)
            {
                int index = catalog.Rules.Count(r => r.Module == module);
                var rule = new GameRuleDefinition { Id = id, Name = name, Module = module, Scope = scope, Schedule = schedule,
                    Reads = reads.Split('|', StringSplitOptions.RemoveEmptyEntries), Writes = writes.Split('|', StringSplitOptions.RemoveEmptyEntries),
                    Description = description, Formula = formula, Execution = execution,
                    X = 25 + index % 3 * 245, Y = 30 + index / 3 * 130 };
                rule.Sources.Add(new(file, symbol)); catalog.Rules.Add(rule); return rule;
            }
            void Param(GameRuleDefinition r, string name, double value, string unit) => r.Parameters.Add(new(name, value, unit));
            const string game = "ForesTycoon/World/GameWorld.cs", forest = "ForesTycoon.Ecology/Forest/ForestSystem.cs";
            const string individuals = "ForesTycoon.Ecology/Forest/ForestSystem.Individuals.cs", water = "ForesTycoon.Ecology/Water/EnvironmentSystem.cs";
            const string machine = "ForesTycoon/World/Cargo/ForestryLogistics.Machines.cs", fleet = "ForesTycoon/World/Cargo/ForestryLogistics.Fleet.cs";
            const string stacks = "ForesTycoon/World/Cargo/ForestryLogistics.Stacks.cs", dynamics = "ForesTycoon/World/Vehicles/VehicleDynamics.cs";
            const string upkeep = "ForesTycoon/World/Cargo/VehicleUpkeep.cs", logistics = "ForesTycoon/World/Cargo/ForestryLogistics.cs";

            var clock = Add("time.world", "Szimulációs órák és sorrend", "Idő és vezérlés", "Világ", "Fix világlépés", game, "VehicleTimeScale",
                "commands", "calendar.time|vehicle.time", "Ökoszisztéma, logisztika, útöregedés, vadak, majd a regisztrált rendszerek. A járművek és vadak külön, gyorsabb órát kapnak: alap 1× mellett természetes tempóban mozognak.");
            Param(clock, "Erdőév", forestYearSeconds, "játék-s");
            var phases = Add("time.ecology", "Ökológiai többütemű futtatás", "Idő és vezérlés", "Világ / hónaphatár", "0,5 s; havi határra bontva",
                "ForesTycoon.Ecology/Water/ForestEnvironmentCoordinator.cs", "ForestEnvironmentCoordinator",
                "calendar.time|forest.trees|water.root", "ecology.steps|forest.month", "Előkészítés → víz és időjárás → havi növényzet → havi integrálok törlése. A sorrend befolyásolja az eredményt.");
            Param(phases, "Alaplépés", EnvironmentSystem.StepSeconds, "játék-s");
            Add("commands.apply", "Játékosparancsok", "Idő és vezérlés", "Világ és kijelölt objektum", "Parancssor végrehajtásakor",
                "ForesTycoon/World/Commands/WorldCommandQueue.cs", "ExecutePending", "player.input", "commands",
                "Építés, bontás, javítás, ültetés, kitermelés kijelölése, járműutasítás és időjárási beavatkozás naplózható parancs.");
            Add("save.restore", "Checkpoint és visszajátszás", "Idő és vezérlés", "Világ", "Mentés / betöltés", game, "Load",
                "commands|calendar.time|forest.trees|water.root|road.condition|vehicle.cargo|economy.income", "save.snapshot",
                "Aktív szabálymodell, talaj és klíma, készletek, egyedek, véletlengenerátorok és függő parancsok megőrzése; hibánál izolált betöltés.");

            Add("terrain.generate", "Terep és vízmedencék", "Terep", "Node-rács / térképcella", "Új világ", "ForesTycoon.Map/Generation/TerrainGenerator.cs", "TerrainGenerator",
                "world.seed|terrain.settings", "terrain.height|terrain.moisture|terrain.water", "Seedből induló domborzat és hidrológia. A térképi nedvesség nem azonos az ökológiai gyökérzónavíz-készlettel.");
            Add("terrain.hydrology", "Folyók, medencék és kifolyók", "Terep", "Térképrács", "Generálás / terepváltozás", "ForesTycoon.Map/Generation/Hydrology.cs", "Hydrology",
                "terrain.height", "terrain.moisture|terrain.water|terrain.outlets", "Terepből származó állóvíz, folyók, nedvesség és a környezeti lefolyás határfeltételei.");
            Add("terrain.edit", "Helyi terepszerkesztés", "Terep", "Érintett node-ok és cellák", "Játékosparancs", game, "ExecuteElevationEdit",
                "commands|terrain.height|buildings.footprint|road.network", "terrain.height|forest.trees|water.routing",
                "Csak ténylegesen megváltozott cellák növényzete vész el, kitermelt készlet nélkül. Út- és épületmagasság védett; a többi erdő és a szimulációs idő megmarad.");
            var soilRule = Add("soil.catalog", "Talajprofilok és termőhely", "Talaj és klíma", "Térképcella", "Új világ", "ForesTycoon.Ecology/Soil/SoilLandscape.cs", "SoilLandscape",
                "world.seed|terrain.height|terrain.moisture", "soil.profile|soil.fertility", "Térben összefüggő, seedelt talajprofilok; telítettség, szabadföldi vízkapacitás, hervadáspont, beszivárgás, drénezés és termékenység.");
            soils ??= SoilLandscapeDefinition.Default;
            foreach (var profile in soils.Catalog.Profiles)
            {
                var p = profile.Properties;
                Param(soilRule, profile.Name + ": telítési készlet", p.Saturation, "mm");
                Param(soilRule, profile.Name + ": szabadföldi vízkapacitás", p.FieldCapacity, "mm");
                Param(soilRule, profile.Name + ": hervadáspont", p.WiltingPoint, "mm");
                Param(soilRule, profile.Name + ": beszivárgás", p.InfiltrationPerHour, "mm/környezeti óra");
                Param(soilRule, profile.Name + ": drénezés", p.DrainagePerHour, "mm/környezeti óra");
                Param(soilRule, profile.Name + ": termékenység", p.Fertility, "1");
            }
            var regional = Add("climate.regional", "Regionális klíma", "Talaj és klíma", "Térképcella", "Induló mezők + minden környezeti lépés",
                "ForesTycoon.Ecology/Climate/RegionalClimate.cs", "Cell", "world.seed|terrain.height|weather.forcing", "climate.local|climate.rainMultiplier",
                "Simított regionális hőmérséklet-, páratartalom- és esőeltérések. Magasság élőben hűti a helyi levegőt; terraform nem generál új klímamagot.", "T_helyi = T + eltérés - normalizált_magasság × hűtés");
            climate ??= ClimateDefinition.Default;
            Param(regional, "Régióméret", climate.RegionSizeTiles, "csempe"); Param(regional, "Hőmérsékleti kontraszt", climate.TemperatureContrast, "°C");
            Param(regional, "Csapadékkontraszt", climate.RainContrast, "1"); Param(regional, "Magassági hűtés", climate.ElevationCooling, "°C");
            Param(regional, "Páratartalom-kontraszt", climate.HumidityContrast, "1");
            Add("weather.events", "Seedelt időjárási események", "Talaj és klíma", "Világ", "Eseményhatárok és környezeti lépés",
                "ForesTycoon.Ecology/Climate/WeatherSystem.cs", "NextEvent", "world.seed|calendar.time|commands", "weather.rain|weather.forcing",
                "Évszakos, seedelt átmenetek: ősszel gyakori tartós eső, nyáron rövid zápor és erős vihar, télen havazás. A hó menthető vízkészlet, melegedéskor olvad. A csapadék rámpáinak integrálása pontos.",
                "T = 10 + 15 × sin(2π × (idő / erdőév - 0,125)); havazáskor T ≤ -1 °C; sugárzás = 1 - 0,75 × felhőzet");
            Add("weather.evaporation", "Légköri párologtató igény", "Talaj és klíma", "Térképcella", "Környezeti lépés",
                "ForesTycoon.Ecology/Climate/WeatherSystem.cs", "PotentialEvaporationPerHour", "climate.local|weather.forcing", "water.potential",
                "A talajpárolgás és a növényzeti vízigény ugyanabból a helyi légköri kényszerből származik.",
                "E = (0,12 + max(0,T) × 0,015) × sugárzás × (1 - 0,5 × pára) × (1 + 0,035 × szél)");

            Add("water.interception", "Csapadék és lombkorona-víz", "Vízháztartás", "Térképcella", "Környezeti lépés", water, "WaterStep",
                "weather.rain|climate.rainMultiplier|forest.canopy|water.canopy", "water.canopy|water.surface",
                "A lombkorona kapacitásáig felfogja az esőt, a többi a felszínre jut. Koronavesztéskor a többlet lecsepeg, a vízmérleg megmarad.", "felfogás = min(eső, max(0, kapacitás - lombvíz))");
            Add("water.infiltration", "Beszivárgás", "Vízháztartás", "Térképcella", "Környezeti lépés", water, "infiltration",
                "water.surface|water.root|soil.profile|road.network|buildings.footprint", "water.surface|water.root",
                "A felszíni víz a talaj szabad kapacitásáig szivárog. A burkolt felszínek beszivárgása 0,5 mm/környezeti óra.", "I = min(felszíni víz, beszivárgási ráta × idő, telítési készlet - gyökérzónavíz)");
            Add("water.evaporation", "Lomb-, felszín- és talajpárolgás", "Vízháztartás", "Térképcella", "Környezeti lépés", water, "Evaporated",
                "water.potential|water.canopy|water.surface|water.root|soil.profile|forest.canopy", "water.canopy|water.surface|water.root|water.evaporated",
                "A párolgási keret sorrendben a lombvízből, felszíni vízből és a hervadáspont feletti talajvízből fogy. A lombborítás mérsékli a talajpárolgást.");
            Add("water.uptake", "Gyökérvíz felvétele és igényelosztás", "Vízháztartás", "Cellán belüli élő fák", "Környezeti lépés", water, "uptake",
                "water.potential|water.root|soil.profile|forest.canopy|road.network|buildings.footprint", "water.root|water.transpired|water.monthBudget",
                "A levélfelület arányában kér vizet; minden fa ugyanazt a teljesített igényhányadot kapja. Burkolt felszínen nincs növényzeti igény.", "igény = E × LAI × 0,8; felvétel = min(elérhető víz, igény × talajvíz-ellátottság)");
            Add("water.drainage", "Mélyvíz és alaplefolyás", "Vízháztartás", "Térképcella", "Környezeti lépés", water, "drainage",
                "water.root|water.deep|soil.profile", "water.root|water.deep|water.outflow",
                "Szabadföldi vízkapacitás felett a drénezési ráta szerint mélyvízbe jut a víz; a mélyvízből 0,15 mm/környezeti óra távozik.");
            Add("water.runoff", "Térbeli felszíni lefolyás", "Vízháztartás", "Szomszédos cellák és kifolyók", "Környezeti lépés",
                "ForesTycoon.Ecology/Water/SurfaceWaterFlux.cs", "Schedule", "water.surface|water.routing|terrain.height|terrain.outlets", "water.surface|water.outflow",
                "Cellánként egy lejtőirányú fluxus. Bejövő víz együtt kerül publikálásra, egy lépésben nem folyhat át több cellán. Külső veszteség csak kifolyón.");
            Add("water.stress", "Aszály, víztöbblet és havi vízválasz", "Vízháztartás", "Cellánként / fafajonként", "Környezeti lépés + erdőhónap", water, "WaterResponse",
                "water.root|water.monthBudget|soil.profile|forest.species", "water.growthFactor",
                "Simított szárazság és víztöbblet; a havi igény/felvétel integrálja adja a növekedési vízellátást. Fafajfüggő szárazságérzékenység.",
                "vízválasz = clamp(ellátottság ^ szárazságérzékenység × (1 - víztöbblet × 0,65), 0, 1)");
            Add("water.balance", "Vízmérleg és havi integrálzárás", "Vízháztartás", "Világ / cella", "Összegzés; hónapzárás", water, "BalanceError",
                "water.canopy|water.surface|water.root|water.deep|weather.rain|water.evaporated|water.transpired|water.outflow|forest.month", "water.balance|water.monthBudget",
                "A havi növényzetfrissítés után törlődnek a felvételi, igény- és sugárzási integrálok; a vízkészletek megmaradnak.", "mérleghiba = tárolt víz - (induló víz + eső - párolgás - transzspiráció - kifolyás)");

            var species = Add("forest.species", "Fafajkatalógus", "Erdő", "Fafaj", "Új világ / fajlekérdezés",
                "ForesTycoon.Ecology/Species/ForestSpeciesCatalog.cs", "Playable", "", "forest.species",
                "Fajonként méretek, koronaarány, növekedési ráta, kapacitás, szárazságérzékenység, árnyéktűrés, életkor és modellpreset. Fák és cserjék is szerepelnek.");
            foreach (var s in ForestSpeciesTraits.Playable)
            {
                var t = ForestSpeciesTraits.For(s); var p = ForestSpeciesProfile.For(s);
                Param(species, t.Name + ": magassági plafon", t.MaxHeight, "m"); Param(species, t.Name + ": radiális ráta", t.RadialRate, "m/növekedési egység");
                Param(species, t.Name + ": érett kor", p.MatureAgeYears, "erdőév"); Param(species, t.Name + ": maximális kor", p.MaximumAgeYears, "erdőév");
                Param(species, t.Name + ": szárazságérzékenység", t.DrySensitivity, "1"); Param(species, t.Name + ": faanyagár", ForestryLogistics.TimberPrice(s), "eFt/m³");
            }
            Add("forest.initial", "Induló erdő eloszlása", "Erdő", "Térképcella / egyed", "Új világ", forest, "GenerateInitialForest",
                "world.seed|terrain.height|terrain.moisture|soil.profile|forest.species", "forest.trees", "Termőhelyből és seedből induló természetes vagy választott erdőminta; egyedi életkor, méret, hely és változatosság.");
            Add("forest.site", "Termőhelyi alkalmasság", "Erdő", "Cella / fafaj", "Telepítés és rátafrissítés", forest, "Suitability",
                "terrain.height|soil.fertility|forest.species", "forest.fitness", "Bekötött környezet mellett a talajtermékenység és fajfüggő magassági alkalmasság adja a termőhelyet. A nedvesség külön vízválaszon keresztül hat; környezet nélküli régi tesztmódban a Fitness nedvességet is olvas.", "alkalmasság = termékenység × (0,45 + magassági alkalmasság × 0,55)");
            Add("forest.competition", "Fény- és koronatér-versengés", "Erdő", "Faegyed és két szomszédsági gyűrű",
                "Havi előkészítés / helyi beavatkozás", "ForesTycoon.Ecology/Forest/ForestCompetition.cs", "Evaluate",
                "forest.trees|weather.forcing|water.growthFactor|forest.species", "forest.resources",
                "Közös geometriapillanatkép, koronafedés és magassági dominancia alapján fény és tér. Környezeti vízelosztás mellett nincs második gyökérverseny-büntetés.");
            Add("forest.growth", "Egyedi fanövekedés", "Erdő", "Faegyed", "Havi ráták; folytonos méretinterpoláció",
                "ForesTycoon.Ecology/Species/ForestTree.cs", "Factor", "forest.fitness|forest.resources|water.growthFactor|forest.health|calendar.time|forest.species", "forest.trees|forest.volume",
                "Átmérő, magasság és korona nő. Magasság fajfüggő plafonhoz közelít, az érett fa tovább vastagodik.",
                "növekedési tényező = alkalmasság × fényválasz × vízválasz × sqrt(tér) × egészség × évszak");
            Add("forest.health", "Egészség és tartós stressz", "Erdő", "Faegyed", "Erdőhónap", individuals, "stressThreshold",
                "forest.resources|forest.fitness|forest.species|forest.health", "forest.health|forest.stress",
                "Egészség a termőhely és a legszűkösebb erőforrás célértéke felé lép; tartós elnyomásnál nő a stressz, javulásnál csökken.",
                "cél = clamp(alkalmasság × (0,15 + 0,85 × min(tér, víz, fényválasz)), 0, 1); havi egészséglépés: 0,035");
            Add("forest.mortality", "Önritkulás, elhalás és holtfa", "Erdő", "Faegyed", "Erdőhónap", individuals, "DeadTrees",
                "forest.stress|forest.health|forest.species|calendar.time|forest.trees", "forest.trees|forest.deadwood",
                "Legalább 4 stresszév és alacsony egészség, vagy a faj maximális kora okoz elhalást. Holtfa 8 év után eltűnik; álló/fekvő megjelenítés külön szabály.");
            Add("forest.regeneration", "Természetes újulat", "Erdő", "Üres cella és magszóró szomszédok", "Erdőhónap", forest, "TryRegenerate",
                "forest.trees|forest.species|forest.fitness|world.seed|calendar.time|road.network|buildings.footprint", "forest.trees",
                "Alkalmas üres cellákon a szomszédos magadó állományok és determinisztikus sorsolás alapján jelenik meg újulat.");
            Add("forest.canopy", "Lombborítás és vízigény visszacsatolása", "Erdő", "Térképcella", "Erdőrevízió-változás",
                "ForesTycoon.Ecology/Forest/ForestHydrology.cs", "HydrologyInputs", "forest.trees|forest.health|forest.species", "forest.canopy",
                "Koronafelület és egészség összegéből lombborítás, interceptiós kapacitás és LAI. A kivágás megváltoztatja a következő vízlépés igényét és esőfelfogását.", "borítás = 1 - exp(-koronafelület / cellaterület); LAI legfeljebb 6");
            Add("forest.plant", "Telepítés és ültetvény-nyilvántartás", "Erdészeti beavatkozás", "Kijelölt cellák", "Játékosparancs", forest, "PlantInArea",
                "commands|terrain.height|terrain.water|road.network|buildings.footprint|forest.species|forest.fitness", "forest.trees|forest.plantations",
                "Érvényes termőhelyen választott fajból csemeték; területazonosító, telepítési év és kezdő egyedszám. A jelenlegi telepítés nem számol pénzügyi költséget.");
            Add("forest.designate", "Kitermelés kijelölése", "Erdészeti beavatkozás", "Kijelölt cellák", "Játékosparancs", logistics, "Designate",
                "commands|forest.volume", "harvest.sites", "A kijelölés megjegyzi a kitermelhető térfogatot; önmagában nem vágja ki a fákat.");
            Add("forest.fell", "Egész fák kivágása és helyi depó", "Erdészeti beavatkozás", "Faegyed / cella", "Processzor-munka vagy kitermelési művelet", individuals, "FellIndividual",
                "harvest.sites|forest.trees|machine.work", "forest.trees|forest.stumps|timber.localDepot",
                "Egész fák kerülnek a helyi rönkdepóba. Részrakodás nem zsugorítja a megmaradt fákat; a fák térfogata és a tönk mérete megmarad.", "V = π × átmérő² / 4 × magasság × 0,45");
            Add("forest.stumps", "Tönkök lebomlása", "Erdészeti beavatkozás", "Kivágott fa helye", "Erdőhónap / megjelenítés",
                "ForesTycoon.Ecology/Species/ForestTree.cs", "LifetimeYears", "forest.stumps|calendar.time", "forest.stumps",
                "A tönk hat erdőév alatt lebomlik; a telepítést nem akadályozza.", "bomlás = clamp((év - kivágás éve) / 6, 0, 1)");

            var build = Add("road.build", "Útépítés és hálózat", "Utak és nyomok", "Útvonal cellái", "Játékosparancs", game, "ExecuteRoadPath",
                "commands|terrain.height|terrain.water|buildings.footprint", "road.network|economy.expenses|forest.trees|water.routing",
                "Aszfalt/makadám út, rögzített úttestmagasság és kölcsönös csatlakozások. Csak ténylegesen létrehozott vagy módosított útcsempék növelik az építési költséget.");
            Add("road.repair", "Útjavítás", "Utak és nyomok", "Útvonal cellái", "Játékosparancs", game, "ExecuteRoadRepair",
                "commands|road.condition|road.network", "road.condition|economy.expenses",
                "A kopott útcsempék állapota 1-re áll; a költség a burkolat építési ára és a sérülés szerint számolódik.", "javítás = építési ár × 0,8 × sérülés");
            Add("road.weather", "Időjárási útromlás", "Utak és nyomok", "Közúti csempe", "Legalább 0,5 játék-s összegzése",
                "ForesTycoon.Map/Terrain/TerrainMap.Roads.cs", "WeatherRoads", "terrain.moisture|weather.rain|calendar.time|road.condition|road.network", "road.condition",
                "A térképi hidrológiai nedvességet olvassa, nem a dinamikus gyökérzónavizet. Makadám erősebben romlik esőben; időt erdőévre váltja.",
                "makadám: 0,10 × (1 + 2,5 × nedvesség) + 1,6 × eső × (0,4 + nedvesség); aszfalt: 0,015 × (1 + nedvesség) + 0,08 × eső; állapot -= ráta × évek");
            var traffic = Add("road.trafficWear", "Forgalmi útkopás – gráf", "Utak és nyomok", "Közúti csempe", "Teherautó csempeváltás",
                "ForesTycoon/World/GameWorld.Rules.cs", "ApplyTrafficWear", "vehicle.mass|road.network|road.condition|vehicle.position", "road.condition",
                "A jelenleg szerkeszthető és a játékba bekötött gráf. Áthaladási terhelés, burkolati szorzó, állapot és szerkesztett egyenletek.",
                "alapmodell: 0,0015 × össztömeg / 36000 × burkolati szorzó × kopási szorzó", GameRuleExecution.EditableGraph);
            traffic.Sources.Add(new("ForesTycoon/World/Vehicles/RoadTrafficParameters.cs", "RoadTrafficParameters"));
            var trail = Add("trail.build", "Közelítőnyom kijelölése", "Utak és nyomok", "Útvonal cellái", "Játékosparancs", game, "ExecuteSkidTrailPath",
                "commands|terrain.water|buildings.footprint|road.network", "trail.network|economy.expenses", "Ideiglenes nyom száraz, épület nélküli talajon, az úthálózathoz csatlakozva; a növényzet nem teljes útépítésként kezelődik.");
            Add("trail.traffic", "Nyomvályú és használat", "Utak és nyomok", "Nyomcsempe", "Gép vagy teherautó áthaladása",
                "ForesTycoon.Map/Terrain/TerrainMap.SkidTrails.cs", "DriveSkidTrail", "vehicle.mass|vehicle.position|machine.position|trail.wear", "trail.wear|trail.idle",
                "A vályú fokozatosan mélyül, az üresjárati idő nullázódik. A teherautó nyomterhelése a közúti alapkopás húszszorosa.", "vályú = clamp(vályú + terhelés × (1 - vályú), 0, 1)");
            var age = Add("trail.age", "Nyom regenerációja és benövése", "Utak és nyomok", "Nyomcsempe", "Út-időjárási frissítés",
                "ForesTycoon.Map/Terrain/TerrainMap.SkidTrails.cs", "AgeSkidTrails", "trail.wear|trail.idle|calendar.time", "trail.wear|trail.idle|trail.network",
                "A vályú évente 0,4-et enyhül; 3 év használatlanság és szinte eltűnt vályú után a nyom megszűnik. Az útvonalak újraellenőrződnek.");
            Add("route.find", "Hálózati útvonalkeresés", "Járművek", "Hálózat / forrás és cél", "Utasítás / hálózatváltozás",
                "ForesTycoon.Map/Terrain/TerrainMap.Network.cs", "FindNetworkPath", "road.network|trail.network|trail.wear|buildings.footprint|timber.stacks", "vehicle.route",
                "Közös út- és nyomhálózat, kölcsönös csatlakozások, burkolat és nyomvályú szerinti költség. A flottában több dokkpár útvonalai közül a legrövidebb elemszámú nyertes út választódik.");
            var mass = Add("vehicle.mass", "Rakomány és össztömeg", "Járművek", "Teherautó", "Mozgási lépés", dynamics, "Mass",
                "vehicle.cargo", "vehicle.mass", "A rakomány térfogatból a rögzített frissfa-sűrűség alapján tömeg lesz. Ez a sűrűség jelenleg nem fajfüggő.", "tömeg = üres tömeg + m³ × fasűrűség");
            var resistance = Add("vehicle.resistance", "Lejtés és menetellenállás", "Járművek", "Teherautó / aktuális útszakasz", "Legfeljebb 1/30 jármű-s", dynamics, "Resistance",
                "vehicle.mass|vehicle.speed|terrain.height|road.condition|road.network|trail.wear", "vehicle.resistance",
                "Gördülés, előjeles lejtőellenállás és légellenállás; durva, kopott felület növeli a gördülést.", "F = Crr × m × g × cos(atan(lejtés)) + m × g × sin(atan(lejtés)) + 0,5 × levegősűrűség × légellenállási felület × v²");
            Add("vehicle.motion", "Gyorsulás, kanyar és fékezés", "Járművek", "Teherautó", "Legfeljebb 1/30 jármű-s",
                "ForesTycoon/World/Vehicles/Vehicle.cs", "VehicleDynamics.Step", "vehicle.time|vehicle.route|vehicle.resistance|vehicle.mass|road.condition|vehicle.broken", "vehicle.position|vehicle.speed|vehicle.fuel",
                "Teljesítmény- és tapadáskorlátos vonóerő, terhelés és kanyar szerinti célsebesség, végponti fékezés. Kopás lassít, meghibásodás megállít.", "célsebesség szorzója: 1 - 0,55 × útsérülés; üzemanyag a tényleges motor-munkából és alapjáratból");
            var fuel = Add("vehicle.fuel", "Motorfogyasztás", "Járművek", "Teherautó", "Mozgási lépés", dynamics, "FuelPerKilowattHour",
                "vehicle.speed|vehicle.resistance|vehicle.mass", "vehicle.fuel", "A dinamika által ténylegesen kifejtett teljesítmény és alapjárat alapján számolt liter. Lejtőn fékezés nem termel negatív fogyasztást.", "liter = motorteljesítmény_kW × órák × fajlagos fogyasztás + alapjárati l/h × órák");
            Add("vehicle.transfer", "Rakodási és szállítási állapotgép", "Járművek", "Teherautó / forrás / cél", "Járműidő",
                "ForesTycoon/World/Vehicles/VehicleSystem.cs", "Update", "vehicle.route|timber.stacks|vehicle.cargo|vehicle.broken", "vehicle.cargo|vehicle.transportState|timber.stacks|mill.stock",
                "Rakodás, fuvar, lerakodás, visszaút, várakozás. Hiányzó készletnél vár; megszakadt útvonalnál megőrzi a rakományt és megáll.");

            var wear = Add("upkeep.wear", "Gép- és járműkopás, meghibásodás", "Fenntartás", "Flottajármű", "Munka / mozgás", upkeep, "Operate",
                "machine.work|vehicle.cargo|trail.network|vehicle.speed|vehicle.wear|vehicle.time", "vehicle.wear|vehicle.broken",
                "Terhelési szorzóval kopik; saját seedelt xorshift-generátorból sorsolt meghibásodás. Teherautó-terhelés: nyomon 2, úton 1, rakománytényező 1 + 0,5 × telítettség.",
                "kopás += 0,00025 × igénybevétel × s; meghibásodás-esély = 0,004 × kopás² × igénybevétel × s");
            Add("upkeep.effects", "Kopás hatása a munkára és költségre", "Fenntartás", "Flottajármű", "Munka / költségelszámolás", upkeep, "FuelFactor",
                "vehicle.wear", "machine.pace|economy.fuelFactor", "Gépek munkaidejét és mozgását a PaceFactor lassítja. A teherautó PaceFactor jelenleg nincs bekötve a mozgásba; a fogyasztási szorzó a költségelszámolásban hat.",
                "üzemanyag-szorzó = 1 + 0,5 × kopás; géptempó = 1 - 0,3 × kopás");
            var repair = Add("upkeep.repair", "Helyszíni szerelés", "Fenntartás", "Meghibásodott flottajármű", "Járműidő", upkeep, "RepairLeft",
                "vehicle.broken|vehicle.wear|vehicle.time", "vehicle.broken|vehicle.wear|economy.runningCosts", "Szerelési várakozás után részben enyhül a kopás; alap- és kopásarányos költség keletkezik.");
            var service = Add("upkeep.service", "Telephelyi karbantartás", "Fenntartás", "Parkoló flottajármű", "Járműidő", upkeep, "Service",
                "vehicle.wear|fleet.state|vehicle.time", "vehicle.wear|economy.runningCosts", "A telephelyen álló, nem meghibásodott gép és teherautó kopása idővel csökken, a javított kopásmennyiségért költséget számol.");

            Add("fleet.depot", "Telephely és induló flotta", "Faanyag és logisztika", "2×2 cella", "Játékosparancs", fleet, "PlaceDepot",
                "commands|terrain.height|terrain.water|road.network|forest.trees|harvest.sites|buildings.footprint", "buildings.footprint|fleet.state",
                "Sík, száraz, üres, út melletti telephely. Az első egy processzort, forwardert és rönkszállítót ad. Jelenleg nincs telephelyépítési vagy járművásárlási költség.");
            Add("fleet.orders", "Munkára küldés és hazahívás", "Faanyag és logisztika", "Flottajármű / cél", "Játékosparancs és állapotváltás", fleet, "AssignTruck",
                "commands|fleet.state|vehicle.route|timber.stacks|harvest.sites|mill.stock", "fleet.state|vehicle.route|machine.work", "Forrás és cél kijelölése, megközelítés, munka és hazatérés. A teherautó üresen távozik a forrástól, a gép előbb leadja a rakományt.");
            var processor = Add("machine.processor", "Processzor: vágás és kihordás", "Faanyag és logisztika", "Kitermelési cella / sarang", "1/30 jármű-s al-lépés", machine, "UpdateProcessor",
                "harvest.sites|forest.volume|timber.localDepot|timber.stacks|machine.pace|machine.work|vehicle.broken|vehicle.time", "machine.work|machine.cargo|timber.localDepot|timber.stacks|machine.fuel",
                "Kivágás, rönkfelvétel és sarangra hordás állapotgépe; kopás szerinti tempóval, rakománykapacitással és műveleti üzemanyag-rátákkal.");
            var forwarder = Add("machine.forwarder", "Forwarder: sarangok közötti közelítés", "Faanyag és logisztika", "Gép / sarangpár", "1/30 jármű-s al-lépés", machine, "UpdateForwarder",
                "timber.stacks|machine.pace|vehicle.route|vehicle.broken|vehicle.time", "machine.cargo|timber.stacks|machine.position|machine.fuel", "Úton vagy nyomon állva, rönkönként rakodik. A markoló megfogáskor kiveszi, elengedéskor átadja a térfogatot és értéket; az alap ciklus 8 másodperc. A rakomány alulról épül és felülről fogy.");
            Add("machine.movement", "Erdészeti gépek mozgása és nyomterhelése", "Faanyag és logisztika", "Gép / útvonal", "1/30 jármű-s al-lépés", machine, "Advance",
                "vehicle.route|road.network|trail.network|trail.wear|machine.pace|machine.cargo|machine.work", "machine.position|trail.wear|machine.fuel",
                "Eltérő üres/rakott sebesség, útvonaljárhatóság ellenőrzése, nyomvályú és művelethez tartozó fogyasztás. A gépek külön kinematikával futnak, a teherautó fizikai modelljét nem használják.");
            Add("timber.stack", "Sarang térfogat- és értékmérlege", "Faanyag és logisztika", "Sarang", "Rakodás / lerakodás", stacks, "Take",
                "machine.cargo|vehicle.cargo|timber.stacks", "timber.stacks|timber.cargoValue", "A kivett mennyiség arányában együtt mozog az érték. Üres sarang nullázódik; foglalt vagy nem üres sarang nem törölhető.", "átadott érték = készletérték × kivett térfogat / készlettérfogat");
            Add("timber.deliver", "Átadás és faanyag eladása", "Faanyag és logisztika", "Teherautó / sarang vagy malom", "Lerakodás", stacks, "Receive",
                "vehicle.cargo|timber.cargoValue|buildings.footprint", "timber.stacks|mill.stock|economy.income", "Sarangra értékkel együtt kerül a fa; malomba érkezéskor azonnal hozzáadódik a bevétel. Nem a feldolgozás pillanatában történik az eladás.");
            Add("mill.place", "Fűrészmalom elhelyezése", "Faanyag és logisztika", "2×2 cella", "Játékosparancs", logistics, "PlaceMill",
                "commands|terrain.height|terrain.water|forest.trees|harvest.sites|buildings.footprint", "buildings.footprint|mill.stock", "Sík, száraz, üres, erdő és kitermelés nélküli telek; út és erdő később nem építhető a lábnyomára. Jelenleg nincs malomépítési költség.");
            Add("mill.process", "Fűrészmalom feldolgozása", "Faanyag és logisztika", "Malom", "Járműidő", logistics, "Processed",
                "mill.stock|vehicle.time", "mill.stock|mill.processed", "A készletből korlátozott ütemben feldolgozott mennyiség lesz; jelenleg nincs új terméklánc vagy feldolgozási bevétel.", "feldolgozás = min(készlet, 0,25 × jármű-s) m³");
            var diesel = Add("economy.fuel", "Üzemanyag-költség", "Gazdaság", "Flottajármű / vállalat", "Gépmunka vagy teherautó elszámolás", stacks, "Burn",
                "vehicle.fuel|machine.fuel|economy.fuelFactor", "economy.runningCosts", "A tényleges fogyasztás és a kopási szorzó alapján költséget számol; a modellnek jelenleg nincs külön üzemanyagkészlete.", "költség += liter × dízelár");
            Add("economy.balance", "Bevétel, kiadás és eredmény", "Gazdaság", "Vállalat", "Műveleti elszámolás",
                game, "Balance", "economy.income|economy.expenses|economy.runningCosts", "economy.result",
                "Bevétel mínusz építési/javítási és üzemeltetési költségek. Nincs szigorú fizetőképességi korlát, hitel, dinamikus piac vagy erdőápolási kiadás.", "eredmény = bevétel - építési kiadások - működési költségek");

            Add("wildlife.habitat", "Vadállomány élőhelye", "Vadak", "Térképcella / állat", "Erdő- vagy tereprevízió változás",
                "ForesTycoon/World/WildlifeSystem.cs", "CollectWildlifeSpots", "forest.trees|terrain.height|terrain.water|road.network", "wildlife.state",
                "Járható élőhelyből induló állatok; élőhely nélkül eltűnnek. Nincs demográfiai vagy erdei vadkármodell.");
            Add("wildlife.forage", "Éhség és helyi táplálékfogyás", "Vadak", "Állat / cella", "Világlépés",
                "ForesTycoon/World/WildlifeSystem.cs", "Hunger", "wildlife.state|terrain.moisture|calendar.time", "wildlife.state|wildlife.forage",
                "Éhség nő, állva táplálkozva csökken; helyi táplálék fogy és idővel regenerálódik. Ez nem írja a faegyedek egészségét.");
            Add("wildlife.movement", "Búvóhely és mozgásválasztás", "Vadak", "Állat / szomszédos cellák", "Világlépés",
                "ForesTycoon/World/WildlifeSystem.cs", "TargetTile", "wildlife.state|wildlife.forage|forest.trees|weather.rain|terrain.height|terrain.water|road.network", "wildlife.state",
                "Éhség, nedvesség, eső alatti érett erdő, vándorlási igény és fordulási korlátok alapján választ célpontot; a járást a talajhoz illeszti.");
            Add("visual.tree", "Faállapotból procedurális modell", "Megjelenítés", "Faegyed", "Megjelenítési frissítés",
                "ForesTycoon.Ecology/Shape/TreeShapeSpec.cs", "TreeShapeSpec", "forest.trees|forest.species|forest.health|forest.resources|visual.foliage|calendar.time", "visual.tree",
                "Faj, életfázis, méret, fény, életerő és fenológia választják a koronát és ágszerkezetet. A modellméret a szimulációt követi; a renderer nem növeszti a fát.", "", GameRuleExecution.Presentation);
            Add("visual.truck", "Teherautómodell és látható rakomány", "Megjelenítés", "Teherautó", "Renderképkocka",
                "ForesTycoon/Rendering/Models/GlbTruckModel.cs", "GlbTruckModel", "vehicle.position|vehicle.speed|vehicle.cargo|terrain.height", "visual.truck",
                "Importált modell, kerékmozgás, rakomány és útfelületet követő dőlés. A megjelenítés olvassa a szimulációt.", "", GameRuleExecution.Presentation);
            Add("visual.weather", "Időjárási látvány", "Megjelenítés", "Világ / felület", "Renderképkocka",
                "ForesTycoon/Rendering/Scene/TerrainRenderer.cs", "TerrainRenderer", "weather.rain|weather.forcing|water.surface|forest.trees|road.condition", "visual.weather",
                "Eső, felhő, villámlás, nedves felületek, utak és erdő. Kézi hó/időjárási látványteszt külön beállítás; nem váltja át a klíma vízmérlegét.", "", GameRuleExecution.Presentation);
            Add("visual.phenology", "Lombfázis és fajfüggő életfázis", "Megjelenítés", "Faegyed", "Erdőév tört része",
                "ForesTycoon.Ecology/Species/TreePhenology.cs", "At", "forest.trees|forest.species|calendar.time|world.seed", "visual.foliage",
                "Rügyfakadás, teljes lomb, őszi szín és lombhullás fajonkénti naptárból, egyedi seedelt eltolással. A jelenlegi HydrologyInputs levélfelületében ez a lombfázis még nem szerepel.", "", GameRuleExecution.Presentation);
            var deadwoodVisual = Add("visual.deadwood", "Holtfa és tönk megjelenítése", "Megjelenítés", "Elhalt vagy kivágott egyed", "Renderfrissítés",
                "ForesTycoon/Terrain/Forest/Terrain.ForestStumps.cs", "Stump", "forest.stumps|forest.deadwood|calendar.time", "visual.deadwood",
                "Kivágott fa méretű, öregedő tönkök és elhalás után idővel fekvő holtfa. Állapotból és korból következik a látvány, LOD-váltáskor is.", "", GameRuleExecution.Presentation);
            deadwoodVisual.Sources.Add(new("ForesTycoon/Terrain/Forest/Terrain.ForestIndividuals.cs", "DeathYear"));
            Add("visual.machines", "Erdészeti gépmodellek", "Megjelenítés", "Processzor / forwarder", "Renderképkocka",
                "ForesTycoon/Rendering/Scene/ForestMachineRenderer.cs", "ForestMachineRenderer", "machine.position|machine.work|machine.cargo|terrain.height|trail.wear", "visual.machines",
                "Útvonalhoz illesztett gépmodellek, rakomány és darumozgás; licencelt GLB-k esetén animáció, hiányukban egyszerű helyettesítő modell.", "", GameRuleExecution.Presentation);
            var buildingVisual = Add("visual.buildings", "Épületek és sarangok modelljei", "Megjelenítés", "Malom / telephely / sarang", "Renderképkocka",
                "ForesTycoon/Rendering/Scene/WorldContentRenderer.cs", "WorldContentRenderer", "buildings.footprint|terrain.height|timber.stacks", "visual.buildings",
                "A lábnyomhoz és terepszinthez illesztett malommodell, telephely és sarang-látvány; a készlet a rönkhalom megjelenését vezérli.", "", GameRuleExecution.Presentation);
            buildingVisual.Sources.Add(new("ForesTycoon/Rendering/Scene/ForestMachineRenderer.cs", "DrawDepots"));
            Add("visual.animals", "Vadmodellek és járásanimáció", "Megjelenítés", "Állat", "Renderképkocka",
                "ForesTycoon/Rendering/Scene/WildlifeRenderer.cs", "WildlifeRenderer", "wildlife.state|terrain.height", "visual.animals",
                "A szimulált hely, fordulás, járáskeverés és lépéshossz hajtja az importált szarvasmodell animációját.", "", GameRuleExecution.Presentation);
            Add("visual.fish", "Halélőhely és úszó modellek", "Megjelenítés", "Állóvízmedence", "Víztérkép változása / renderképkocka",
                "ForesTycoon.Map/Terrain/TerrainMap.Fish.cs", "CollectFishHabitats", "terrain.water|terrain.height|calendar.time|world.seed", "visual.fish",
                "Elég mély medencékben dekoratív, seedelt halhelyek. Nagy, térképszéli tenger 8–120 hal; kis belső víz legfeljebb 2. Nincs halpopulációs anyagmérleg.", "", GameRuleExecution.Presentation);
            Add("visual.terrain", "Terep- és vízgeometria", "Megjelenítés", "Térképchunk", "Tereprevízió / láthatóság",
                "ForesTycoon/Terrain/Render/Terrain.Mesh.cs", "Terrain", "terrain.height|terrain.water|terrain.moisture", "visual.terrain",
                "A node- és csempeadatokból terep- és vízfelület épül; helyi változások chunkonként frissítik a geometriát.", "", GameRuleExecution.Presentation);
            Add("visual.roads", "Útburkolat, kátyú és nyomvályú", "Megjelenítés", "Út- vagy nyomcsempe", "Renderképkocka",
                "ForesTycoon/Terrain/Render/Terrain.RoadRender.cs", "DrawRoadPotholes", "road.network|road.condition|trail.network|trail.wear|terrain.height|weather.forcing", "visual.roads",
                "Burkolatfüggő felület, sérültség szerinti kátyúk, terephez illesztett nyomvályúk és csatlakozó lejárók. A látvány az út és nyom állapotát követi.", "", GameRuleExecution.Presentation);
            // Every tunable number appears at its rule, editable in the editor and applied to the world as one journaled change.
            foreach (var spec in GameTuning.Specs)
            {
                var owner = catalog.Rules.Find(r => r.Id == spec.Rule) ?? throw new InvalidOperationException("Tuning rule missing: " + spec.Rule);
                owner.Parameters.Add(new(spec.Name, tuning[spec.Key], spec.Unit, spec.Id, spec.Min, spec.Max, spec.Default));
            }
            catalog.Gaps.AddRange(new[] {
                "A natív folyamatok képlete a jelenlegi C# implementáció leírása; a számaik (kulccsal jelölt paraméterek) hangolhatók, a képlet szerkezete csak road.trafficWear esetén szerkeszthető gráfként.",
                "A talaj-, klíma- és fafajparaméterek egyelőre csak tájékoztató adatok: a világ létrehozásakor rögzülnek, a szerkesztőből nem hangolhatók.",
                "Talajtömörödés, erózió és útkialakítás miatti részletes vízelvezető műtárgyak nincsenek külön folyamatként megvalósítva.",
                "A vadak nem rágják a csemetéket; kártevő-, fertőzés- és tűzterjedési rendszer még nincs.",
                "Gyérítés, ápolás és ezek pénzügyi költsége még nem külön játékosparancs; telepítés és kitermelés van.",
                "Nincs pénzkészlet alapján tiltott építés, járművásárlás, hitel vagy dinamikus faanyagpiac.",
                "A fa térfogatának pénzbeli értékét a logisztikai TimberPrice adja; a teherautó fasűrűsége minden fajra azonos.",
                "A natív folyamatok több írója és visszacsatolása a meglévő futtatási sorrend szerint működik; a katalógus élei nem új végrehajtási sorrendet jelentenek." });
            catalog.Validate(); return catalog;
        }
    }
}
