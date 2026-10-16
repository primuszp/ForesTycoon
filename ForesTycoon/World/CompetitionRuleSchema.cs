using ForesTycoon.Rules;

namespace ForesTycoon;

[RuleEntity("competitionPair", Abstract = true, Display = "Fapár")]
internal interface ICompetitionPairQuery
{
    [RuleRelation("A vizsgált fa pillanatképe", Display = "Vizsgált fa")] ICompetitionTree Tree { get; }
    [RuleRelation("A másik fa ugyanabból a pillanatképből", Display = "Szomszéd fa")] ICompetitionTree Neighbor { get; }
    [RuleRelation("A két törzs közötti mért távolság", Display = "Fapár")] ICompetitionDistance Pair { get; }
}
[RuleEntity("competitionTree", Abstract = true, Display = "Fa")]
internal interface ICompetitionTree
{
    [RuleField("Pillanatképbeli magasság", Display = "Magasság", Unit = "m", Min = 0)] double Height { get; }
    [RuleField("Pillanatképbeli koronasugár", Display = "Koronasugár", Unit = "m", Min = 0)] double CrownRadius { get; }
    [RuleField("Pillanatképbeli törzsátmérő", Display = "Törzsátmérő", Unit = "m", Min = 0)] double Diameter { get; }
    [RuleField("Pillanatképbeli egészség", Display = "Egészség", Min = 0, Max = 1)] double Health { get; }
    [RuleField("A fafaj árnyéktűrése", Display = "Árnyéktűrés", Min = 0, Max = 1)] double ShadeTolerance { get; }
}
[RuleEntity("competitionDistance", Abstract = true, Display = "Fapár")]
internal interface ICompetitionDistance
{
    [RuleField("A törzsek vízszintes távolsága", Display = "Távolság", Unit = "m", Min = 0)] double Distance { get; }
    [RuleField("A távolság négyzete", Display = "Távolság négyzete", Unit = "m²", Min = 0)] double DistanceSquared { get; }
}
[RuleEntity("competition", Abstract = true, Display = "Összegzett verseny")]
internal interface ICompetitionQuery
{
    [RuleRelation("A fapárgráfok összegzett hatásai", Display = "Verseny")] ICompetitionLoads Competition { get; }
    [RuleRelation("Az aktuális vagy havi átlagos környezet", Display = "Környezet")] ICompetitionEnvironment Environment { get; }
    [RuleRelation("A fa rendelkezésére álló erőforrás", Display = "Erőforrások")] ICompetitionResources Resources { get; }
}
[RuleEntity("competitionLoads", Abstract = true, Display = "Verseny")]
internal interface ICompetitionLoads
{
    [RuleField("Az árnyékolási gráf szomszédonkénti kimenetének összege", Display = "Árnyékterhelés", Min = 0)] double Shade { get; }
    [RuleField("A térfoglalási gráf szomszédonkénti kimenetének összege", Display = "Koronaterhelés", Min = 0)] double CrownLoad { get; }
}
[RuleEntity("competitionEnvironment", Abstract = true, Display = "Környezet")]
internal interface ICompetitionEnvironment
{
    [RuleField("A pillanatnyi vagy havi átlagos besugárzás", Display = "Besugárzás", Min = 0, Max = 1)] double Radiation { get; }
}
[RuleEntity("competitionResources", Abstract = true, Display = "Erőforrások")]
internal interface ICompetitionResources
{
    [RuleField("A fához jutó fény aránya", Display = "Fény", Min = 0, Max = 1)] double Light { get; }
    [RuleField("A vízgráf által adott vízválasz", Display = "Víz", Min = 0, Max = 1)] double Water { get; }
    [RuleField("A térgráf által adott szabad növekedési tér", Display = "Szabad tér", Min = 0, Max = 1)] double Space { get; }
    [RuleField("A faj fényválaszgráfjának eredménye", Display = "Faj szerinti fényválasz", Min = 0, Max = 1)] double LightResponse { get; }
}
[RuleEntity("resource", Abstract = true, Display = "Fa erőforrásválasza")]
internal interface IResourceQuery
{
    [RuleRelation("A fa mért és gráfból kapott erőforrásai", Display = "Erőforrások")] ICompetitionResources Resources { get; }
    [RuleRelation("A vizsgált fafaj", Display = "Fafaj")] IResourceSpecies Species { get; }
}
[RuleEntity("resourceSpecies", Abstract = true, Display = "Fafaj")]
internal interface IResourceSpecies
{
    [RuleField("A faj árnyéktűrési tulajdonsága", Display = "Árnyéktűrés", Min = 0, Max = 1)] double ShadeTolerance { get; }
}
