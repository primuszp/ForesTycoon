using System.Collections.Generic;

namespace ForesTycoon.Rules;

/// <summary>
/// The water balance as a stock-and-flow model: per tile the stocks are crown water, surface water, snow, root-zone and
/// deep water; every arrow between them is a flow of a starter rule, edited like any other rule. Rates are per hour of the
/// environment's clock (rain, evaporation and soil rates are hourly). The defaults are plausible gameplay values, not a
/// copy of the former code; see docs/rule-editor.md.
/// </summary>
public static class WaterRules
{
    private static RuleEffect Flow(string name, string from, string to, params InfluenceTerm[] terms) => new() {
        Target = from, Mode = "flow", To = to, FlowName = name, Influence = new() { PerYear = true, PerHour = true, Terms = new(terms) } };

    private static InfluenceTerm Term(string name, double amount, string amountField = "", params InfluenceFactor[] factors) =>
        new() { Name = name, Decrease = false, Amount = amount, AmountField = amountField, Factors = new(factors) };

    /// <summary>A multiplier rising linearly with <paramref name="source"/> from 0 to <paramref name="high"/> (then held).</summary>
    private static InfluenceFactor Rising(string source, double high) => new() { Source = source, InputLow = 0, InputHigh = high, OutputLow = 0, OutputHigh = high };
    private static InfluenceFactor Map(string source, double inLow, double inHigh, double outLow, double outHigh) =>
        new() { Source = source, InputLow = inLow, InputHigh = inHigh, OutputLow = outLow, OutputHigh = outHigh };
    private static InfluenceCondition Is(string field, bool value) => new() { Field = field, Comparison = value ? "atLeast" : "atMost", Value = .5 };

    public static IEnumerable<WorldRule> Defaults()
    {
        yield return new() { Id = "water.precipitation", Name = "Csapadék és források", Scope = "tile",
            Description = "Az eső a lombkoronára és a talajra hull (a koronák a borításuk arányában fogják fel, amíg el nem telnek), a hó a hótakaróba kerül, a források a felszínre adnak vizet.",
            Effects = {
                Flow("eső a koronára", "", "canopyWater", Term("Felfogott eső", 1, "rain", Rising("cover", 1), Map("canopyFill", 0, 1, 1, 0))),
                Flow("eső a talajra", "", "surfaceWater",
                    Term("Koronák közötti eső", 1, "rain", Map("cover", 0, 1, 1, 0)),
                    Term("Teli koronáról lecsöpögő eső", 1, "rain", Rising("cover", 1), Rising("canopyFill", 1))),
                Flow("hóesés", "", "snow", Term("Hóesés", 1, "snowfall")),
                Flow("forrásvíz", "", "surfaceWater", Term("Forrás", 1, "springRate")),
            } };
        yield return new() { Id = "water.snowmelt", Name = "Hóolvadás", Scope = "tile",
            Description = "Fagypont fölött a hó olvad, az olvadékvíz a felszínre kerül; minél melegebb, annál gyorsabban.",
            Effects = { Flow("hóolvadás", "snow", "surfaceWater", Term("Olvadás fokonként", .5, "", Rising("temperature", 20))) } };
        yield return new() { Id = "water.infiltration", Name = "Beszivárgás", Scope = "tile",
            Description = "A felszíni víz a talaj beszivárgási képessége szerint a gyökérzónába jut, amíg az telítődik; burkolt felületen alig.",
            Effects = { Flow("beszivárgás", "surfaceWater", "rootWater",
                new InfluenceTerm { Name = "Beszivárgás a talajba", Decrease = false, Amount = 1, AmountField = "infiltration", Conditions = { Is("sealed", false) } },
                new InfluenceTerm { Name = "Burkolt felületen", Decrease = false, Amount = .5, Conditions = { Is("sealed", true) } }) } };
        yield return new() { Id = "water.evaporation", Name = "Párolgás", Scope = "tile",
            Description = "A levegő párologtató képessége szerint párolog a koronákon és a felszínen álló víz, és a csupasz talaj nedvessége, annál kevésbé, minél szárazabb.",
            Effects = {
                Flow("párolgás a koronáról", "canopyWater", "", Term("Korona", 1, "potentialEvaporation")),
                Flow("párolgás a felszínről", "surfaceWater", "", Term("Felszín", 1, "potentialEvaporation")),
                Flow("talajpárolgás", "rootWater", "", Term("Csupasz talaj", 1, "potentialEvaporation", Rising("waterSupply", 1), Map("cover", 0, 1, .65, 0))),
            } };
        yield return new() { Id = "water.uptake", Name = "Fák vízfelvétele", Scope = "tile",
            Description = "A fák a lombfelületük és a levegő párologtató képessége szerint vesznek fel vizet a gyökérzónából; szárazabb talajból kevesebbet. A fák növekedése a talaj vízellátottságát látja.",
            Effects = { Flow("párologtatás (fák)", "rootWater", "",
                new InfluenceTerm { Name = "Lombfelület szerint", Decrease = false, Amount = 1, AmountField = "potentialEvaporation",
                    Factors = { Map("leafAreaIndex", 0, 10, 0, 8), Rising("waterSupply", 1) }, Conditions = { Is("sealed", false) } }) } };
        yield return new() { Id = "water.drainage", Name = "Mélybe szivárgás és alaplefolyás", Scope = "tile",
            Description = "A szabadföldi vízkapacitás feletti víz a talaj drénezése szerint a mélybe szivárog; a mélyvízből lassan alaplefolyás távozik.",
            Effects = {
                Flow("mélybe szivárgás", "rootWater", "deepWater", Term("Többletvíz", 1, "drainage", Rising("excessWater", 1))),
                Flow("alaplefolyás", "deepWater", "", Term("Alaplefolyás", .15)),
            } };
        yield return new() { Id = "water.runoff", Name = "Felszíni lefolyás", Scope = "tile",
            Description = "A felszíni víz a vízszint szerint az alacsonyabb szomszédokra folyik, annál gyorsabban, minél nagyobb az esés; a pálya szélén és az állóvizekben kifolyik.",
            Effects = {
                new RuleEffect { Target = "surfaceWater", Mode = "flow", To = "neighbours.surfaceWater", Spread = "downhill", Level = "waterLevel", FlowName = "lefolyás",
                    Influence = new() { PerYear = true, PerHour = true, Terms = { new() { Name = "Esés szerint", Decrease = false, Amount = 15,
                        Factors = { Rising("drop", 1000) }, Conditions = { new() { Field = "surfaceWater", Comparison = "atLeast", Value = .5 } } } } } },
                Flow("kifolyás", "surfaceWater", "", new InfluenceTerm { Name = "Kifolyás a pályáról", Decrease = false, Amount = 10,
                    Factors = { Rising("surfaceWater", 1000) }, Conditions = { Is("outlet", true) } }),
            } };
    }
}
