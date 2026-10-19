using System;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>Project-owned pair contributions and resource responses. Geometry and ordered reduction belong to the host.</summary>
public static class ForestCompetitionRules
{
    public static readonly string[] PairInputs = { "tree.height", "tree.crownRadius", "tree.diameter", "tree.health", "tree.shadeTolerance",
        "neighbor.height", "neighbor.crownRadius", "neighbor.diameter", "neighbor.health", "neighbor.shadeTolerance", "pair.distance", "pair.distanceSquared" };
    public static readonly string[] LoadInputs = { "competition.shade", "competition.crownLoad", "environment.radiation", "resources.water" };
    public static readonly string[] ResourceInputs = { "resources.light", "resources.water", "resources.space", "species.shadeTolerance", "resources.lightResponse" };
    public static readonly BehaviorHook[] Hooks = {
        new("forest.shading", "Szomszéd árnyékoló hatása", "competitionPair", "terhelés", 0, 1e6, "", "", "Szeletelten, minden különböző fapárra a két szomszédsági gyűrűben", false, PairInputs),
        new("forest.crowding", "Szomszéd térfoglaló hatása", "competitionPair", "terhelés", 0, 1e6, "", "", "Szeletelten, minden különböző fapárra a két szomszédsági gyűrűben", false, PairInputs),
        new("forest.light", "A fához jutó fény", "competition", "0–1", 0, 1, "", "", "A szomszédok gráfos hatásainak sorrendtartó összegzése után", false, LoadInputs),
        new("forest.space", "A fa szabad növekedési tere", "competition", "0–1", 0, 1, "", "", "A szomszédok gráfos hatásainak sorrendtartó összegzése után", false, LoadInputs),
        new("forest.lightResponse", "Fafaj fényválasza", "resource", "0–1", 0, 1, "", "", "Növekedés, stressz és erdődiagnózis", false, ResourceInputs.Take(4).ToArray()),
        new("forest.resourceLimit", "A fa erőforráskorlátja", "resource", "0–1", 0, 1, "", "", "Egészség, stressz és erdődiagnózis", false, ResourceInputs)
    };
}
