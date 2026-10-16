using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>Meaning of a host query result. Field reads and links between authored graphs are derived from their actual connected nodes.</summary>
public sealed record ResponseRuleDescription(string Result, string Consumers, double SuggestedOffValue, string FixedMeaning, string[] OutputKeys)
{
    public static ResponseRuleDescription For(string hook) => hook switch {
        "forest.site" => D("A fafaj termőhelyi alkalmassága", "Erdő létrehozása, ültetés, újulat és fanövekedés", 1, "Minden vizsgált fafaj és termőhely ezt az alkalmasságot kapja.", "regeneration:tile.suitability", "tree:fitness"),
        "water.stress" => D("A fafaj vízellátásra adott válasza", "Fanövekedés, faegészség, stressz és újulat", 1, "Minden vizsgált fafaj ezt a vízválaszt kapja, a tényleges vízhelyzettől függetlenül.", "resource:resources.water", "regeneration:tile.waterResponse", "tree:water"),
        "water.drought" => D("A csempe követett aszályállapota", "Aszályt olvasó szabályok és a csempe állapotjelzése", 0, "Minden környezeti frissítéskor erre az értékre áll az aszály.", "tile:drought"),
        "water.waterlogging" => D("A csempe követett pangóvízállapota", "Fafajok vízválasza és a talaj járhatósága", 0, "Minden környezeti frissítéskor erre az értékre áll a pangóvíz.", "site:tile.waterlogging"),
        "forest.shading" => D("Egy szomszéd árnyékoló hozzájárulása", "A fához jutó fény", 0, "Minden vizsgált fapár ezt az árnyékterhelést adja; 0 esetén nincs szomszédos árnyékolás.", "competition:competition.shade"),
        "forest.crowding" => D("Egy szomszéd térfoglaló hozzájárulása", "A fa szabad növekedési tere", 0, "Minden vizsgált fapár ezt a térterhelést adja; 0 esetén nincs szomszédos térfoglalás.", "competition:competition.crownLoad"),
        "forest.light" => D("A fához jutó fény aránya", "Fafaj fényválasza, növekedés, egészség és stressz", 1, "Minden fa ezt a fényarányt kapja, a szomszédoktól és az időjárástól függetlenül.", "resource:resources.light", "tree:light"),
        "forest.space" => D("A fa szabad növekedési tere", "Fanövekedés és erőforráskorlát", 1, "Minden fa ezt a szabad térarányt kapja.", "resource:resources.space", "tree:space"),
        "forest.lightResponse" => D("A fafaj fényre adott válasza", "Fanövekedés és erőforráskorlát", 1, "Minden fafaj ezt a fényválaszt kapja; a faj árnyéktűrése nem módosítja.", "resource:resources.lightResponse", "tree:lightResponse"),
        "forest.resourceLimit" => D("A fa erőforráskorlátja", "Faegészség, stressz és erdődiagnózis", 1, "Minden fa ezt az erőforrásértéket kapja.", "tree:resourceLimit"),
        "forest.seedCrowding" => D("Az újulatot körülvevő lombterhelés", "A megtelepedő fafaj kiválasztása", 0, "Minden újulati célcsempe ezt a lombterhelést kapja.", "regeneration:tile.crowding"),
        "forest.seedSupply" => D("Egy szomszédos állomány magmennyisége", "Fafajkiválasztás és csírázás", 0, "Minden szomszéd ezt a magmennyiséget adja; 0 esetén nincs természetes újulat.", "regeneration:seeds.supply"),
        "forest.seedSelection" => D("Egy magadó jelölt kiválasztási pontszáma", "A legnagyobb pozitív pontszámú fafaj megtelepedése", 0, "Minden magadó ezt a pontszámot kapja; 0 esetén egyik sem választható ki."),
        "forest.germination" => D("Megtelepedhet-e az újulat", "Természetes csemeték létrehozása", 0, "0: nincs megtelepedés. Pozitív érték: minden megfelelő magadóval rendelkező üres célcsempén engedélyezett."),
        "forest.seedlingCount" => D("A létrejövő csemeték száma", "Újulat létrehozása", 0, "Ennyi csemete jön létre megtelepedésenként, egészre kerekítve. 0 esetén nincs újulat."),
        "forest.seedlingAge" => D("A csemeték kezdő átlagéletkora", "Fák kezdő méretgörbéi", 0, "Minden újulati csemete ebből az átlagéletkorból indul; a seedelt egyedi koreltérés megmarad.", "initialTree:tree.age"),
        "forest.seedlingHealth" => D("A csemeték kezdő egészsége", "Újulat létrehozása és a fák későbbi életfolyamatai", 1, "Minden csemete ezt az egészséget kapja. 0 esetén nem jön létre újulat.", "tree:health"),
        "forest.initialHeight" => D("Az új fa kezdő magassága", "Kezdő átmérő, koronasugár és az új fa állapota", 1, "Minden ezután létrejövő fa ennyi méter magas. A meglévő fák mérete megmarad.", "initialTree:initial.height"),
        "forest.initialDiameter" => D("Az új fa kezdő törzsátmérője", "Kezdő koronasugár és az új fa állapota", .01, "Minden ezután létrejövő fa törzsátmérője ennyi méter. A meglévő fák mérete megmarad.", "initialTree:initial.diameter"),
        "forest.initialCrown" => D("Az új fa kezdő koronasugara", "Az új fa geometriája, versengése és vízháztartási visszacsatolása", .15, "Minden ezután létrejövő fa koronasugara ennyi méter. A meglévő fák mérete megmarad."),
        _ => throw new ArgumentException("Missing response rule meaning: " + hook, nameof(hook))
    };
    private static ResponseRuleDescription D(string result, string consumers, double off, string fixedMeaning, params string[] outputs) => new(result, consumers, off, fixedMeaning, outputs);
    public static IReadOnlyList<BehaviorNode> ConnectedNodes(BehaviorGraph graph)
    {
        var byId = graph.Nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First()); var seen = new HashSet<string>(); var nodes = new List<BehaviorNode>();
        void Visit(string id) {
            if (id == null || !seen.Add(id) || !byId.TryGetValue(id, out var node)) return;
            int count = BehaviorModel.Inputs(node.Operation);
            if (count > 0) Visit(node.A); if (count > 1) Visit(node.B); if (count > 2) Visit(node.C);
            nodes.Add(node);
        }
        Visit(graph.Output); return nodes;
    }
    public static string[] ConnectedInputs(BehaviorGraph graph) => ConnectedNodes(graph).Where(n => n.Operation == BehaviorOperation.Input).Select(n => n.Key).Distinct().ToArray();
    public string[] GraphConsumers(IEnumerable<BehaviorGraph> graphs) => graphs.Where(g => !g.Disabled && ConnectedInputs(g).Any(input => OutputKeys.Contains(g.Kind + ":" + input))).Select(g => g.Name).Distinct().ToArray();
}
