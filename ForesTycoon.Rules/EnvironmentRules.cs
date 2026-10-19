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
}
