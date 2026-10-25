using ForesTycoon.Rules;

namespace ForesTycoon;

// Query inputs are discovered by the same schema as world objects. They are immutable values,
// so a response graph cannot recurse through a live tile's derived water response.
[RuleEntity("site", Abstract = true, Display = "Termőhelyi lekérdezés")]
internal interface ISiteResponse
{
    [RuleRelation("A vizsgált csempe", Display = "Csempe")] ISiteTile Tile { get; }
    [RuleRelation("A vizsgált fafaj", Display = "Fafaj")] ISiteSpecies Species { get; }
    [RuleRelation("Az eltelt környezeti idő", Display = "Időlépés")] ISiteStep Step { get; }
}
[RuleEntity("siteTile", Abstract = true, Display = "Csempe")]
internal interface ISiteTile
{
    [RuleField("Térképi nedvesség", Display = "Nedvesség", Min = 0, Max = 1)] double Moisture { get; }
    [RuleField("Relatív terepmagasság", Display = "Tengerszint feletti helyzet", Min = 0, Max = 1)] double Elevation { get; }
    [RuleField("A talaj termékenysége", Display = "Termékenység", Min = 0, Max = 1)] double Fertility { get; }
    [RuleField("A növény számára hozzáférhető víz aránya", Display = "Vízellátottság", Min = 0, Max = 1)] double WaterSupply { get; }
    [RuleField("A gráf eddigi simított pangóvízértéke; a havi válaszban az időátlaga", Display = "Pangóvíz", Min = 0, Max = 1)] double Waterlogging { get; }
    [RuleField("A gyökérzóna pillanatnyi telítettségéből mért víztöbblet", Display = "Pillanatnyi víztöbblet", Min = 0, Max = 1)] double RawWaterlogging { get; }
    [RuleField("A gráf eddigi aszályértéke", Display = "Aszály", Min = 0, Max = 1)] double Drought { get; }
    [RuleField("A csempe magassági öve alulról számolva (0 = legalacsonyabb)", Display = "Magassági öv", Min = 0)] double Zone { get; }
    [RuleField("A csempe biomjának sorszáma a pálya biomlistájában; -1, ha nincs", Display = "Biom", Min = -1)] double Biome { get; }
    [RuleField("A csempe meredeksége", Display = "Lejtő")] ForesTycoon.Map.TerrainSlope Slope { get; }
}
[RuleEntity("siteSpecies", Abstract = true, Display = "Fafaj")]
internal interface ISiteSpecies
{
    [RuleField("A faj számára kedvező relatív magasság", Display = "Kedvelt magasság")] double PreferredElevation { get; }
    [RuleField("A kedvelt magasságtól tűrt eltérés", Display = "Magasságtűrés")] double ElevationTolerance { get; }
    [RuleField("A vízellátottság hatványkitevője", Display = "Szárazságérzékenység")] double DrySensitivity { get; }
    [RuleField("Melyik fafaj", Display = "Fafaj")] ForestSpecies Kind { get; }
}
[RuleEntity("siteStep", Abstract = true, Display = "Időlépés")]
internal interface ISiteStep
{
    [RuleField("A környezeti lépés hossza másodpercben", Display = "Eltelt idő", Unit = "s", Min = 0)] double Seconds { get; }
}
