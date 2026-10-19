using System;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>Authored natural regeneration decisions; the host supplies ordered adjacent source candidates.</summary>
public static class ForestRegenerationRules
{
    public static readonly string[] Inputs = { "source.maturity", "source.health", "source.biomass", "tile.moisture", "tile.suitability",
        "tile.waterResponse", "tile.crowding", "species.shadeTolerance", "species.maximumBiomass", "seeds.supply", "step.years", "seeds.random", "tile.neighborBiomass", "source.distance", "source.age", "species.matureAge", "tile.fertility", "tile.elevation", "environment.radiation" };
    public static readonly BehaviorHook[] Hooks = {
        Hook("forest.seedCrowding", "Újulat lombterhelése", 1),
        Hook("forest.seedlingAge", "Újulat kezdő életkora", 1e3),
        Hook("forest.seedSupply", "Magadó képesség", 1e6),
        Hook("forest.seedSelection", "Megtelepedő fafaj alkalmassága", 1e6),
        Hook("forest.germination", "Csírázás és megtelepedés", 1),
        Hook("forest.seedlingCount", "Megtelepedő csemeték száma", 128),
        Hook("forest.seedlingHealth", "Újulat kezdő egészsége", 1)
    };
    private static BehaviorHook Hook(string id, string name, double max) => new(id, name, "regeneration",
        id == "forest.seedlingCount" ? "db" : id == "forest.seedlingAge" ? "év" : "", 0, max, "", "", id switch {
            "forest.seedCrowding" => "A szomszédok átlagos biomasszájából képezett lombterhelés, 0–1. A hatásokat te kapcsolod össze.",
            "forest.seedSupply" => "Minden szomszédos élő állományra: 0 = nincs mag, nagyobb érték = több mag. Az érettség, egészség és távolság szabadon beköthető.",
            "forest.seedSelection" => "A magadó jelöltek pontszáma. A legnagyobb pozitív értékű jelölt fafaja telepedhet meg. A magellátottság itt az adott forrás saját magmennyisége.",
            "forest.germination" => "0 = nincs megtelepedés; pozitív kimenet = megtelepedés. A sorsolást te kapcsolod az esélyhez. A magellátottság itt az összes szomszéd magmennyiségének összege.",
            "forest.seedlingCount" => "A megtelepedő csemeték száma, egészre kerekítve. 0 = nincs újulat. Egy megtelepedés legfeljebb 128 csemete; a következő hónapokban tovább terjedhet.",
            "forest.seedlingAge" => "A csemeték kezdő átlagéletkora erdőévben. A kezdő méretet a közös, fajfüggő méret-inicializáló adja; az egyedek seedelt eltérést kapnak.",
            "forest.seedlingHealth" => "A csemeték kezdő egészsége, 0–1. A nulla megakadályozza a megtelepedést.",
            _ => throw new ArgumentException("Unknown regeneration graph.", nameof(id))
        }, false, id == "forest.seedCrowding" ? new[] { "tile.neighborBiomass", "tile.moisture" } : Inputs);
}

/// <summary>Immutable measured inputs to one authored regeneration decision.</summary>
public readonly record struct ForestRegenerationInputs
{
    public double Maturity { get; init; }
    public double Health { get; init; }
    public double Biomass { get; init; }
    public double Moisture { get; init; }
    public double Suitability { get; init; }
    public double Water { get; init; }
    public double Crowding { get; init; }
    public double ShadeTolerance { get; init; }
    public double MaximumBiomass { get; init; }
    public double Supply { get; init; }
    public double Years { get; init; }
    public double Random { get; init; }
    public double NeighborBiomass { get; init; }
    public double Distance { get; init; }
    public double Age { get; init; }
    public double MatureAge { get; init; }
    public double Fertility { get; init; }
    public double Elevation { get; init; }
    public double Radiation { get; init; }
}
