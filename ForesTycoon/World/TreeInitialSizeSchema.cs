using ForesTycoon.Rules;

namespace ForesTycoon;

[RuleEntity("initialTree", Abstract = true, Display = "Fa kezdő mérete")]
internal interface IInitialTreeQuery
{
    [RuleRelation("A most létrejövő fa mért bemenetei", Display = "Új fa")] IInitialTreeParameters Tree { get; }
    [RuleRelation("A létrehozott fafaj méretadatai", Display = "Fafaj")] IInitialTreeSpecies Species { get; }
    [RuleRelation("A korábban kiértékelt kezdőméret-gráfok eredménye", Display = "Kezdő méretek")] IInitialTreeDimensions Initial { get; }
}
[RuleEntity("initialTreeParameters", Abstract = true, Display = "Új fa")]
internal interface IInitialTreeParameters
{
    [RuleField("Az egyed kezdő életkora, az esetleges seedelt koreltéréssel együtt", Display = "Életkor", Unit = "év", Min = 0)] double Age { get; }
    [RuleField("Az egyed saját seedjéből képzett szám, 0 és 1 között", Display = "Seedelt változatosság", Min = 0, Max = 1)] double Random { get; }
    [RuleField("Külső méretlépték; a játékban 1, a mintanézetek saját léptéket adhatnak", Display = "Méretlépték", Min = 0)] double Scale { get; }
    [RuleField("1: játékos által ültetett fa; 0: generált erdő, festés vagy természetes újulat", Display = "Ültetett fa", Min = 0, Max = 1)] double Planted { get; }
}
[RuleEntity("initialTreeSpecies", Abstract = true, Display = "Fafaj")]
internal interface IInitialTreeSpecies
{
    [RuleField("A fafaj érési kora", Display = "Érési kor", Unit = "év", Min = 0)] double MatureAge { get; }
    [RuleField("A fafaj érett magassága", Display = "Érett magasság", Unit = "m", Min = 0)] double MatureHeight { get; }
    [RuleField("A fafaj érett törzsátmérője", Display = "Érett törzsátmérő", Unit = "m", Min = 0)] double MatureDiameter { get; }
    [RuleField("A fafaj koronaaránya", Display = "Koronaarány", Min = 0)] double CrownRatio { get; }
}
[RuleEntity("initialTreeDimensions", Abstract = true, Display = "Kezdő méretek")]
internal interface IInitialTreeDimensions
{
    [RuleField("A kezdő magasság gráfjának tényleges kimenete", Display = "Kezdő magasság", Unit = "m", Min = 0)] double Height { get; }
    [RuleField("A kezdő átmérő gráfjának tényleges kimenete", Display = "Kezdő törzsátmérő", Unit = "m", Min = 0)] double Diameter { get; }
}
