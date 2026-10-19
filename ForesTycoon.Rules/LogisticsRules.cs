using System;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>Editable logistics decisions; stock conservation and path topology are host invariants.</summary>
public static class LogisticsRules
{
    public static readonly string[] InputKeys = ["step.seconds", "machine.kind", "machine.state", "machine.cargo", "machine.capacity", "machine.wear", "machine.pace", "machine.homeRequested", "source.stock", "source.fed", "source.availableTimber", "source.reachable", "spec.fellingRate", "spec.unloadRate", "spec.speed", "spec.loadedSpeed", "spec.fuelDriving", "spec.fuelLoaded", "spec.fuelCrane", "spec.fuelFelling", "spec.emptyMass", "spec.timberDensity", "spec.loadRate", "log.volume", "log.lastVolume", "transfer.unloading", "transfer.requested", "transfer.loaded", "route.damage", "route.surface", "route.from", "route.to", "mill.stock", "mill.processed", "site.forest", "site.harvest", "site.roadAccess", "depot.count", "order.hasTimber", "order.destinationIsMill", "workflow.stage", "workflow.arrived", "workflow.atSource", "decision.continue", "decision.deliver", "decision.fetch", "decision.return", "decision.allow", "machine.control", "machine.working", "machine.broken", "machine.hasVehicle", "machine.transit", "machine.transportState", "machine.speed", "transfer.held"];
    public static string Kind(string agent) => "logistics" + char.ToUpperInvariant(agent[0]) + agent[1..];
    public static bool IsKind(string kind) => kind is "logisticsProcessor" or "logisticsForwarder" or "logisticsTruck" or "logisticsMill" or "logisticsDepot";
    private static BehaviorHook H(string id, string name, string kind, string unit, double min, double max, string state, string amount, string schedule) =>
        new(id, name, kind, unit, min, max, state, amount, schedule, true, InputKeys.Where(key => Available(id, kind, key)).ToArray());
    private static bool Available(string id, string kind, string key)
    {
        if (id == "supply.active") return key.StartsWith("machine.") || key is "source.stock" or "source.reachable" or "transfer.held";
        if (key == "transfer.held") return false;
        if (id is "control.allow" or "control.home") return key.StartsWith("machine.");
        if (id == "control.upkeep") return key.StartsWith("machine.") || key == "decision.allow";
        if (key == "decision.allow") return false;
        if (id == "machine.workflow") return key is "machine.state" or "machine.cargo" or "machine.capacity" or "machine.homeRequested"
            or "source.stock" or "source.reachable" or "workflow.stage" or "decision.continue" or "decision.deliver" or "decision.fetch" or "decision.return";
        if (id == "truck.workflow") return key is "machine.state" or "machine.cargo" or "machine.homeRequested" or "workflow.arrived" or "workflow.atSource" or "decision.return";
        if (key.StartsWith("workflow.") || key.StartsWith("decision.")) return false;
        if (id == "orders.allow") return key is "machine.kind" or "machine.cargo" or "machine.capacity" or "machine.wear" or "machine.homeRequested"
            || key == "source.stock" && kind != "logisticsProcessor"
            || key == "order.hasTimber" && kind == "logisticsProcessor"
            || key == "order.destinationIsMill" && kind != "logisticsProcessor";
        if (key.StartsWith("order.")) return false;
        if (kind == "logisticsDepot") return key.StartsWith("site.") || key == "depot.count";
        if (key.StartsWith("site.") || key.StartsWith("depot.")) return false;
        if (id == "transfer.duration") return key is "step.seconds" or "machine.cargo" or "machine.capacity";
        if (id == "truck.return") return key is "step.seconds" or "machine.cargo" or "machine.capacity" or "machine.kind" or "machine.wear" or "machine.homeRequested" or "source.stock" or "source.fed";
        if (kind == "logisticsMill") return key.StartsWith("step.") || key.StartsWith("mill.");
        if (id.StartsWith("route.")) return key.StartsWith("route.") || key is "machine.cargo" or "machine.capacity";
        if (key.StartsWith("mill.") || key.StartsWith("route.")) return false;
        if (kind == "logisticsTruck") return key is "step.seconds" or "machine.cargo" or "machine.capacity" or "machine.kind" or "machine.wear" or "machine.homeRequested" or "machine.control"
            or "source.stock" or "transfer.requested" or "transfer.loaded" or "transfer.unloading" || id == "truck.return" && key == "source.fed";
        if (key is "transfer.requested" or "transfer.loaded") return false;
        if (key == "source.stock" && kind == "logisticsProcessor") return false;
        if (key == "source.fed") return id is "machine.fetch" or "machine.return";
        if (key == "source.reachable") return id == "machine.deliver" && kind == "logisticsProcessor";
        if (key == "source.availableTimber") return id is "machine.felling" or "machine.continueFelling";
        return true;
    }
    public static readonly BehaviorHook[] Hooks = {
        H("supply.active", "Processzor aktív utánpótlása", "logisticsProcessor", "0–1", 0, 1, "", "", "A szállítási függőségi háló bejárásakor"),
        H("supply.active", "Forwarder aktív utánpótlása", "logisticsForwarder", "0–1", 0, 1, "", "", "A szállítási függőségi háló bejárásakor"),
        H("supply.active", "Teherautó aktív utánpótlása", "logisticsTruck", "0–1", 0, 1, "", "", "A szállítási függőségi háló bejárásakor"),
        H("control.allow", "Processzor vezérlési engedélye", "logisticsProcessor", "0–1", 0, 1, "", "", "Minden al-lépésben, a vezérlő és a gép mezőiből"),
        H("control.allow", "Forwarder vezérlési engedélye", "logisticsForwarder", "0–1", 0, 1, "", "", "Minden al-lépésben, a vezérlő és a gép mezőiből"),
        H("control.allow", "Teherautó vezérlési engedélye", "logisticsTruck", "0–1", 0, 1, "", "", "Mozgás és rakodás előtt"),
        H("control.home", "Processzor hazahívási kérése", "logisticsProcessor", "0–1", 0, 1, "", "", "A vezérlő és az aktuális állapot alapján"),
        H("control.home", "Forwarder hazahívási kérése", "logisticsForwarder", "0–1", 0, 1, "", "", "A vezérlő és az aktuális állapot alapján"),
        H("control.home", "Teherautó hazahívási kérése", "logisticsTruck", "0–1", 0, 1, "", "", "A vezérlő és az aktuális állapot alapján"),
        H("control.upkeep", "Processzor fenntartási módja", "logisticsProcessor", "mód", 0, 2, "", "", "0: nincs fenntartás, 1: szerviz, 2: üzemi kopás és javítás"),
        H("control.upkeep", "Forwarder fenntartási módja", "logisticsForwarder", "mód", 0, 2, "", "", "0: nincs fenntartás, 1: szerviz, 2: üzemi kopás és javítás"),
        H("control.upkeep", "Teherautó fenntartási módja", "logisticsTruck", "mód", 0, 2, "", "", "0: nincs fenntartás, 1: szerviz, 2: üzemi kopás és javítás"),
        H("machine.workflow", "Processzor munkafázis választása", "logisticsProcessor", "művelet", 0, 5, "", "", "0: folytatás, 1: várakozás, 2: hazatérés, 3: lerakó, 4: munkahely, 5: munkakeresés"),
        H("machine.workflow", "Forwarder munkafázis választása", "logisticsForwarder", "művelet", 0, 5, "", "", "0: folytatás, 1: várakozás, 2: hazatérés, 3: lerakó, 4: forrás, 5: munkakeresés"),
        H("truck.workflow", "Teherautó munkafázis választása", "logisticsTruck", "művelet", 0, 3, "", "", "0: folytatás, 1: fuvar indítása, 2: hazatérés, 3: parkolás"),
        H("orders.allow", "Processzor munkára küldésének engedélyezése", "logisticsProcessor", "0–1", 0, 1, "", "", "Parancs ellenőrzésekor, útkeresés után, módosítás előtt; 1 engedélyez, 0 elutasít"),
        H("orders.allow", "Forwarder munkára küldésének engedélyezése", "logisticsForwarder", "0–1", 0, 1, "", "", "Parancs ellenőrzésekor, útkeresés után, módosítás előtt; 1 engedélyez, 0 elutasít"),
        H("orders.allow", "Teherautó munkára küldésének engedélyezése", "logisticsTruck", "0–1", 0, 1, "", "", "Parancs ellenőrzésekor, útkeresés után, módosítás előtt; 1 engedélyez, 0 elutasít"),
        H("depot.allow", "Telephely építésének engedélyezése", "logisticsDepot", "0–1", 0, 1, "", "", "Építési parancskor, a hely vizsgálata után; 1 engedélyez, 0 elutasít"),
        H("depot.processors", "Telephely induló processzorai", "logisticsDepot", "db", 0, 1024, "", "", "Építéskor egyszer; egész darabszám, gépenként szeletelt létrehozás"),
        H("depot.forwarders", "Telephely induló forwarderei", "logisticsDepot", "db", 0, 1024, "", "", "Építéskor egyszer; egész darabszám, gépenként szeletelt létrehozás"),
        H("depot.trucks", "Telephely induló teherautói", "logisticsDepot", "db", 0, 1024, "", "", "Építéskor egyszer; egész darabszám, gépenként szeletelt létrehozás"),
        H("route.cost", "Processzor útvonalköltsége", "logisticsProcessor", "költség/szakasz", 0.001, 1000000.0, "", "", "Útkeresés közben, szakaszonként; csak a járható hálózaton"),
        H("route.allow", "Processzor útvonalszakasz engedélyezése", "logisticsProcessor", "0–1", 0, 1, "", "", "Útkeresés közben; 0 tilt, 1 engedélyez egy ténylegesen járható szakaszt"),
        H("machine.pace", "Processzor munkatempója", "logisticsProcessor", "munka-s", 0, 10, "", "", "Szeletelt gépi al-lépésben, fenntartás után"),
        H("machine.speed", "Processzor haladási sebessége", "logisticsProcessor", "csempe/munka-s", 0, 100, "", "", "Vezetés közben, minden gépi al-lépésben"),
        H("machine.fuel", "Processzor munkafogyasztása", "logisticsProcessor", "l", 0, 1000000.0, "", "", "Vezetés, vágás és daruzás munkalépésében; utána közös kopási fogyasztáskorrekció"),
        H("machine.mass", "Processzor áthaladási tömege", "logisticsProcessor", "kg", 1, 1000000000.0, "", "", "Csempehatár átlépésekor, a közös áthaladási szabály előtt"),
        H("route.cost", "Forwarder útvonalköltsége", "logisticsForwarder", "költség/szakasz", 0.001, 1000000.0, "", "", "Útkeresés közben, szakaszonként; csak a járható hálózaton"),
        H("route.allow", "Forwarder útvonalszakasz engedélyezése", "logisticsForwarder", "0–1", 0, 1, "", "", "Útkeresés közben; 0 tilt, 1 engedélyez egy ténylegesen járható szakaszt"),
        H("machine.pace", "Forwarder munkatempója", "logisticsForwarder", "munka-s", 0, 10, "", "", "Szeletelt gépi al-lépésben, fenntartás után"),
        H("machine.speed", "Forwarder haladási sebessége", "logisticsForwarder", "csempe/munka-s", 0, 100, "", "", "Vezetés közben, minden gépi al-lépésben"),
        H("machine.fuel", "Forwarder munkafogyasztása", "logisticsForwarder", "l", 0, 1000000.0, "", "", "Vezetés, vágás és daruzás munkalépésében; utána közös kopási fogyasztáskorrekció"),
        H("machine.mass", "Forwarder áthaladási tömege", "logisticsForwarder", "kg", 1, 1000000000.0, "", "", "Csempehatár átlépésekor, a közös áthaladási szabály előtt"),
        H("loading.amount", "Forwarder felrakott mennyisége", "logisticsForwarder", "m³", 0, 1000000.0, "", "", "Forwarder: megfogáskor; teherautó: rakodási al-lépésben; készlet- és kapacitáskorláttal"),
        H("unloading.amount", "Forwarder lerakott mennyisége", "logisticsForwarder", "m³", 0, 1000000.0, "", "", "Forwarder: megfogáskor; teherautó: lerakási al-lépésben; legfeljebb a tényleges rakomány"),
        H("loading.depart", "Forwarder indulása a rakodásból", "logisticsForwarder", "0–1", 0, 1, "", "", "Forwarder: lezárt rönkciklus; teherautó: felrakás után; 1 indul, 0 folytatja"),
        H("route.cost", "Teherautó útvonalköltsége", "logisticsTruck", "költség/szakasz", 0.001, 1000000.0, "", "", "Útkeresés közben, szakaszonként; csak a járható hálózaton"),
        H("route.allow", "Teherautó útvonalszakasz engedélyezése", "logisticsTruck", "0–1", 0, 1, "", "", "Útkeresés közben; 0 tilt, 1 engedélyez egy ténylegesen járható szakaszt"),
        H("loading.amount", "Teherautó felrakott mennyisége", "logisticsTruck", "m³", 0, 1000000.0, "", "", "Forwarder: megfogáskor; teherautó: rakodási al-lépésben; készlet- és kapacitáskorláttal"),
        H("unloading.amount", "Teherautó lerakott mennyisége", "logisticsTruck", "m³", 0, 1000000.0, "", "", "Forwarder: megfogáskor; teherautó: lerakási al-lépésben; legfeljebb a tényleges rakomány"),
        H("loading.depart", "Teherautó indulása a rakodásból", "logisticsTruck", "0–1", 0, 1, "", "", "Forwarder: lezárt rönkciklus; teherautó: felrakás után; 1 indul, 0 folytatja"),
        H("loading.cycleSeconds", "Forwarder rönkciklus ideje", "logisticsForwarder", "munka-s", 0.1, 3600, "", "", "Ciklus kezdetén rögzítve és mentve"),
        H("loading.gripPhase", "Forwarder megfogási pillanat", "logisticsForwarder", "ciklusarány", 0.01, 0.98, "", "", "Ciklus kezdetén rögzítve és mentve"),
        H("loading.releasePhase", "Forwarder elengedési pillanat", "logisticsForwarder", "ciklusarány", 0.02, 0.99, "", "", "Ciklus kezdetén; legalább 0,01-dal a megfogás után"),
        H("unloading.amount", "Processzor lerakott mennyisége", "logisticsProcessor", "m³", 0, 1000000.0, "", "", "Lerakási al-lépésben; készletkorláttal"),
        H("machine.felling", "Processzor kitermelt mennyisége", "logisticsProcessor", "m³", 0, 1000000.0, "", "", "Vágási al-lépésben, a tényleges kitermelés előtt"),
        H("machine.continueFelling", "Processzor folytassa a vágást", "logisticsProcessor", "0–1", 0, 1, "", "", "Vágási al-lépés után; hazahívás mindig biztonságosan lezárja"),
        H("machine.deliver", "Processzor induljon lerakni", "logisticsProcessor", "0–1", 0, 1, "", "", "Következő elérhető munka keresése után; csak rakománnyal"),
        H("machine.fetch", "Forwarder induljon a forrásért", "logisticsForwarder", "0–1", 0, 1, "", "", "Üres gép munkaválasztásakor; hazahívás megelőzi"),
        H("machine.deliver", "Forwarder induljon lerakni", "logisticsForwarder", "0–1", 0, 1, "", "", "Lezárt rönkciklus után; csak rakománnyal"),
        H("machine.return", "Forwarder térjen haza", "logisticsForwarder", "0–1", 0, 1, "", "", "Ha nincs szállítandó rakomány és nem indul a forráshoz"),
        H("truck.return", "Teherautó fejezze be a megbízást", "logisticsTruck", "0–1", 0, 1, "", "", "Üres teherautó a forrásnál; a hazahívás mindig elsőbbséget kap"),
        H("mill.process", "Malom feldolgozása", "logisticsMill", "m³", 0, 1000000.0, "", "", "Logisztikai lépésben, malmonként szeletelve"),
        H("transfer.duration", "Teherautó bemutató rakodási ideje", "logisticsTruck", "s", .001, 3600, "", "", "Központi fakészletes bemutató jármű rakodási al-lépésében"),
    };
    [ThreadStatic] private static CompiledBehaviorModel network;
    [ThreadStatic] private static double[] networkInputs;
    public static float NetworkCost(double surface, double damage)
    {
        var hook = Hooks.Single(h => h.Id == "route.cost" && h.Kind == "logisticsTruck");
        network ??= new(new() { Graphs = new() { DefaultGraph(hook) } }, new[] { hook });
        networkInputs ??= new double[hook.Inputs.Length];
        networkInputs[Array.IndexOf(hook.Inputs, "route.surface")] = surface;
        networkInputs[Array.IndexOf(hook.Inputs, "route.damage")] = damage;
        return (float)network.Evaluate(hook.Id, hook.Kind, 0, new(), networkInputs);
    }
    public static bool AllowedOperation(BehaviorOperation op) => EcologyResponseProgram.AllowedOperation(op) || op == BehaviorOperation.NamedValue;
    public static ResponseRuleDescription Description(string id)
    {
        string result = id switch {
            "supply.active" => "A kapcsolt szállító várható utánpótlása",
            "control.allow" => "Mozgás és munkavégzés engedélye", "control.home" => "Hazahívás kérése", "control.upkeep" => "Fenntartási művelet kiválasztása",
            "machine.workflow" or "truck.workflow" => "A következő végrehajtandó motorművelet",
            "orders.allow" => "Az új munkamegbízás engedélye",
            "depot.allow" => "Az új telephely építési engedélye",
            "depot.processors" or "depot.forwarders" or "depot.trucks" => "Az új telephelyre létrehozott gépek száma",
            "machine.pace" => "A gép munkalépésének hossza", "machine.speed" => "Haladási sebesség", "machine.fuel" => "Munkalépés alapfogyasztása",
            "machine.mass" => "A gép és rakomány áthaladási tömege", "machine.felling" => "Kitermelésre kért térfogat", "machine.continueFelling" => "A helyi vágás folytatása",
            "machine.fetch" => "Elindulás a forrásért", "machine.deliver" => "Elindulás a célhoz", "machine.return" or "truck.return" => "A megbízás befejezése és hazatérés",
            "route.allow" => "Egy tényleges útszakasz engedélyezése", "route.cost" => "Útszakasz költsége az útkeresőnek",
            "loading.amount" => "Felrakásra kért térfogat", "unloading.amount" => "Lerakásra kért térfogat", "loading.depart" => "Rakodás befejezése és indulás",
            "loading.cycleSeconds" or "transfer.duration" => "Rakodási ciklus időtartama", "loading.gripPhase" => "Megfogás pontja a ciklusban", "loading.releasePhase" => "Elengedés pontja a ciklusban",
            _ => "A malom feldolgozásra kért térfogata"
        };
        double off = id is "machine.mass" or "route.cost" ? 1 : id is "loading.cycleSeconds" or "transfer.duration" ? 8 : id == "loading.gripPhase" ? .32 : id == "loading.releasePhase" ? .77 : 0;
        string meaning = id switch {
            "supply.active" => "1: ettől a szállítótól még várható utánpótlás; 0: a gráf nem tekinti aktív forrásnak. A készletküszöb és a vezérlő figyelembevétele szabadon szerkeszthető.",
            "control.allow" => "0: várakozik; 1: végrehajthatja a munkát. A vezérlő műveleteinek jelentését ez a gráf kapcsolja a gép állapotához.",
            "control.home" => "1: hazahívást kér; 0: nem kér. A rakomány megőrzése a végrehajtó feladata.",
            "control.upkeep" => "0: nincs fenntartás, 1: szerviz, 2: üzemi kopás és helyszíni javítás. A feltételeket a projekt adja.",
            "machine.workflow" => "0: jelenlegi művelet folytatása; 1: várakozás; 2: hazatérés; 3: indulás lerakni; 4: indulás dolgozni/felrakodni; 5: munkakeresés. Az elsőbbséget és feltételeket ez a gráf adja. Rakományt nem dobhat el.",
            "truck.workflow" => "0: folytatás; 1: fuvar indítása; 2: hazatérés; 3: parkolás. Az elsőbbséget és feltételeket ez a gráf adja. Rakott vagy mozgó járművet nem tüntethet el.",
            "orders.allow" => "0: elutasítja az új megbízást, a meglévő munka és rakomány megmarad. 1: engedélyezi, ha az út és a cél ténylegesen használható. A hazahívást nem tiltja.",
            "depot.allow" => "0: új telephely nem építhető; a már létezők megmaradnak. Az 1 sem írja felül a száraz, sík, szabad épülethely követelményét.",
            "depot.processors" or "depot.forwarders" or "depot.trucks" => "0: ebből a gépfajtából nem kap új gépet. A pozitív eredmény egészrésze számít, legfeljebb 1024. A meglévő flotta nem változik.",
            "route.allow" => "0 esetén nem használja ezt az útszakaszt; új kapcsolatot az 1 sem hoz létre.",
            "machine.continueFelling" or "machine.fetch" or "machine.deliver" or "machine.return" or "truck.return" or "loading.depart" => "0: nem kéri ezt a lépést; 1: kéri. A hazahívás és a készletbiztonság elsőbbséget kap.",
            "loading.cycleSeconds" or "loading.gripPhase" or "loading.releasePhase" => "A következő rönkciklustól használja a fix értéket; a már megfogott rönk ciklusa folytatódik.",
            "machine.pace" => "0 esetén a gép nem végez munkát, a fenntartási óra tovább jár.",
            _ => "A játék a gráf helyett ezt a fix eredményt használja. A készlet, kapacitás és útvonal fizikai korlátai megmaradnak."
        };
        string consumer = id switch {
            "supply.active" => "A ciklusokat kezelő ellátásiháló-bejáró és a source.fed bemenet",
            "control.allow" or "control.home" or "control.upkeep" => "A közös gépi műveletvégrehajtó",
            "machine.workflow" => "A szeletelt gépi műveletvégrehajtó és az útkeresés",
            "truck.workflow" => "A teherautó fuvarindító, hazatérő és parkoló művelete",
            "orders.allow" => "A térképi célválasztás ellenőrzése és a szeletelt munkára küldési parancs",
            "depot.allow" => "Telephely építési parancs és jelenet létrehozása",
            "depot.processors" or "depot.forwarders" or "depot.trucks" => "Az új telephely szeletelt flotta-létrehozása",
            "machine.pace" => "A gép haladásának, vágásának és rakodásának munkaideje",
            "machine.speed" => "Szeletelt útvonalbejárás",
            "machine.fuel" => "Kopási korrekció után a fogyasztásmérő és a működési költség",
            "machine.mass" => "Közös áthaladási út- és nyomkopási szabály",
            "machine.felling" => "A fák tényleges kitermelése és a processzor rakománya",
            "machine.continueFelling" => "A processzor helyi vágási munkafázisa",
            "machine.fetch" => "A forwarder útkeresése a forrás saranghoz",
            "machine.deliver" => "A gép útkeresése a kijelölt lerakóhoz",
            "machine.return" or "truck.return" => "A rakománybiztos hazatérési útkeresés",
            "route.allow" or "route.cost" => "Útvonalkereső és a legjobb dokkpár kiválasztása",
            "loading.amount" or "unloading.amount" => "Készlet- és értékátadás a sarang, markoló, rakomány és cél között",
            "loading.depart" => "A rakodási munkafázis lezárása és a fuvar indítása",
            "loading.cycleSeconds" or "loading.gripPhase" or "loading.releasePhase" => "Mentett rönkciklus, fizikai megfogás és elengedés; ugyanazt követő animáció",
            "transfer.duration" => "A központi készletes bemutató jármű rakodási állapotgépe",
            _ => "Malom feldolgozatlan és feldolgozott készletének könyvelése"
        };
        return new(result, consumer + ". Egyedi kötés a típusszintű gráfot helyettesíti.", off, meaning, Array.Empty<string>());
    }
    public static BehaviorGraph DefaultGraph(BehaviorHook hook) => RuleProject.DefaultGraph(hook);
}
