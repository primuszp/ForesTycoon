using System;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>Editable logistics decisions; stock conservation and path topology are host invariants.</summary>
public static class LogisticsRules
{
    public static readonly string[] InputKeys = ["step.seconds", "machine.kind", "machine.state", "machine.cargo", "machine.capacity", "machine.wear", "machine.pace", "machine.homeRequested", "source.stock", "source.fed", "source.availableTimber", "source.reachable", "spec.fellingRate", "spec.unloadRate", "spec.speed", "spec.loadedSpeed", "spec.fuelDriving", "spec.fuelLoaded", "spec.fuelCrane", "spec.fuelFelling", "spec.emptyMass", "spec.timberDensity", "spec.loadRate", "log.volume", "log.lastVolume", "transfer.unloading", "transfer.requested", "transfer.loaded", "route.damage", "route.surface", "route.from", "route.to", "mill.stock", "mill.processed", "site.forest", "site.harvest", "site.roadAccess", "depot.count"];
    public static string Kind(string agent) => "logistics" + char.ToUpperInvariant(agent[0]) + agent[1..];
    public static bool IsKind(string kind) => kind is "logisticsProcessor" or "logisticsForwarder" or "logisticsTruck" or "logisticsMill" or "logisticsDepot";
    private static BehaviorHook H(string id, string name, string kind, string unit, double min, double max, string state, string amount, string schedule) =>
        new(id, name, kind, unit, min, max, state, amount, schedule, true, InputKeys.Where(key => Available(id, kind, key)).ToArray());
    private static bool Available(string id, string kind, string key)
    {
        if (kind == "logisticsDepot") return key.StartsWith("site.") || key == "depot.count";
        if (key.StartsWith("site.") || key.StartsWith("depot.")) return false;
        if (id == "transfer.duration") return key is "step.seconds" or "machine.cargo" or "machine.capacity";
        if (id == "truck.return") return key is "step.seconds" or "machine.cargo" or "machine.capacity" or "machine.kind" or "machine.wear" or "machine.homeRequested" or "source.stock" or "source.fed";
        if (kind == "logisticsMill") return key.StartsWith("step.") || key.StartsWith("mill.");
        if (id.StartsWith("route.")) return key.StartsWith("route.") || key is "machine.cargo" or "machine.capacity";
        if (key.StartsWith("mill.") || key.StartsWith("route.")) return false;
        if (kind == "logisticsTruck") return key is "step.seconds" or "machine.cargo" or "machine.capacity" or "machine.kind" or "machine.wear" or "machine.homeRequested"
            or "source.stock" or "transfer.requested" or "transfer.loaded" or "transfer.unloading" || id == "truck.return" && key == "source.fed";
        if (key is "transfer.requested" or "transfer.loaded") return false;
        if (key == "source.stock" && kind == "logisticsProcessor") return false;
        if (key == "source.fed") return id is "machine.fetch" or "machine.return";
        if (key == "source.reachable") return id == "machine.deliver" && kind == "logisticsProcessor";
        if (key == "source.availableTimber") return id is "machine.felling" or "machine.continueFelling";
        return true;
    }
    public static readonly BehaviorHook[] Hooks = {
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
            "depot.allow" => "0: új telephely nem építhető; a már létezők megmaradnak. Az 1 sem írja felül a száraz, sík, szabad épülethely követelményét.",
            "depot.processors" or "depot.forwarders" or "depot.trucks" => "0: ebből a gépfajtából nem kap új gépet. A pozitív eredmény egészrésze számít, legfeljebb 1024. A meglévő flotta nem változik.",
            "route.allow" => "0 esetén nem használja ezt az útszakaszt; új kapcsolatot az 1 sem hoz létre.",
            "machine.continueFelling" or "machine.fetch" or "machine.deliver" or "machine.return" or "truck.return" or "loading.depart" => "0: nem kéri ezt a lépést; 1: kéri. A hazahívás és a készletbiztonság elsőbbséget kap.",
            "loading.cycleSeconds" or "loading.gripPhase" or "loading.releasePhase" => "A következő rönkciklustól használja a fix értéket; a már megfogott rönk ciklusa folytatódik.",
            "machine.pace" => "0 esetén a gép nem végez munkát, a fenntartási óra tovább jár.",
            _ => "A játék a gráf helyett ezt a fix eredményt használja. A készlet, kapacitás és útvonal fizikai korlátai megmaradnak."
        };
        string consumer = id switch {
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
    public static BehaviorGraph DefaultGraph(BehaviorHook hook)
    {
        string formula = (hook.Id, hook.Kind) switch {
            ("depot.allow", "logisticsDepot") => "ha(site.forest < 0.5; ha(site.harvest < 0.5; site.roadAccess; 0); 0)",
            ("depot.processors" or "depot.forwarders" or "depot.trucks", "logisticsDepot") => "ha(depot.count < 0.5; 1; 0)",
            ("route.cost", "logisticsProcessor") => "1",
            ("route.allow", "logisticsProcessor") => "1",
            ("machine.pace", "logisticsProcessor") => "machine.pace * step.seconds",
            ("machine.speed", "logisticsProcessor") => "ha(machine.cargo > 0.5; spec.loadedSpeed; spec.speed)",
            ("machine.fuel", "logisticsProcessor") => "ha(machine.state < 1.5; ha(machine.cargo > 0.5; spec.fuelLoaded; spec.fuelDriving); ha(machine.state < 2.5; spec.fuelFelling; spec.fuelCrane)) * step.seconds",
            ("machine.mass", "logisticsProcessor") => "spec.emptyMass + max(0; machine.cargo) * spec.timberDensity",
            ("route.cost", "logisticsForwarder") => "1",
            ("route.allow", "logisticsForwarder") => "1",
            ("machine.pace", "logisticsForwarder") => "machine.pace * step.seconds",
            ("machine.speed", "logisticsForwarder") => "ha(machine.cargo > 0.5; spec.loadedSpeed; spec.speed)",
            ("machine.fuel", "logisticsForwarder") => "ha(machine.state < 1.5; ha(machine.cargo > 0.5; spec.fuelLoaded; spec.fuelDriving); ha(machine.state < 2.5; spec.fuelFelling; spec.fuelCrane)) * step.seconds",
            ("machine.mass", "logisticsForwarder") => "spec.emptyMass + max(0; machine.cargo) * spec.timberDensity",
            ("loading.amount", "logisticsForwarder") => "min(log.volume; machine.capacity - machine.cargo)",
            ("unloading.amount", "logisticsForwarder") => "log.lastVolume",
            ("loading.depart", "logisticsForwarder") => "0",
            ("route.cost", "logisticsTruck") => "ha(route.surface < 1.5; 1; ha(route.surface < 2.5; 1.15; 3.5 + 2 * route.damage))",
            ("route.allow", "logisticsTruck") => "1",
            ("loading.amount", "logisticsTruck") => "step.seconds * machine.capacity / 3",
            ("unloading.amount", "logisticsTruck") => "step.seconds * machine.capacity / 3",
            ("loading.depart", "logisticsTruck") => "ha(machine.cargo > machine.capacity - 0.001; 1; ha(transfer.loaded < 0.000001; ha(machine.cargo > 0; ha(transfer.requested > 0; 1; ha(source.stock < 0.000001; 1; 0)); 0); 0))",
            ("loading.cycleSeconds", "logisticsForwarder") => "log.volume / ha(transfer.unloading > 0.5; spec.unloadRate; spec.loadRate)",
            ("loading.gripPhase", "logisticsForwarder") => "0.32",
            ("loading.releasePhase", "logisticsForwarder") => "0.77",
            ("unloading.amount", "logisticsProcessor") => "min(machine.cargo; spec.unloadRate * step.seconds)",
            ("machine.felling", "logisticsProcessor") => "min(spec.fellingRate * step.seconds; max(0; machine.capacity - machine.cargo))",
            ("machine.continueFelling", "logisticsProcessor") => "ha(machine.cargo < machine.capacity - 0.001; ha(source.availableTimber > 0.001; 1; 0); 0)",
            ("machine.deliver", "logisticsProcessor") => "ha(source.reachable < 0.5; 1; ha(machine.cargo > machine.capacity - 0.001; 1; 0))",
            ("machine.fetch", "logisticsForwarder") => "ha(source.stock > machine.capacity * 0.5 - 0.0000001; 1; ha(source.stock > 0.01; ha(source.fed < 0.5; 1; 0); 0))",
            ("machine.deliver", "logisticsForwarder") => "ha(machine.cargo > 0.0001; 1; 0)",
            ("machine.return", "logisticsForwarder") => "ha(source.fed < 0.5; 1; 0)",
            ("truck.return", "logisticsTruck") => "ha(source.stock < 0.010000001; ha(source.fed < 0.5; 1; 0); 0)",
            ("mill.process", "logisticsMill") => "min(mill.stock; step.seconds * 0.25)",
            ("transfer.duration", "logisticsTruck") => "3",
            _ => throw new ArgumentException("Unknown logistics hook.", nameof(hook))
        };
        return RuleFormula.Compile(formula, new() { Id = "default." + hook.Kind + "." + hook.Id, Name = hook.Name, Hook = hook.Id, Kind = hook.Kind },
            hook.Inputs.Select(k => new FormulaName(k, BehaviorOperation.Input, k)).ToArray());
    }
}
