using ForesTycoon.Rules;

namespace ForesTycoon;

[RuleEntity("regeneration", Abstract = true, Display = "Természetes újulat")]
internal interface IRegenerationQuery
{
    [RuleRelation("Magadó állomány", Display = "Magadó állomány")] IRegenerationSource Source { get; }
    [RuleRelation("Célcsempe", Display = "Célcsempe")] IRegenerationTile Tile { get; }
    [RuleRelation("Fafaj", Display = "Fafaj")] IRegenerationSpecies Species { get; }
    [RuleRelation("Magok", Display = "Magok")] IRegenerationSeeds Seeds { get; }
    [RuleRelation("Időlépés", Display = "Időlépés")] IRegenerationStep Step { get; }
    [RuleRelation("Az elmúlt hónap besugárzása", Display = "Környezet")] ICompetitionEnvironment Environment { get; }
}
[RuleEntity("regenerationSource", Abstract = true, Display = "Magadó állomány")]
internal interface IRegenerationSource
{
    [RuleField("Érettség", Display = "Érettség", Min = 0, Max = 1)] double Maturity { get; }
    [RuleField("Magadó átlagéletkora", Display = "Életkor", Unit = "év", Min = 0)] double Age { get; }
    [RuleField("A magadó és a célcsempe középpontjának távolsága", Display = "Távolság", Unit = "m", Min = 0)] double Distance { get; }
    [RuleField("Egészség", Display = "Egészség", Min = 0, Max = 1)] double Health { get; }
    [RuleField("Biomassza", Display = "Biomassza", Min = 0)] double Biomass { get; }
}
[RuleEntity("regenerationTile", Abstract = true, Display = "Célcsempe")]
internal interface IRegenerationTile
{
    [RuleField("Nedvesség", Display = "Nedvesség", Min = 0, Max = 1)] double Moisture { get; }
    [RuleField("Talaj termékenysége", Display = "Termékenység", Min = 0, Max = 1)] double Fertility { get; }
    [RuleField("Relatív terepmagasság", Display = "Tengerszint feletti helyzet", Min = 0, Max = 1)] double Elevation { get; }
    [RuleField("Termőhelyi alkalmasság", Display = "Termőhelyi alkalmasság", Min = 0, Max = 1)] double Suitability { get; }
    [RuleField("Fafaj vízválasza", Display = "Fafaj vízválasza", Min = 0, Max = 1)] double WaterResponse { get; }
    [RuleField("Szomszédos lombterhelés", Display = "Szomszédos lombterhelés", Min = 0, Max = 1)] double Crowding { get; }
    [RuleField("Szomszédok átlagos biomasszája a közös pillanatképből", Display = "Szomszédok átlagos biomasszája", Min = 0)] double NeighborBiomass { get; }
}
[RuleEntity("regenerationSpecies", Abstract = true, Display = "Fafaj")]
internal interface IRegenerationSpecies
{
    [RuleField("Árnyéktűrés", Display = "Árnyéktűrés", Min = 0, Max = 1)] double ShadeTolerance { get; }
    [RuleField("Maximális biomassza", Display = "Maximális biomassza", Min = 0)] double MaximumBiomass { get; }
    [RuleField("A fafaj érési kora", Display = "Érési kor", Unit = "év", Min = 0)] double MatureAge { get; }
}
[RuleEntity("regenerationSeeds", Abstract = true, Display = "Magok")]
internal interface IRegenerationSeeds
{
    [RuleField("Magellátottság", Display = "Magellátottság", Min = 0)] double Supply { get; }
    [RuleField("Determinisztikus sorsolás", Display = "Determinisztikus sorsolás", Min = 0, Max = 1)] double Random { get; }
}
[RuleEntity("regenerationStep", Abstract = true, Display = "Időlépés")]
internal interface IRegenerationStep
{
    [RuleField("Eltelt erdőév", Display = "Eltelt erdőév", Min = 0)] double Years { get; }
}
