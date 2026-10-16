using System;
using System.Linq;

namespace ForesTycoon.Rules;

// The order is a runtime binding, independent of catalog order and display labels.
public enum EnvironmentResponse
{
    EventType, EventDuration, EventPeak, ForcedDuration, ForcedPeak, Ramp,
    Temperature, Humidity, Wind, Cloud, Radiation, Evaporation,
    LocalTemperature, LocalHumidity, LocalRain,
    CrownArea, Interception, LeafArea, Cover, CanopyCapacity, LeafIndex,
    SnowDirection, SnowShelter, SnowRetention, SnowRelease, SoilWetness, Trafficability,
    InitialType, InitialDuration, InitialPeak, SoilBearing
}

/// <summary>Authored environment decisions. Random draws, conservative transport and scheduling belong to the host.</summary>
public static class EnvironmentRules
{
    private static BehaviorHook H(string id, string name, double min, double max, string schedule, params string[] inputs) =>
        new(id, name, "environment", "", min, max, "", "", schedule, false, inputs);
    public static readonly BehaviorHook[] Hooks = {
        H("weather.eventType", "Következő időjárás", 0, 4, "Időjárási esemény végén; 0 napos, 1 felhős, 2 eső, 3 hó, 4 vihar", "calendar.season", "event.random"),
        H("weather.eventDuration", "Időjárási esemény hossza", .001, 3600, "Új esemény kezdetén; alaptempójú másodperc, évszakhatáron lezárva", "calendar.season", "event.type", "event.random"),
        H("weather.eventPeak", "Csapadék csúcsintenzitása", 0, 100, "Új csapadékesemény kezdetén", "calendar.season", "event.type", "event.random"),
        H("weather.forcedDuration", "Kért időjárás alapideje", .001, 3600, "Időjárási parancsnál, ha nincs külön időtartam", "event.type"),
        H("weather.forcedPeak", "Kért csapadék alapintenzitása", 0, 100, "Időjárási parancsnál, ha nincs külön intenzitás", "event.type"),
        H("weather.ramp", "Csapadék fel- és lefutása", .000001, 3600, "Eseményen belül; másodperc, legfeljebb az esemény fele", "event.seconds", "calendar.timeScale"),
        H("weather.temperature", "Időjárás hőmérséklete", -100, 100, "Légköri állapot lekérdezésekor", "calendar.seasonWave", "event.type", "calendar.yearPhase", "calendar.season"),
        H("weather.humidity", "Időjárás páratartalma", 0, 1, "Légköri állapot lekérdezésekor", "event.type"),
        H("weather.wind", "Időjárás szélsebessége", 0, 100, "Légköri állapot lekérdezésekor", "event.type", "event.rainRate", "event.peak"),
        H("weather.cloud", "Felhőzet átmenete", 0, 1, "Légköri állapot lekérdezésekor", "event.type", "event.elapsed", "calendar.timeScale", "event.previousCloud"),
        H("weather.radiation", "Felhőzet fényhatása", 0, 10, "Légköri állapot lekérdezésekor", "weather.cloud"),
        H("weather.evaporation", "Levegő párologtató képessége", 0, 1000, "A vízáramlási gráfok csempemezőjének lekérdezésekor", "weather.radiation", "weather.temperature", "weather.humidity", "weather.wind"),
        H("climate.temperature", "Helyi hőmérséklet", -100, 100, "Csempénként; terepváltozás és időjárás után újraszámolva", "weather.temperature", "region.temperatureAnomaly", "tile.elevation", "climate.elevationCooling"),
        H("climate.humidity", "Helyi páratartalom", 0, 1, "Csempénként; légköri állapotból", "weather.humidity", "region.humidityAnomaly"),
        H("climate.rain", "Helyi csapadékszorzó", 0, 10, "Csempénként; a seedelt régiómintából", "region.rainMultiplier"),
        H("forest.crownArea", "Fa vízháztartási koronaterülete", 0, 1e6, "Növényzeti állapot frissítésekor, fánként", "tree.crownRadius", "tree.health"),
        H("forest.interception", "Fa koronájának vízfogása", 0, 1e7, "Növényzeti állapot frissítésekor, fánként", "canopy.crownArea", "species.evergreen"),
        H("forest.leafArea", "Fa lombfelülete", 0, 1e7, "Növényzeti állapot frissítésekor, fánként", "canopy.crownArea", "species.evergreen"),
        H("forest.cover", "Csempe lombborítása", 0, 1, "Csempénként, az egyedi koronaterületek összegéből", "canopy.crownArea", "tile.area"),
        H("forest.canopyCapacity", "Lombkorona vízkapacitása", 0, 1000, "Csempénként, a fák vízfogásának összegéből", "canopy.cover", "canopy.interception", "canopy.crownArea"),
        H("forest.leafIndex", "Csempe lombfelületi indexe", 0, 100, "Csempénként, a fák lombfelületének összegéből", "canopy.leafArea", "tile.area"),
        H("snow.direction", "Hó szélirány szerinti célpontszáma", -10, 10, "Hóátrendezéskor, a legfeljebb négy szomszédra; legnagyobb pozitív érték a cél", "neighbor.directionX", "neighbor.directionY"),
        H("snow.shelter", "Hó szélvédettsége", 0, 1, "Hóátrendezéskor, csempénként", "canopy.cover", "tile.upwindRelief"),
        H("snow.retention", "Helyben maradó hó", 0, 1e6, "Hóátrendezéskor, csempénként; mm vízegyenérték", "snow.shelter"),
        H("snow.release", "Szél által elmozdított hó aránya", 0, 1, "Hóátrendezéskor, csempénként; csak a visszatartott készleten felül", "weather.temperature", "weather.wind", "snow.shelter", "step.hours", "tile.snow"),
        H("soil.wetness", "Járhatósági átnedvesedés", 0, 1, "Burkolatlan csempe járhatóságának lekérdezésekor", "tile.soilWater", "tile.surfaceWater", "soil.fieldCapacity", "soil.saturation"),
        H("trail.trafficability", "Földút járhatósága", 0, 1, "Burkolatlan csempén haladás és útvonaltervezés", "tile.wetness", "soil.bearingCapacity"),
        H("weather.initialType", "Induló időjárás", 0, 4, "Új világ létrehozásakor; 0 napos, 1 felhős, 2 eső, 3 hó, 4 vihar"),
        H("weather.initialDuration", "Induló időjárás hossza", .001, 3600, "Új világ létrehozásakor; alaptempójú másodperc"),
        H("weather.initialPeak", "Induló csapadék erőssége", 0, 100, "Új világ létrehozásakor; csak csapadékos induló időjárásnál"),
        H("soil.bearing", "Talaj teherbírási válasza", 0, 1, "Járhatóság és áthaladási kopás lekérdezésekor", "soil.bearingCapacity", "soil.bearingDefined", "soil.drainage")
    };
    public static BehaviorGraph DefaultGraph(BehaviorHook hook)
    {
        const string cloudTarget = "ha(event.type < 0.5; 0.12; ha(event.type < 1.5; 0.65; 1))";
        const string progress = "korlát(event.elapsed / (10 * calendar.timeScale); 0; 1)";
        string expression = hook.Id switch {
            "weather.initialType" => "0",
            "weather.initialDuration" => "150",
            "weather.initialPeak" => "0",
            "soil.bearing" => "ha(soil.bearingDefined > 0.5; soil.bearingCapacity; korlát(0.25 + soil.drainage / 8; 0.25; 0.9))",
            "weather.eventType" => "ha(calendar.season < 0.5; ha(event.random < 0.35; 0; ha(event.random < 0.65; 1; ha(event.random < 0.96; 2; 4))); ha(calendar.season < 1.5; ha(event.random < 0.45; 0; ha(event.random < 0.70; 1; ha(event.random < 0.85; 2; 4))); ha(calendar.season < 2.5; ha(event.random < 0.10; 0; ha(event.random < 0.38; 1; ha(event.random < 0.96; 2; 4))); ha(event.random < 0.15; 0; ha(event.random < 0.40; 1; 3)))))",
            "weather.eventDuration" => "ha(event.type < 0.5; 45 + event.random * 65; ha(event.type < 1.5; 25 + event.random * 35; ha(event.type < 2.5; ha(abs(calendar.season - 1) < 0.5; 8 + event.random * 12; ha(abs(calendar.season - 2) < 0.5; 45 + event.random * 45; 25 + event.random * 30)); ha(event.type < 3.5; 40 + event.random * 35; ha(abs(calendar.season - 1) < 0.5; 8 + event.random * 10; 20 + event.random * 15)))))",
            "weather.eventPeak" => "ha(event.type < 1.5; 0; ha(event.type < 2.5; ha(abs(calendar.season - 1) < 0.5; 16; 6) + event.random * 12; ha(event.type < 3.5; 4 + event.random * 6; ha(abs(calendar.season - 1) < 0.5; 32; 20) + event.random * 20)))",
            "weather.forcedDuration" => "ha(event.type > 3.5; 45; ha(abs(event.type - 2) < 0.5; 90; 180))",
            "weather.forcedPeak" => "ha(event.type < 1.5; 0; ha(event.type < 2.5; 12; ha(event.type < 3.5; 8; 32)))",
            "weather.ramp" => "min(10 * calendar.timeScale; event.seconds * 0.2)",
            "weather.temperature" => "ha(abs(event.type - 3) < 0.5; min(-1; 10 + 15 * calendar.seasonWave); 10 + 15 * calendar.seasonWave)",
            "weather.humidity" => "ha(event.type > 1.5; 0.9; 0.55)",
            "weather.wind" => "1.5 + ha(event.type > 3.5; 8.5; ha(abs(event.type - 2) < 0.5; 1.5; 0)) * korlát(event.rainRate / max(1; event.peak); 0; 1)",
            "weather.cloud" => $"event.previousCloud + ({cloudTarget} - event.previousCloud) * ({progress} * {progress} * (3 - 2 * {progress}))",
            "weather.radiation" => "1 - weather.cloud * 0.75",
            "weather.evaporation" => "(0.12 + max(0; weather.temperature) * 0.015) * weather.radiation * (1 - weather.humidity * 0.5) * (1 + weather.wind * 0.035)",
            "climate.temperature" => "weather.temperature + region.temperatureAnomaly - tile.elevation * climate.elevationCooling",
            "climate.humidity" => "korlát(weather.humidity + region.humidityAnomaly; 0; 1)",
            "climate.rain" => "region.rainMultiplier",
            "forest.crownArea" => "3.141592653589793 * tree.crownRadius * tree.crownRadius * tree.health",
            "forest.interception" => "canopy.crownArea * ha(species.evergreen > 0.5; 2.5; 1.5)",
            "forest.leafArea" => "canopy.crownArea * ha(species.evergreen > 0.5; 3.5; 2.5)",
            "forest.cover" => "1 - exp(-canopy.crownArea / tile.area)",
            "forest.canopyCapacity" => "ha(canopy.crownArea > 0; canopy.cover * canopy.interception / canopy.crownArea; 0)",
            "forest.leafIndex" => "korlát(canopy.leafArea / tile.area; 0; 6)",
            "snow.direction" => "neighbor.directionX + 0.35 * neighbor.directionY",
            "snow.shelter" => "korlát(canopy.cover + tile.upwindRelief * 8; 0; 1)",
            "snow.retention" => "0.25 + snow.shelter * 3",
            "snow.release" => "ha(weather.temperature > 0; 0; ha(weather.wind > 0.8; ha(tile.snow > 0.25; 1 - exp(-step.hours * (weather.wind - 0.8) * 4 * (1 - snow.shelter * 0.85)); 0); 0))",
            "soil.wetness" => "korlát(max((tile.soilWater - soil.fieldCapacity / 2) / (soil.saturation - soil.fieldCapacity / 2); tile.surfaceWater / 10); 0; 1)",
            "trail.trafficability" => "1 - tile.wetness * (1 - soil.bearingCapacity)",
            _ => throw new ArgumentException("Unknown environment response.", nameof(hook))
        };
        var graph = RuleFormula.Compile(expression, new() { Id = "default.environment." + hook.Id, Name = hook.Name, Hook = hook.Id, Kind = hook.Kind },
            hook.Inputs.Select(f => new FormulaName(f, BehaviorOperation.Input, f)).ToArray());
        graph.Nodes.Single(n => n.Id == graph.Output).Name = hook.Name;
        return graph;
    }
}
