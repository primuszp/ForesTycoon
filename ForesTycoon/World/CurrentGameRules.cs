using System;
using System.Linq;

namespace ForesTycoon
{
    /// <summary>Inventory of implemented behavior. Values with public constants are read from the runtime itself.</summary>
    internal static class CurrentGameRules
    {
        // File descriptions are historical; only authoring choices survive a refresh from runtime metadata.
        internal static GameRuleCatalog Refresh(GameRuleCatalog saved, GameRuleCatalog current)
        {
            saved.Validate();
            var tuning = GameTuning.FromOverrides(saved.TuningOverrides());
            current.Name = saved.Name;
            foreach (var rule in current.Rules)
            {
                var previous = saved.Rules.Find(r => r.Id == rule.Id);
                if (previous != null) { rule.Notes = previous.Notes; rule.X = previous.X; rule.Y = previous.Y; }
                for (int i = 0; i < rule.Parameters.Count; i++)
                {
                    var parameter = rule.Parameters[i];
                    if (parameter.Tunable) rule.Parameters[i] = parameter with { Value = tuning[GameTuning.Spec(parameter.Key).Key] };
                }
            }
            current.Validate();
            return current;
        }

        internal static GameRuleCatalog Build(double forestYearSeconds = EcologyTime.DefaultGameSecondsPerYear,
            ClimateDefinition climate = null, SoilLandscapeDefinition soils = null, GameTuning tuning = null)
        {
            tuning ??= GameTuning.Default;
            var catalog = new GameRuleCatalog();
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
                "commands", "calendar.time|vehicle.time|wildlife.time", "Ökoszisztéma, logisztika, útöregedés, vadak, majd a regisztrált rendszerek (rakomány, járművek, effektek). A járművek hangolható órát, a vadak külön rögzített időszorzót kapnak.");
            Param(clock, "Erdőév", forestYearSeconds, "játék-s");
            Param(clock, "Vadak időszorzója", GameWorld.WildlifeTimeScale, "×");
            var phases = Add("time.ecology", "Ökológiai többütemű futtatás", "Idő és vezérlés", "Világ / hónaphatár", "0,5 s; havi határra bontva",
                "ForesTycoon.Ecology/Water/ForestEnvironmentCoordinator.cs", "ForestEnvironmentCoordinator",
                "calendar.time|forest.trees|water.root", "ecology.steps|forest.month", "Előkészítés → víz és időjárás → havi növényzet → havi integrálok törlése. A sorrend befolyásolja az eredményt.");
            Param(phases, "Alaplépés", EnvironmentSystem.StepSeconds, "játék-s");
            Add("commands.apply", "Játékosparancsok", "Idő és vezérlés", "Világ és kijelölt objektum", "Parancssor végrehajtásakor",
                "ForesTycoon/World/Commands/WorldCommandQueue.cs", "ExecutePending", "player.input", "commands",
                "Építés, bontás, javítás, ültetés, kitermelés kijelölése, járműutasítás és időjárási beavatkozás naplózható parancs.");
            Add("save.restore", "Checkpoint és visszajátszás", "Idő és vezérlés", "Világ", "Mentés / betöltés", game, "Load",
                "commands|rules.behaviors|machine.controllerState|calendar.time|forest.trees|water.root|water.snow|road.condition|vehicle.cargo|machine.grapple|economy.income", "save.snapshot",
                "Aktív szabálymodell, talaj és klíma, készletek, egyedek, véletlengenerátorok és függő parancsok megőrzése; hibánál izolált betöltés.");
            Add("rules.behaviors", "Viselkedésgráfok és objektumkötések", "Idő és vezérlés", "Típus / stabil objektumazonosító", "Naplózott parancs; a kötött folyamat meghívásakor",
                "ForesTycoon/World/GameWorld.Behaviors.cs", "ExecuteBehaviors", "commands", "rules.behaviors",
                "Ellenőrzött számítási gráfok a támogatott folyamatkimenetekhez és hangolási paraméterekhez. Az egyedi kötés a típuskötés helyére lép; kötés nélkül az eredeti C# eredmény érvényes. Hibás számtan esetén a beépített érték marad, látható hibajelzéssel.");
            Add("machine.controller", "Gép- és teherautó-vezérlési állapotgráf", "Faanyag és logisztika", "Forwarder / processzor / teherautó típusa vagy egyedi flotta-ID", "Gép al-lépés; teherautó: logisztikai lépés",
                "ForesTycoon.Rules/BehaviorController.cs", "CompiledBehaviorControllers", "rules.behaviors|machine.work|machine.cargo|vehicle.broken|vehicle.transportState|fleet.state",
                "machine.controllerState|machine.state", "Szerkeszthető állapotok, sorrendezett ÉS-feltételek, időzítő és egyszeri események. Műveletek: automatikus munka, várakozás, hazatérés; forwardernél és teherautónál külön felrakodás, lerakodás, haladás és részrakománnyal indulás. A szállítási ciklussablon ezeket szerkeszthető állapotokra bontja. Mentett időzítő és eseményállapot; fizikai átadás csak a megfelelő helyen.", execution: GameRuleExecution.EditableController);
            Add("route.policy", "Útvonalköltség és szakaszengedély gráfjai", "Járművek", "Flottatípus / egyedi flotta-ID", "Minden új útkeresés",
                "ForesTycoon/World/Cargo/ForestryLogistics.Behaviors.cs", "RouteCost", "rules.behaviors|road.condition|road.network|trail.network|trail.wear|machine.cargo|vehicle.cargo", "vehicle.route",
                "A route.allow kizárhat egy járható szakaszt; a route.cost pozitív súlyt ad. Bemenet a kiinduló és célcsempe, burkolat, sérültség, rakomány és kapacitás. A gráf szerinti Dijkstra-költség a dokkpár kiválasztását is befolyásolja. A már folyamatban lévő út nem cserélődik le menet közben.", execution: GameRuleExecution.EditableGraph);
            Add("loading.policy", "Rakodási mennyiség, időzítés és indulás gráfjai", "Faanyag és logisztika", "Forwarder / processzor / teherautó típusa vagy egyedi flotta-ID", "Rakodási al-lépés / rönkciklus",
                "ForesTycoon/World/Cargo/ForwarderLoading.cs", "ForwarderLoading", "rules.behaviors|timber.stacks|machine.cargo|vehicle.cargo|machine.controllerState",
                "machine.cargo|machine.grapple|machine.work|vehicle.cargo|vehicle.transportState|timber.stacks", "Szerkeszthető fel- és lerakott mennyiség, részrakománnyal indulás, forwarder rönkciklus-idő, megfogás és elengedés. A ciklus időzítése rögzítve és mentve; az animáció ugyanazt követi. A készlet-, kapacitás- és rönkkorlát, valamint a térfogat–érték megőrzése kötelező.", execution: GameRuleExecution.EditableGraph);

            Add("terrain.generate", "Terep és vízmedencék", "Terep", "Node-rács / térképcella", "Új világ", "ForesTycoon.Map/Generation/TerrainGenerator.cs", "TerrainGenerator",
                "world.seed|terrain.settings", "terrain.height|terrain.moisture|terrain.water", "Seedből induló domborzat és hidrológia. A térképi nedvesség nem azonos az ökológiai gyökérzónavíz-készlettel.");
            Add("terrain.hydrology", "Folyók, medencék és kifolyók", "Terep", "Térképrács", "Generálás / terepváltozás", "ForesTycoon.Map/Generation/Hydrology.cs", "Hydrology",
                "terrain.height", "terrain.moisture|terrain.water|terrain.outlets", "Terepből származó állóvíz, folyók, nedvesség és a környezeti lefolyás határfeltételei.");
            Add("terrain.edit", "Helyi terepszerkesztés", "Terep", "Érintett node-ok és cellák", "Játékosparancs", game, "ExecuteElevationEdit",
                "commands|terrain.height|buildings.footprint|road.network", "terrain.height|forest.trees|water.routing",
                "Csak ténylegesen megváltozott cellák növényzete vész el, kitermelt készlet nélkül. Út- és épületmagasság védett; a többi erdő és a szimulációs idő megmarad.");
            var soilRule = Add("soil.catalog", "Talajprofilok és termőhely", "Talaj és klíma", "Térképcella", "Új világ", "ForesTycoon.Ecology/Soil/SoilLandscape.cs", "SoilLandscape",
                "world.seed|terrain.height|terrain.moisture", "soil.profile|soil.fertility|soil.bearing", "Térben összefüggő, seedelt talajprofilok; telítettség, szabadföldi vízkapacitás, hervadáspont, beszivárgás, drénezés, termékenység és a drénezésből adódó teherbírás.");
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
                "Simított regionális hőmérséklet-, páratartalom- és esőeltérések. Magasság élőben hűti a helyi levegőt; terraform nem generál új klímamagot.", "T_helyi = T + eltérés - normalizált_magasság × hűtés", execution: GameRuleExecution.EditableGraph);
            climate ??= ClimateDefinition.Default;
            Param(regional, "Régióméret", climate.RegionSizeTiles, "csempe"); Param(regional, "Hőmérsékleti kontraszt", climate.TemperatureContrast, "°C");
            Param(regional, "Csapadékkontraszt", climate.RainContrast, "1"); Param(regional, "Magassági hűtés", climate.ElevationCooling, "°C");
            Param(regional, "Páratartalom-kontraszt", climate.HumidityContrast, "1");
            Add("weather.events", "Seedelt időjárási események", "Talaj és klíma", "Világ", "Eseményhatárok és környezeti lépés",
                "ForesTycoon.Ecology/Climate/WeatherSystem.cs", "NextEvent", "world.seed|calendar.time|commands", "weather.rain|weather.snowfall|weather.forcing",
                "Évszakos, seedelt átmenetek: ősszel gyakori tartós eső, nyáron rövid zápor és erős vihar, télen havazás. A hó menthető vízkészlet, melegedéskor olvad. A csapadék rámpáinak integrálása pontos.",
                "T = 10 + 15 × sin(2π × (idő / erdőév - 0,125)); havazáskor T ≤ -1 °C; sugárzás = 1 - 0,75 × felhőzet", execution: GameRuleExecution.EditableGraph);
            Add("weather.evaporation", "Légköri párologtató igény", "Talaj és klíma", "Térképcella", "Környezeti lépés",
                "ForesTycoon.Rules/EnvironmentRules.cs", "weather.evaporation", "climate.local|weather.forcing", "water.potential",
                "A talajpárolgás és a növényzeti vízigény ugyanabból a helyi légköri kényszerből származik.",
                "E = (0,12 + max(0,T) × 0,015) × sugárzás × (1 - 0,5 × pára) × (1 + 0,035 × szél)", execution: GameRuleExecution.EditableGraph);

            // The water balance is the project's stock-and-flow rules (WaterRules): every flow is an editable graph.
            const string waterRules = "ForesTycoon.Rules/WaterRules.cs";
            Add("water.precipitation", "Csapadék, hóesés és források", "Vízháztartás", "Térképcella", "Szabálylépés", waterRules, "water.precipitation",
                "weather.rain|weather.snowfall|climate.rainMultiplier|forest.canopy|water.canopy", "water.canopy|water.surface|water.snow",
                "Áramlások kívülről: az eső a koronák borításuk és telítettségük szerint a lombkoronára, a többi a felszínre hull; a hó a hótakaróba, a források vize a felszínre kerül.",
                "korona += eső × borítás × (1 - telítettség); felszín += eső × (1 - borítás × (1 - telítettség))", GameRuleExecution.EditableGraph);
            Add("water.snowmelt", "Hóolvadás", "Vízháztartás", "Térképcella", "Szabálylépés", waterRules, "water.snowmelt",
                "water.snow|climate.local", "water.snow|water.surface",
                "Áramlás a hóból a felszínre: fagypont fölött fokonként 0,5 mm/óra.", "olvadás = 0,5 × max(0; T) mm/óra", GameRuleExecution.EditableGraph);
            Add("water.snowdrift", "Szél általi hóátrendezés", "Vízháztartás", "Szomszédos cellák", "Vízlépés után, fagyban", water, "RedistributeSnow",
                "water.snow|weather.forcing|terrain.height|forest.canopy", "water.snow",
                "A szél a következő szélirányú cellába hordja a havat; növényzet és domborzat mérsékli az áthordást. Zárt térképszélen megmarad a hó.", execution: GameRuleExecution.EditableGraph);
            Add("water.infiltration", "Beszivárgás", "Vízháztartás", "Térképcella", "Szabálylépés", waterRules, "water.infiltration",
                "water.surface|water.root|soil.profile|road.network|buildings.footprint", "water.surface|water.root",
                "Áramlás a felszínről a gyökérzónába a talaj beszivárgási képessége szerint, amíg a talaj telítődik; burkolt felületen 0,5 mm/óra.",
                "beszivárgás = talaj beszivárgása (burkolva 0,5) mm/óra", GameRuleExecution.EditableGraph);
            Add("water.evaporation", "Lomb-, felszín- és talajpárolgás", "Vízháztartás", "Térképcella", "Szabálylépés", waterRules, "water.evaporation",
                "water.potential|water.canopy|water.surface|water.root|forest.canopy", "water.canopy|water.surface|water.root",
                "Áramlások ki a rendszerből a levegő párologtató képessége szerint: a koronáról, a felszínről és a csupasz talajból (a vízellátottsággal és a borítással csökkenve).",
                "talajpárolgás = E × vízellátottság × 0,65 × (1 - borítás)", GameRuleExecution.EditableGraph);
            Add("water.uptake", "Fák vízfelvétele", "Vízháztartás", "Térképcella", "Szabálylépés", waterRules, "water.uptake",
                "water.potential|water.root|forest.canopy|road.network|buildings.footprint", "water.root",
                "Áramlás a gyökérzónából ki: a fák a lombfelületük és a párologtató képesség szerint vesznek fel vizet, szárazabb talajból kevesebbet.",
                "felvétel = E × 0,8 × LAI × vízellátottság", GameRuleExecution.EditableGraph);
            Add("water.drainage", "Mélybe szivárgás és alaplefolyás", "Vízháztartás", "Térképcella", "Szabálylépés", waterRules, "water.drainage",
                "water.root|water.deep|soil.profile", "water.root|water.deep",
                "Áramlás a szabadföldi vízkapacitás feletti vízből a mélyvízbe a drénezés szerint; a mélyvízből 0,15 mm/óra alaplefolyás távozik.",
                "", GameRuleExecution.EditableGraph);
            Add("water.runoff", "Felszíni lefolyás és kifolyás", "Vízháztartás", "Szomszédos csempék és kifolyók", "Szabálylépés", waterRules, "water.runoff",
                "water.surface|terrain.height|terrain.outlets", "water.surface",
                "Áramlás a vízszint szerint az alacsonyabb szomszédokra, az eséssel arányosan; a pálya szélén és az állóvizekben kifolyik. A térfogat megmarad.",
                "lefolyás = 15 × esés mm/óra, lejtés szerint elosztva", GameRuleExecution.EditableGraph);
            Add("water.stress", "Aszály, víztöbblet és havi vízválasz", "Vízháztartás", "Cellánként / fafajonként", "Környezeti lépés + erdőhónap", "ForesTycoon.Rules/EcologyResponseProgram.cs", "WaterFactor",
                "water.root|water.monthBudget|soil.profile|forest.species", "water.growthFactor",
                "A projekt gráfjai számolják az aszály és a pangóvíz simítását, valamint a fafaj pillanatnyi és havi vízválaszát. A havi integrál csak a kiválasztott állapot időátlagát gyűjti.", execution: GameRuleExecution.EditableGraph);
            Add("water.balance", "Vízmérleg és havi integrálzárás", "Vízháztartás", "Világ / cella", "Összegzés; hónapzárás", water, "BalanceError",
                "water.canopy|water.surface|water.root|water.deep|water.snow|weather.rain|weather.snowfall|water.evaporated|water.transpired|water.outflow|forest.month", "water.balance|water.monthBudget",
                "A szabályok minden áramlását könyveli: ami egy készletbe kívülről érkezik vagy onnan kilép, a szabálycsere része; a havi integrálok a hónap végén törlődnek.", "mérleghiba = tárolt víz (hóval együtt) - (induló víz + szabályok cseréje + korábbi natív tételek)");

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
            Add("forest.initialSize", "Fák kezdő méretgörbéi", "Erdő", "Új faegyed", "Minden fa létrehozásakor",
                "ForesTycoon.Rules/TreeInitialSizeRules.cs", "DefaultGraph", "forest.species|forest.trees|world.seed", "forest.trees",
                "A projekt három gráfja adja az új fa magasságát, törzsátmérőjét és koronasugarát. Ugyanez fut a generálás, erdőfestés, ültetés és természetes újulat során, egyedenként szeletelve.", execution: GameRuleExecution.EditableGraph);
            Add("forest.site", "Termőhelyi alkalmasság", "Erdő", "Cella / fafaj", "Generálás, fajajánló, telepítés és rátafrissítés", "ForesTycoon.Rules/EcologyResponseProgram.cs", "Fitness",
                "terrain.height|soil.fertility|forest.species", "forest.fitness", "Egy közös projektgráf adja a fafaj termőhelyi alkalmasságát minden hívónak. A nedvesség is beköthető; az alapgráf a termékenységet és a faj magasságigényét használja.", execution: GameRuleExecution.EditableGraph);
            Add("forest.competition", "Fény- és koronatér-versengés", "Erdő", "Faegyed és két szomszédsági gyűrű",
                "Havi előkészítés / helyi beavatkozás", "ForesTycoon.Rules/ForestCompetitionRules.cs", "DefaultGraph",
                "forest.trees|weather.forcing|water.growthFactor|forest.species", "forest.resources",
                "A projekt fapáronkénti árnyékolás- és térterhelésgráfjai, majd összesítő fény-, tér-, fajválasz- és erőforráskorlát-gráfjai. A motor csak a közös geometriapillanatképet és a sorrendtartó, szeletelt összegzést adja. Nincs külön gyökérverseny-büntetés.", execution: GameRuleExecution.EditableGraph);
            Add("forest.growth", "Fák növekedése", "Erdő", "Faegyed", "Havi és helyi rátafrissítés",
                "ForesTycoon.Rules/TreeGrowthRule.cs", "Default", "forest.fitness|forest.resources|water.growthFactor|forest.health|calendar.time|forest.species", "forest.trees|forest.volume",
                "A projekt gráfja adja az éves átmérő-, magasság- és koronanövekedést. A motor csak interpolálja a méretet.", execution: GameRuleExecution.EditableGraph);
            foreach (var vitality in ForesTycoon.Rules.TreeVitalityRules.Defaults())
                Add(vitality.Id, vitality.Name, "Erdő", vitality.Scope, "Erdőhónap",
                    "ForesTycoon.Rules/TreeVitalityRules.cs", "Defaults",
                    "forest.resources|forest.fitness|forest.species|forest.health|forest.stress|calendar.time",
                    vitality.Id == "forest.mortality" ? "forest.trees|forest.deadwood" : vitality.Id, vitality.Description, execution: GameRuleExecution.EditableGraph);
            Add("forest.regeneration", "Természetes újulat", "Erdő", "Üres cella és magszóró szomszédok", "Erdőhónap", "ForesTycoon.Rules/ForestRegenerationRules.cs", "DefaultGraph",
                "forest.trees|forest.species|forest.fitness|world.seed|calendar.time|road.network|buildings.footprint", "forest.trees",
                "A projekt gráfjai adják a magadó képességet, a megtelepedő faj pontszámát, a csírázást és az újulat kezdő állapotát. Szeletelt szomszédvizsgálat és determinisztikus sorsolás.", execution: GameRuleExecution.EditableGraph);
            Add("forest.canopy", "Lombborítás és vízigény visszacsatolása", "Erdő", "Térképcella", "Erdőrevízió-változás",
                "ForesTycoon.Ecology/Forest/ForestHydrology.cs", "HydrologyInputs", "forest.trees|forest.health|forest.species", "forest.canopy",
                "Koronafelület és egészség összegéből lombborítás, interceptiós kapacitás és LAI. A kivágás megváltoztatja a következő vízlépés igényét és esőfelfogását.", "borítás = 1 - exp(-koronafelület / cellaterület); LAI legfeljebb 6", execution: GameRuleExecution.EditableGraph);
            Add("forest.plant", "Telepítés és ültetvény-nyilvántartás", "Erdészeti beavatkozás", "Kijelölt cellák", "Játékosparancs", forest, "PlantInArea",
                "commands|terrain.height|terrain.water|road.network|buildings.footprint|forest.species|forest.fitness", "forest.trees|forest.plantations",
                "Érvényes termőhelyen választott fajból csemeték; területazonosító, telepítési év és kezdő egyedszám. A jelenlegi telepítés nem számol pénzügyi költséget.");
            Add("forest.designate", "Kitermelés kijelölése", "Erdészeti beavatkozás", "Kijelölt cellák", "Játékosparancs", logistics, "Designate",
                "commands|forest.volume", "harvest.sites", "A kijelölés megjegyzi a kitermelhető térfogatot; önmagában nem vágja ki a fákat.");
            Add("forest.fell", "Egész fák kivágása és helyi depó", "Erdészeti beavatkozás", "Faegyed / cella", "Processzor-munka vagy kitermelési művelet", individuals, "FellIndividual",
                "harvest.sites|forest.trees|machine.work", "forest.trees|forest.stumps|timber.localDepot",
                "Egész fák kerülnek a helyi rönkdepóba. Részrakodás nem zsugorítja a megmaradt fákat; a fák térfogata és a tönk mérete megmarad.", "V = π × átmérő² / 4 × magasság × 0,45");
            Add("forest.stumps", "Tönkök lebomlása", "Erdészeti beavatkozás", "Kivágott fa helye", "Szabálylépés / megjelenítés",
                "ForesTycoon.Rules/StumpDecayRule.cs", "Default", "forest.stumps|terrain.moisture|calendar.time", "forest.stumps",
                "A nedvesség gyorsítja, az átmérő lassítja a lebomlást. A szerkeszthető gráf teljes lebomláskor távolítja el a tönköt; a telepítést nem akadályozza.", "megszűnik, ha decayProgress ≥ 1", GameRuleExecution.EditableExpressions);

            var build = Add("road.build", "Útépítés és hálózat", "Utak és nyomok", "Útvonal cellái", "Játékosparancs", game, "ExecuteRoadPath",
                "commands|terrain.height|terrain.water|buildings.footprint|economy.result", "road.network|economy.expenses|forest.trees|water.routing",
                "Új építés csak a hitelkereten belüli egyenlegnél indul. Aszfalt/makadám út, rögzített úttestmagasság és kölcsönös csatlakozások. Az érintett növényzet azonnal törlődik, a látvány a következő kirajzolás előtt frissül. Csak ténylegesen létrehozott vagy módosított útcsempék növelik az építési költséget.");
            Add("road.repair", "Útjavítás", "Utak és nyomok", "Útvonal cellái", "Játékosparancs", game, "ExecuteRoadRepair",
                "commands|road.condition|road.network|economy.result", "road.condition|economy.expenses",
                "Csak a hitelkereten belüli egyenlegnél indul. A kopott útcsempék állapota 1-re áll; a költség a burkolat építési ára és a sérülés szerint számolódik.", "javítás = építési ár × 0,8 × sérülés");
            Add("road.weather", "Időjárási útromlás", "Utak és nyomok", "Közúti csempe", "Legalább 0,5 játék-s összegzése",
                "ForesTycoon.Rules/RuleInfluence.cs", "Compile", "terrain.moisture|weather.rain|calendar.time|road.condition|road.network", "road.condition",
                "A térképi hidrológiai nedvességet olvassa, nem a dinamikus gyökérzónavizet. Makadám erősebben romlik esőben; időt erdőévre váltja.",
                "makadám: 0,10 × (1 + 2,5 × nedvesség) + 1,6 × eső × (0,4 + nedvesség); aszfalt: 0,015 × (1 + nedvesség) + 0,08 × eső; állapot -= ráta × évek", GameRuleExecution.EditableExpressions);
            Add("traffic.wear", "Forgalmi út- és nyomkopás", "Utak és nyomok", "Áthaladás", "Csempére belépés",
                "ForesTycoon.Rules/TrafficWearRule.cs", "Default", "vehicle.mass|vehicle.cargo|vehicle.position|machine.position|road.condition|trail.wear", "road.condition|trail.wear|trail.idle",
                "Egy közös, szerkeszthető eseménygráf az útállapot, a nyomvályú és a pihenőidő változtatására. A mozgáskód csak az áthaladás adatait jelenti.", "", GameRuleExecution.EditableGraph);
            var trail = Add("trail.build", "Közelítőnyom kijelölése", "Utak és nyomok", "Útvonal cellái", "Játékosparancs", game, "ExecuteSkidTrailPath",
                "commands|terrain.water|buildings.footprint|road.network|economy.result", "trail.network|economy.expenses", "Csak a hitelkereten belüli egyenlegnél indul. Ideiglenes nyom száraz, épület nélküli talajon, az úthálózathoz csatlakozva; a növényzet nem teljes útépítésként kezelődik.");
            var age = Add("trail.age", "Nyom regenerációja és benövése", "Utak és nyomok", "Nyomcsempe", "Út-időjárási frissítés",
                "ForesTycoon.Rules/TrailAgeRule.cs", "Default", "trail.wear|trail.idle|calendar.time", "trail.wear|trail.idle|trail.network",
                "A gráfos szabály alapbeállítása: a vályú évente 0,4-et enyhül; a két szerkeszthető feltétel (elég használat nélküli idő és kis vályúmélység) együtt váltja ki a megszűnést. Az útvonalak újraellenőrződnek.",
                "kopás = max(0; kopás − 0,4 × lépés); tétlenség += lépés; megszűnik, ha tétlenség ≥ benövés és kopás ≤ 0,001", GameRuleExecution.EditableExpressions);
            Add("route.find", "Hálózati útvonalkeresés", "Járművek", "Hálózat / forrás és cél", "Utasítás / hálózatváltozás",
                "ForesTycoon.Map/Terrain/TerrainMap.Network.cs", "FindNetworkPath", "road.network|trail.network|trail.wear|buildings.footprint|timber.stacks", "vehicle.route",
                "Közös út- és nyomhálózat, kölcsönös csatlakozások, burkolat és nyomvályú szerinti költség. A flottában a dokkpárok útvonalai közül a legkisebb összesített költségű nyer; azonos költségnél a rendezett dokksorrend dönt.");
            var mass = Add("vehicle.mass", "Rakomány és össztömeg", "Járművek", "Teherautó", "Mozgási lépés", dynamics, "Mass",
                "vehicle.cargo", "vehicle.mass", "A rakomány térfogatból a rögzített frissfa-sűrűség alapján tömeg lesz. Ez a sűrűség jelenleg nem fajfüggő.", "tömeg = üres tömeg + m³ × fasűrűség", execution: GameRuleExecution.EditableGraph);
            Add("trail.trafficability", "Földút járhatósága", "Utak és nyomok", "Burkolatlan csempe", "Jármű haladásakor",
                "ForesTycoon/World/GameWorld.Rules.cs", "Trafficability", "soil.bearing|water.root|water.surface|trail.wear", "trail.trafficability",
                "Átázott, gyenge teherbírású talajon a földút és a közelítőnyom felülete romlik, a jármű gördülési ellenállása nő. A teherbírás a talajtípus paramétere; az átnedvesedés a gyökérzóna telítettsége (a szabadföldi vízkapacitás felétől a telítettségig) vagy a felszíni víz.",
                "járhatóság = 1 - átnedvesedés × (1 - teherbírás)", execution: GameRuleExecution.EditableGraph);
            var resistance = Add("vehicle.resistance", "Lejtés és menetellenállás", "Járművek", "Teherautó / aktuális útszakasz", "Legfeljebb 1/30 jármű-s", dynamics, "Resistance",
                "vehicle.mass|vehicle.speed|terrain.height|road.condition|road.network|trail.wear|trail.trafficability", "vehicle.resistance",
                "Gördülés, előjeles lejtőellenállás és légellenállás; durva, kopott felület növeli a gördülést.", "F = Crr × m × g × cos(atan(lejtés)) + m × g × sin(atan(lejtés)) + 0,5 × levegősűrűség × légellenállási felület × v²", execution: GameRuleExecution.EditableGraph);
            Add("vehicle.motion", "Gyorsulás, kanyar és fékezés", "Járművek", "Teherautó", "Legfeljebb 1/30 jármű-s",
                "ForesTycoon/World/Vehicles/Vehicle.cs", "VehicleDynamics.Step", "vehicle.time|vehicle.route|vehicle.resistance|vehicle.mass|road.condition|vehicle.broken", "vehicle.position|vehicle.speed|vehicle.fuel",
                "Teljesítmény- és tapadáskorlátos vonóerő, terhelés és kanyar szerinti célsebesség, végponti fékezés. Kopás lassít, meghibásodás megállít.", "célsebesség szorzója: 1 - 0,55 × útsérülés; üzemanyag a tényleges motor-munkából és alapjáratból", execution: GameRuleExecution.EditableGraph);
            var fuel = Add("vehicle.fuel", "Motorfogyasztás", "Járművek", "Teherautó", "Mozgási lépés", dynamics, "FuelPerKilowattHour",
                "vehicle.speed|vehicle.resistance|vehicle.mass", "vehicle.fuel", "A dinamika által ténylegesen kifejtett teljesítmény és alapjárat alapján számolt liter. Lejtőn fékezés nem termel negatív fogyasztást.", "liter = motorteljesítmény_kW × órák × fajlagos fogyasztás + alapjárati l/h × órák", execution: GameRuleExecution.EditableGraph);
            Add("vehicle.transfer", "Rakodási és szállítási állapotgép", "Járművek", "Teherautó / forrás / cél", "Járműidő",
                "ForesTycoon/World/Vehicles/VehicleSystem.cs", "Update", "vehicle.route|timber.stacks|vehicle.cargo|vehicle.broken", "vehicle.cargo|vehicle.transportState|timber.stacks|mill.stock",
                "Rakodás, fuvar, lerakodás, visszaút, várakozás. Hiányzó készletnél vár; megszakadt útvonalnál megőrzi a rakományt és megáll.", execution: GameRuleExecution.EditableGraph);

            var wear = Add("upkeep.wear", "Gép- és járműkopás, meghibásodás", "Fenntartás", "Flottajármű", "Munka / mozgás", upkeep, "Operate",
                "machine.work|vehicle.cargo|trail.network|vehicle.speed|vehicle.wear|vehicle.time", "vehicle.wear|vehicle.broken",
                "Terhelési szorzóval kopik; saját seedelt xorshift-generátorból sorsolt meghibásodás. Teherautó-terhelés: nyomon 2, úton 1, rakománytényező 1 + 0,5 × telítettség.",
                "kopás += 0,00025 × igénybevétel × s; meghibásodás-esély = 0,004 × kopás² × igénybevétel × s", execution: GameRuleExecution.EditableGraph);
            Add("upkeep.effects", "Kopás hatása a munkára és költségre", "Fenntartás", "Flottajármű", "Munka / költségelszámolás", upkeep, "FuelFactor",
                "vehicle.wear", "machine.pace|economy.fuelFactor", "Gépek munkaidejét és mozgását a PaceFactor lassítja. A teherautó PaceFactor jelenleg nincs bekötve a mozgásba; a fogyasztási szorzó a költségelszámolásban hat.",
                "üzemanyag-szorzó = 1 + 0,5 × kopás; géptempó = 1 - 0,3 × kopás", execution: GameRuleExecution.EditableGraph);
            var repair = Add("upkeep.repair", "Helyszíni szerelés", "Fenntartás", "Meghibásodott flottajármű", "Járműidő", upkeep, "RepairLeft",
                "vehicle.broken|vehicle.wear|vehicle.time", "vehicle.broken|vehicle.wear|economy.runningCosts", "Szerelési várakozás után részben enyhül a kopás; alap- és kopásarányos költség keletkezik.", execution: GameRuleExecution.EditableGraph);
            var service = Add("upkeep.service", "Telephelyi karbantartás", "Fenntartás", "Parkoló flottajármű", "Járműidő", upkeep, "Service",
                "vehicle.wear|fleet.state|vehicle.time", "vehicle.wear|economy.runningCosts", "A telephelyen álló, nem meghibásodott gép és teherautó kopása idővel csökken, a javított kopásmennyiségért költséget számol.", execution: GameRuleExecution.EditableGraph);

            Add("fleet.depot", "Telephely és induló flotta", "Faanyag és logisztika", "2×2 cella", "Játékosparancs", fleet, "PlaceDepotWork",
                "commands|terrain.height|terrain.water|road.network|forest.trees|harvest.sites|buildings.footprint", "buildings.footprint|fleet.state",
                "Sík, száraz, üres, út melletti telephely. Az első egy processzort, forwardert és rönkszállítót ad. Jelenleg nincs telephelyépítési vagy járművásárlási költség. Az engedély és az induló darabszámok közös gráfokból futnak.", execution: GameRuleExecution.EditableGraph);
            Add("fleet.orders", "Munkára küldés és hazahívás", "Faanyag és logisztika", "Flottajármű / cél", "Játékosparancs és állapotváltás", fleet, "AssignTruck",
                "commands|fleet.state|vehicle.route|timber.stacks|harvest.sites|mill.stock", "fleet.state|vehicle.route|machine.work", "Forrás és cél kijelölése, megközelítés, munka és hazatérés. A teherautó üresen távozik a forrástól, a gép előbb leadja a rakományt. A munkára küldési engedély közös gráf; az út és a rakomány biztonságát a motor őrzi.", execution: GameRuleExecution.EditableGraph);
            var processor = Add("machine.processor", "Processzor: vágás és kihordás", "Faanyag és logisztika", "Kitermelési cella / sarang", "1/30 jármű-s al-lépés", machine, "UpdateProcessor",
                "harvest.sites|forest.volume|timber.localDepot|timber.stacks|machine.pace|machine.work|vehicle.broken|vehicle.time", "machine.work|machine.cargo|timber.localDepot|timber.stacks|machine.fuel",
                "Kivágás, rönkfelvétel és sarangra hordás állapotgépe; kopás szerinti tempóval, rakománykapacitással és műveleti üzemanyag-rátákkal.", execution: GameRuleExecution.EditableGraph);
            var forwarder = Add("machine.forwarder", "Forwarder: sarangok közötti közelítés", "Faanyag és logisztika", "Gép / sarangpár", "1/30 jármű-s al-lépés", machine, "UpdateForwarder",
                "timber.stacks|machine.pace|vehicle.route|vehicle.broken|vehicle.time", "machine.cargo|timber.stacks|machine.position|machine.fuel", "Úton vagy nyomon állva, rönkönként rakodik. A markoló megfogáskor kiveszi, elengedéskor átadja a térfogatot és értéket; az alap ciklus 8 másodperc. A rakomány alulról épül és felülről fogy.", execution: GameRuleExecution.EditableGraph);
            var loading = Add("machine.logTransfer", "Rönk és érték átadása a markolóval", "Faanyag és logisztika", "Forwarder / sarang / markoló", "Rakodási al-lépés", "ForesTycoon/World/Cargo/ForwarderLoading.cs", "Step",
                "machine.cargo|timber.stacks|machine.pace|vehicle.time|machine.work", "machine.cargo|machine.grapple|timber.stacks|timber.cargoValue|machine.work",
                "A ciklus elején rögzül a hangolt rakodási rátából számolt idő. Megfogáskor a forrásból a markolóba, elengedéskor a célba kerül a térfogat és érték. A folyamatban levő ciklust és a markoló tartalmát a mentés megőrzi.",
                "ciklusidő = rönktérfogat / rakodási ráta; fázis = munkaidő / ciklusidő", execution: GameRuleExecution.EditableGraph);
            Param(loading, "Rönktérfogat", ForwarderLoading.LogVolume, "m³");
            Param(loading, "Megfogás fázisa", ForwarderLoading.Grip, "1");
            Param(loading, "Elengedés fázisa", ForwarderLoading.Release, "1");
            Add("machine.movement", "Erdészeti gépek mozgása és nyomterhelése", "Faanyag és logisztika", "Gép / útvonal", "1/30 jármű-s al-lépés", machine, "Advance",
                "vehicle.route|road.network|trail.network|trail.wear|machine.pace|machine.cargo|machine.work", "machine.position|trail.wear|machine.fuel",
                "Eltérő üres/rakott sebesség, útvonaljárhatóság ellenőrzése, nyomvályú és művelethez tartozó fogyasztás. A gépek külön kinematikával futnak, a teherautó fizikai modelljét nem használják.", execution: GameRuleExecution.EditableGraph);
            Add("timber.stack", "Sarang térfogat- és értékmérlege", "Faanyag és logisztika", "Sarang", "Rakodás / lerakodás", stacks, "Take",
                "machine.cargo|vehicle.cargo|timber.stacks", "timber.stacks|timber.cargoValue", "A kivett mennyiség arányában együtt mozog az érték. Üres sarang nullázódik; foglalt vagy nem üres sarang nem törölhető.", "átadott érték = készletérték × kivett térfogat / készlettérfogat");
            Add("timber.deliver", "Átadás és faanyag eladása", "Faanyag és logisztika", "Teherautó / sarang vagy malom", "Lerakodás", stacks, "Receive",
                "vehicle.cargo|timber.cargoValue|buildings.footprint", "timber.stacks|mill.stock|economy.income", "Sarangra értékkel együtt kerül a fa; malomba érkezéskor azonnal hozzáadódik a bevétel, a piaci árszorzóval. Nem a feldolgozás pillanatában történik az eladás.", "bevétel += rakományérték × piaci árszorzó");
            Add("mill.place", "Fűrészmalom elhelyezése", "Faanyag és logisztika", "2×2 cella", "Játékosparancs", logistics, "PlaceMill",
                "commands|terrain.height|terrain.water|forest.trees|harvest.sites|buildings.footprint", "buildings.footprint|mill.stock", "Sík, száraz, üres, erdő és kitermelés nélküli telek; út és erdő később nem építhető a lábnyomára. Jelenleg nincs malomépítési költség.");
            Add("mill.process", "Fűrészmalom feldolgozása", "Faanyag és logisztika", "Malom", "Járműidő", logistics, "Processed",
                "mill.stock|vehicle.time", "mill.stock|mill.processed", "A készletből korlátozott ütemben feldolgozott mennyiség lesz; jelenleg nincs új terméklánc vagy feldolgozási bevétel.", "feldolgozás = min(készlet, 0,25 × jármű-s) m³", execution: GameRuleExecution.EditableGraph);
            var diesel = Add("economy.fuel", "Üzemanyag-költség", "Gazdaság", "Flottajármű / vállalat", "Gépmunka vagy teherautó elszámolás", stacks, "Burn",
                "vehicle.fuel|machine.fuel|economy.fuelFactor", "economy.runningCosts", "A tényleges fogyasztás és a kopási szorzó alapján költséget számol; a modellnek jelenleg nincs külön üzemanyagkészlete.", "költség += liter × dízelár");
            Add("economy.balance", "Bevétel, kiadás és eredmény", "Gazdaság", "Vállalat", "Műveleti elszámolás",
                game, "Balance", "economy.income|economy.expenses|economy.runningCosts", "economy.result",
                "Bevétel mínusz építési/javítási és üzemeltetési költségek. A hitelkeret alatt új út, nyom és útjavítás nem indul; az alapértelmezés korlátlan. Erdőápolási kiadás és kamat még nincs.", "eredmény = bevétel - építési kiadások - működési költségek");

            Add("wildlife.habitat", "Vadállomány élőhelye", "Vadak", "Térképcella / állat", "Erdő- vagy tereprevízió változás",
                "ForesTycoon/World/WildlifeSystem.cs", "CollectWildlifeSpots", "forest.trees|terrain.height|terrain.water|road.network", "wildlife.state",
                "Járható élőhelyből induló állatok; élőhely nélkül eltűnnek. Nincs demográfiai vagy erdei vadkármodell.");
            Add("wildlife.forage", "Éhség és helyi táplálékfogyás", "Vadak", "Állat / cella", "Világlépés",
                "ForesTycoon/World/WildlifeSystem.cs", "Hunger", "wildlife.state|terrain.moisture|wildlife.time", "wildlife.state|wildlife.forage",
                "Éhség nő, állva táplálkozva csökken; helyi táplálék fogy és idővel regenerálódik. Ez nem írja a faegyedek egészségét.");
            Add("wildlife.movement", "Búvóhely és mozgásválasztás", "Vadak", "Állat / szomszédos cellák", "Világlépés",
                "ForesTycoon/World/WildlifeSystem.cs", "TargetTile", "wildlife.time|wildlife.state|wildlife.forage|forest.trees|weather.rain|terrain.height|terrain.water|road.network", "wildlife.state",
                "Éhség, nedvesség, eső alatti érett erdő, vándorlási igény és fordulási korlátok alapján választ célpontot; a járást a talajhoz illeszti.");
            Add("visual.tree", "Faállapotból procedurális modell", "Megjelenítés", "Faegyed", "Megjelenítési frissítés",
                "ForesTycoon.Ecology/Shape/TreeShapeSpec.cs", "TreeShapeSpec", "forest.trees|forest.species|forest.health|forest.resources|visual.foliage|calendar.time", "visual.tree",
                "Faj, életfázis, méret, fény, életerő és fenológia választják a koronát és ágszerkezetet. A modellméret a szimulációt követi; a renderer nem növeszti a fát.", "", GameRuleExecution.Presentation);
            Add("visual.truck", "Teherautómodell és látható rakomány", "Megjelenítés", "Teherautó", "Renderképkocka",
                "ForesTycoon/Rendering/Models/GlbTruckModel.cs", "GlbTruckModel", "vehicle.position|vehicle.speed|vehicle.cargo|terrain.height", "visual.truck",
                "Importált modell, kerékmozgás, rakomány és útfelületet követő dőlés. A megjelenítés olvassa a szimulációt.", "", GameRuleExecution.Presentation);
            Add("visual.weather", "Időjárási látvány", "Megjelenítés", "Világ / felület", "Renderképkocka",
                "ForesTycoon/Rendering/Scene/TerrainRenderer.cs", "TerrainRenderer", "weather.rain|weather.snowfall|weather.forcing|water.snow|water.surface|forest.trees|road.condition", "visual.weather",
                "Eső, hóesés, felhő, villámlás és nedves felületek. A talaj, utak és fák hóborítása a helyi szimulált hókészletet követi, olvadáskor eltűnik. A kézi látványteszt külön beállítás.", "", GameRuleExecution.Presentation);
            Add("visual.phenology", "Lombfázis és fajfüggő életfázis", "Megjelenítés", "Faegyed", "Erdőév tört része",
                "ForesTycoon.Ecology/Species/TreePhenology.cs", "At", "forest.trees|forest.species|calendar.time|world.seed", "visual.foliage",
                "Rügyfakadás, teljes lomb, őszi szín és lombhullás fajonkénti naptárból, egyedi seedelt eltolással. A jelenlegi HydrologyInputs levélfelületében ez a lombfázis még nem szerepel.", "", GameRuleExecution.Presentation);
            var deadwoodVisual = Add("visual.deadwood", "Holtfa és tönk megjelenítése", "Megjelenítés", "Elhalt vagy kivágott egyed", "Renderfrissítés",
                "ForesTycoon/Terrain/Forest/Terrain.ForestStumps.cs", "Stump", "forest.stumps|forest.deadwood|calendar.time", "visual.deadwood",
                "Kivágott fa méretű, öregedő tönkök és elhalás után idővel fekvő holtfa. Állapotból és korból következik a látvány, LOD-váltáskor is.", "", GameRuleExecution.Presentation);
            deadwoodVisual.Sources.Add(new("ForesTycoon/Terrain/Forest/Terrain.ForestIndividuals.cs", "DeathYear"));
            Add("visual.machines", "Erdészeti gépmodellek", "Megjelenítés", "Processzor / forwarder", "Renderképkocka",
                "ForesTycoon/Rendering/Scene/ForestMachineRenderer.cs", "ForestMachineRenderer", "machine.position|machine.work|machine.cargo|machine.grapple|terrain.height|trail.wear", "visual.machines",
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
                owner.Parameters.Add(new(spec.Name, tuning.BaseValue(spec.Key), spec.Unit, spec.Id, spec.Min, spec.Max, spec.Default));
            }
            var behaviorRules = WorldBehaviorPolicy.Hooks.Select(h => h.Id).Concat(GameTuning.Specs.Select(s => s.Rule))
                .Append("machine.forwarder").Append("machine.processor").ToHashSet();
            foreach (var rule in catalog.Rules.Where(r => behaviorRules.Contains(r.Id)))
                rule.Reads = rule.Reads.Append("rules.behaviors").Distinct().ToArray();
            foreach (var rule in catalog.Rules.Where(r => r.Id is "machine.forwarder" or "machine.processor" or "vehicle.transfer" or "fleet.orders"))
                rule.Reads = rule.Reads.Append("machine.controllerState").ToArray();
            catalog.Gaps.AddRange(new[] {
                "A logisztikai döntések mért bemenetekből, a közös Szabályok gráfjaiból futnak. A fizikai átadás, útgeometria és készletkönyvelés motorfeladat; új játékosparancs és tetszőleges fizikai munkafázis még nem hozható létre.",
                "A talaj-, klíma- és fafajparaméterek egyelőre csak tájékoztató adatok: a világ létrehozásakor rögzülnek, a szerkesztőből nem hangolhatók.",
                "Talajtömörödés, erózió és útkialakítás miatti részletes vízelvezető műtárgyak nincsenek külön folyamatként megvalósítva.",
                "A vadak nem rágják a csemetéket; kártevő-, fertőzés- és tűzterjedési rendszer még nincs.",
                "Gyérítés, ápolás és ezek pénzügyi költsége még nem külön játékosparancs; telepítés és kitermelés van.",
                "A hitelkeret csak az út- és nyomépítést, valamint az útjavítást korlátozza; alapértéke korlátlan. Járművásárlás, kamat és beépített piaci kereslet–kínálat modell még nincs: a piaci árszorzó képlettel vagy saját készlettel modellezhető.",
                "A fa térfogatának pénzbeli értékét a logisztikai TimberPrice adja; a teherautó fasűrűsége minden fajra azonos.",
                "A natív folyamatok több írója és visszacsatolása a meglévő futtatási sorrend szerint működik; a katalógus élei nem új végrehajtási sorrendet jelentenek." });
            catalog.Validate(); return catalog;
        }
    }
}
