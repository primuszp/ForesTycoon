using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>Meaning of a host query result. Field reads and links between authored graphs are derived from their actual connected nodes.</summary>
public sealed record ResponseRuleDescription(string Result, string Consumers, double SuggestedOffValue, string FixedMeaning, string[] OutputKeys)
{
    public static ResponseRuleDescription For(string hook) => hook switch {
        "soil.bearing" => D("Talaj teherbírása", "Burkolatlan utak járhatósága és áthaladási kopása", 1, "Minden talaj ezt a teherbírást adja. A talajtípusban megadott érték és a drénezés most nem számít.", "environment:soil.bearingCapacity"),
        "weather.initialType" => D("Új világ első időjárása", "Időjárási idővonal indítása", 0, "Az új világok ezzel az időjárással indulnak. A futó világot nem módosítja."),
        "weather.initialDuration" => D("Első esemény alaptempójú időtartama", "Időjárási idővonal indítása", 150, "Az új világ első eseménye ennyi alaptempójú másodpercig tart."),
        "weather.initialPeak" => D("Első csapadékesemény erőssége", "Időjárási idővonal indítása", 0, "Csapadékos induló időjárásnál ez a csúcsintenzitás. A futó világot nem módosítja."),
        "weather.eventType" => D("Következő időjárás típusa", "Eseményhossz, csapadékintenzitás és légköri válaszok", 0, "Minden következő esemény a megadott típus lesz: 0 napos, 1 felhős, 2 eső, 3 hó, 4 vihar. A folyamatban lévő eseményt nem cseréli le.", "environment:event.type"),
        "weather.eventDuration" => D("Következő esemény alaptempójú időtartama", "Seedelt időjárási idővonal", 60, "Az új események ennyi alaptempójú másodpercig tartanak, legfeljebb az évszakhatárig."),
        "weather.eventPeak" => D("Új csapadékesemény csúcsintenzitása", "Eső és hó integrálása, szélválasz", 0, "Az új csapadékesemények ezt az intenzitást kapják; 0 esetén nem adnak vizet.", "environment:event.peak"),
        "weather.forcedDuration" => D("Kért időjárás alapideje", "Időjárásváltó parancs", 180, "Külön megadott idő nélkül ennyi alaptempójú másodperc lesz az esemény."),
        "weather.forcedPeak" => D("Kért csapadék alapintenzitása", "Időjárásváltó parancs", 0, "Külön megadott intenzitás nélkül ezt kapja a kért csapadékesemény."),
        "weather.ramp" => D("Csapadék rámpájának hossza", "Pontos, szakaszonkénti csapadékintegrálás", 1, "Ennyi másodpercig fut fel és le az intenzitás, legfeljebb az esemény feléig."),
        "weather.temperature" => D("Világ légköri hőmérséklete", "Helyi klíma, hóátrendezés és párolgás", 10, "A levegő ezt az állandó hőmérsékletet kapja.", "environment:weather.temperature"),
        "weather.humidity" => D("Világ légköri páratartalma", "Helyi klíma és párolgás", .55, "A levegő páratartalma minden időjárásban ennyi.", "environment:weather.humidity"),
        "weather.wind" => D("Világ szélsebessége", "Hóátrendezés és párolgás", 0, "A szélsebesség minden időjárásban ennyi.", "environment:weather.wind"),
        "weather.cloud" => D("Világ felhőzete", "Sugárzási válasz és időjárás megjelenítése", 0, "A felhőzet átmenet nélkül a fix érték lesz.", "environment:weather.cloud"),
        "weather.radiation" => D("Világ sugárzása", "Fák fényellátása, párolgás és havi fényátlag", 1, "A sugárzás nem függ a felhőzettől.", "environment:weather.radiation", "competition:environment.radiation", "regeneration:environment.radiation"),
        "weather.evaporation" => D("Helyi párologtató képesség", "Lomb-, felszín- és talajpárolgás, fák vízfelvételi áramlása", 0, "Minden csempe ezt a párologtató képességet kapja. 0 esetén az erre kötött áramlások nem párologtatnak."),
        "climate.temperature" => D("Csempe helyi hőmérséklete", "Hóolvadás és helyi párologtató képesség", 10, "Minden csempe ezt a hőmérsékletet kapja, magasságtól és régiótól függetlenül.", "environment:weather.temperature"),
        "climate.humidity" => D("Csempe helyi páratartalma", "Helyi párologtató képesség", .55, "Minden csempe páratartalma ennyi.", "environment:weather.humidity"),
        "climate.rain" => D("Csempe csapadékszorzója", "Eső- és hóforrások, csapadék vízmérlege", 1, "Minden csempe azonos arányban kap csapadékot. 0 esetén nem kap csapadékot."),
        "forest.crownArea" => D("Egy fa hatásos koronaterülete", "Vízfogás, lombfelület és csempeborítás", 0, "Minden fa ennyi koronaterülettel járul hozzá a vízháztartáshoz.", "environment:canopy.crownArea"),
        "forest.interception" => D("Egy fa vízfogási hozzájárulása", "Lombkorona vízkapacitásának csempeösszege", 0, "A fák vízfogási hozzájárulása ennyi.", "environment:canopy.interception"),
        "forest.leafArea" => D("Egy fa lombfelületi hozzájárulása", "Csempe lombfelületi indexe", 0, "A fák lombfelületi hozzájárulása ennyi.", "environment:canopy.leafArea"),
        "forest.cover" => D("Csempe lombborítása", "Csapadék megosztása, csupasz talaj párolgása, hó szélvédettsége", 0, "A csempék lombborítása a fix érték lesz.", "environment:canopy.cover"),
        "forest.canopyCapacity" => D("Csempe lombkoronájának vízkapacitása", "Koronák telítettsége és csapadék megfogása", 0, "A koronák ennyi mm vizet tarthatnak. A már tárolt vizet a váltás nem tünteti el."),
        "forest.leafIndex" => D("Csempe lombfelületi indexe", "Fák vízfelvételi áramlása", 0, "A lombfelületi index a fix érték lesz."),
        "snow.direction" => D("Egy szomszéd szélirány szerinti pontszáma", "Hószállítás célpontjának kiválasztása és szél felőli domborzat", 0, "A legnagyobb pozitív pontszámú szomszéd a cél. 0 esetén nincs cél, a hó helyben marad."),
        "snow.shelter" => D("Csempe hóvédettsége", "Visszatartott hó és szállított hányad", 0, "Minden csempe ezt a védettséget kapja.", "environment:snow.shelter"),
        "snow.retention" => D("Csempén visszatartott hó", "Konzervatív szomszédos hószállítás", 0, "Ennyi mm hó marad helyben; csak az e fölötti rész szállítható."),
        "snow.release" => D("Szállítható hó elmozduló hányada", "Konzervatív szomszédos hószállítás", 0, "0 esetén nincs hóátrendezés; a hó nem vész el. A kiválasztott szomszéd csak a következő lépésben adhatja tovább."),
        "soil.wetness" => D("Csempe járhatósági átnedvesedése", "Járhatósági válasz és áthaladási kopás", 0, "Minden csempe ezt az átnedvesedést jelzi.", "environment:tile.wetness"),
        "trail.trafficability" => D("Burkolatlan csempe járhatósága", "Járművek gördülési ellenállása és útvonaltervezés", 1, "Minden burkolatlan csempe ezt a járhatóságot kapja; 1 a jól járható állapot."),
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
