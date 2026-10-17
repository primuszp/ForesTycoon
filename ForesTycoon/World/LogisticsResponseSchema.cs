using ForesTycoon.Rules;

namespace ForesTycoon;

[RuleEntity("logisticsProcessor", Abstract = true, Display = "Processzor válasza")]
internal interface ILogisticsProcessorQuery
{
    [RuleRelation("A tervezett megbízás adatai", Display = "Megbízás")] ILogisticsOrder Order { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Időlépés")] ILogisticsStep Step { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Gép / jármű")] ILogisticsMachine Machine { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Forrás")] ILogisticsSource Source { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Gépadatok")] ILogisticsSpec Spec { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Rönk")] ILogisticsLog Log { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Rakodási lépés")] ILogisticsTransfer Transfer { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Útvonal")] ILogisticsRoute Route { get; }
}
[RuleEntity("logisticsForwarder", Abstract = true, Display = "Forwarder válasza")]
internal interface ILogisticsForwarderQuery
{
    [RuleRelation("A tervezett megbízás adatai", Display = "Megbízás")] ILogisticsOrder Order { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Időlépés")] ILogisticsStep Step { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Gép / jármű")] ILogisticsMachine Machine { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Forrás")] ILogisticsSource Source { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Gépadatok")] ILogisticsSpec Spec { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Rönk")] ILogisticsLog Log { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Rakodási lépés")] ILogisticsTransfer Transfer { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Útvonal")] ILogisticsRoute Route { get; }
}
[RuleEntity("logisticsTruck", Abstract = true, Display = "Teherautó válasza")]
internal interface ILogisticsTruckQuery
{
    [RuleRelation("A tervezett megbízás adatai", Display = "Megbízás")] ILogisticsOrder Order { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Időlépés")] ILogisticsStep Step { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Gép / jármű")] ILogisticsMachine Machine { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Forrás")] ILogisticsSource Source { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Gépadatok")] ILogisticsSpec Spec { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Rönk")] ILogisticsLog Log { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Rakodási lépés")] ILogisticsTransfer Transfer { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Útvonal")] ILogisticsRoute Route { get; }
}
[RuleEntity("logisticsMill", Abstract = true, Display = "Malom válasza")]
internal interface ILogisticsMillQuery
{
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Időlépés")] ILogisticsStep Step { get; }
    [RuleRelation("A szabály futásakor mért bemenet", Display = "Malom")] ILogisticsMill Mill { get; }
}
[RuleEntity("logisticsStepInput", Abstract = true, Display = "Időlépés")]
internal interface ILogisticsStep
{
    [RuleField("Eltelt idő: a szabály futásakor rögzített bemenet.", Display = "Eltelt idő", Unit = "s")] double Seconds { get; }
}
[RuleEntity("logisticsMachineInput", Abstract = true, Display = "Gép / jármű")]
internal interface ILogisticsMachine
{
    [RuleField("Gépfajta (0 processzor, 1 forwarder, 2 teherautó): a szabály futásakor rögzített bemenet.", Display = "Gépfajta (0 processzor, 1 forwarder, 2 teherautó)", Unit = "")] double Kind { get; }
    [RuleField("0 pihen, 1 halad, 2 vág, 3 felrakodik, 4 lerakodik. A szabály futásakor mért fázis.", Display = "Munkafázis", Unit = "")] double State { get; }
    [RuleField("Rakomány: a szabály futásakor rögzített bemenet.", Display = "Rakomány", Unit = "m³")] double Cargo { get; }
    [RuleField("Kapacitás: a szabály futásakor rögzített bemenet.", Display = "Kapacitás", Unit = "m³")] double Capacity { get; }
    [RuleField("Kopás: a szabály futásakor rögzített bemenet.", Display = "Kopás", Unit = "")] double Wear { get; }
    [RuleField("Fenntartási tempószorzó: a szabály futásakor rögzített bemenet.", Display = "Fenntartási tempószorzó", Unit = "×")] double Pace { get; }
    [RuleField("Hazahívás: a szabály futásakor rögzített bemenet.", Display = "Hazahívás", Unit = "")] double HomeRequested { get; }
}
[RuleEntity("logisticsSourceInput", Abstract = true, Display = "Forrás")]
internal interface ILogisticsSource
{
    [RuleField("Forrás készlete: a szabály futásakor rögzített bemenet.", Display = "Forrás készlete", Unit = "m³")] double Stock { get; }
    [RuleField("Érkezik még fa: a szabály futásakor rögzített bemenet.", Display = "Érkezik még fa", Unit = "")] double Fed { get; }
    [RuleField("Helyben kitermelhető fa: a szabály futásakor rögzített bemenet.", Display = "Helyben kitermelhető fa", Unit = "m³")] double AvailableTimber { get; }
    [RuleField("Elérhető kijelölt fa: a szabály futásakor rögzített bemenet.", Display = "Elérhető kijelölt fa", Unit = "")] double Reachable { get; }
}
[RuleEntity("logisticsSpecInput", Abstract = true, Display = "Gépadatok")]
internal interface ILogisticsSpec
{
    [RuleField("Kitermelési ráta: a szabály futásakor rögzített bemenet.", Display = "Kitermelési ráta", Unit = "m³/s")] double FellingRate { get; }
    [RuleField("Lerakási ráta: a szabály futásakor rögzített bemenet.", Display = "Lerakási ráta", Unit = "m³/s")] double UnloadRate { get; }
    [RuleField("Üres haladási sebesség: a szabály futásakor rögzített bemenet.", Display = "Üres haladási sebesség", Unit = "csempe/s")] double Speed { get; }
    [RuleField("Rakott haladási sebesség: a szabály futásakor rögzített bemenet.", Display = "Rakott haladási sebesség", Unit = "csempe/s")] double LoadedSpeed { get; }
    [RuleField("Üres menet fogyasztása: a szabály futásakor rögzített bemenet.", Display = "Üres menet fogyasztása", Unit = "l/s")] double FuelDriving { get; }
    [RuleField("Rakott menet fogyasztása: a szabály futásakor rögzített bemenet.", Display = "Rakott menet fogyasztása", Unit = "l/s")] double FuelLoaded { get; }
    [RuleField("Daruzás fogyasztása: a szabály futásakor rögzített bemenet.", Display = "Daruzás fogyasztása", Unit = "l/s")] double FuelCrane { get; }
    [RuleField("Vágás fogyasztása: a szabály futásakor rögzített bemenet.", Display = "Vágás fogyasztása", Unit = "l/s")] double FuelFelling { get; }
    [RuleField("Saját tömeg: a szabály futásakor rögzített bemenet.", Display = "Saját tömeg", Unit = "kg")] double EmptyMass { get; }
    [RuleField("Faanyag sűrűsége: a szabály futásakor rögzített bemenet.", Display = "Faanyag sűrűsége", Unit = "kg/m³")] double TimberDensity { get; }
    [RuleField("Rakodási ráta: a szabály futásakor rögzített bemenet.", Display = "Rakodási ráta", Unit = "m³/s")] double LoadRate { get; }
}
[RuleEntity("logisticsLogInput", Abstract = true, Display = "Rönk")]
internal interface ILogisticsLog
{
    [RuleField("Egy rönk térfogata: a szabály futásakor rögzített bemenet.", Display = "Egy rönk térfogata", Unit = "m³")] double Volume { get; }
    [RuleField("Legfelső rakományrész térfogata: a szabály futásakor rögzített bemenet.", Display = "Legfelső rakományrész térfogata", Unit = "m³")] double LastVolume { get; }
}
[RuleEntity("logisticsTransferInput", Abstract = true, Display = "Rakodási lépés")]
internal interface ILogisticsTransfer
{
    [RuleField("Lerakodás folyik: a szabály futásakor rögzített bemenet.", Display = "Lerakodás folyik", Unit = "")] double Unloading { get; }
    [RuleField("Kért mennyiség: a szabály futásakor rögzített bemenet.", Display = "Kért mennyiség", Unit = "m³")] double Requested { get; }
    [RuleField("Ténylegesen felrakott mennyiség: a szabály futásakor rögzített bemenet.", Display = "Ténylegesen felrakott mennyiség", Unit = "m³")] double Loaded { get; }
}
[RuleEntity("logisticsRouteInput", Abstract = true, Display = "Útvonal")]
internal interface ILogisticsRoute
{
    [RuleField("Célcsempe sérültsége: a szabály futásakor rögzített bemenet.", Display = "Célcsempe sérültsége", Unit = "")] double Damage { get; }
    [RuleField("Felszín (0 terep, 1 aszfalt, 2 makadám, 3 nyom): a szabály futásakor rögzített bemenet.", Display = "Felszín (0 terep, 1 aszfalt, 2 makadám, 3 nyom)", Unit = "")] double Surface { get; }
    [RuleField("Kiinduló csempe azonosítója: a szabály futásakor rögzített bemenet.", Display = "Kiinduló csempe azonosítója", Unit = "")] double From { get; }
    [RuleField("Célcsempe azonosítója: a szabály futásakor rögzített bemenet.", Display = "Célcsempe azonosítója", Unit = "")] double To { get; }
}
[RuleEntity("logisticsMillInput", Abstract = true, Display = "Malom")]
internal interface ILogisticsMill
{
    [RuleField("Feldolgozatlan készlet: a szabály futásakor rögzített bemenet.", Display = "Feldolgozatlan készlet", Unit = "m³")] double Stock { get; }
    [RuleField("Feldolgozott mennyiség: a szabály futásakor rögzített bemenet.", Display = "Feldolgozott mennyiség", Unit = "m³")] double Processed { get; }
}

[RuleEntity("logisticsDepot", Abstract = true, Display = "Telephely építése")]
internal interface ILogisticsDepotQuery
{
    [RuleRelation("A tervezett épülethely állapota", Display = "Épülethely")] ILogisticsSite Site { get; }
    [RuleRelation("Építés előtti állapot", Display = "Telephelyek")] ILogisticsDepot Depot { get; }
}
[RuleEntity("logisticsSiteInput", Abstract = true, Display = "Épülethely")]
internal interface ILogisticsSite
{
    [RuleField("1, ha erdő áll a helyén; engedélyezett építéskor eltávolítjuk.", Display = "Erdő van a helyén", Unit = "0–1")] double Forest { get; }
    [RuleField("1, ha kitermelésre kijelölt területet érint.", Display = "Kitermelés érintett", Unit = "0–1")] double Harvest { get; }
    [RuleField("1, ha a tervezett telephelynek van közvetlen útkapcsolata.", Display = "Útkapcsolat", Unit = "0–1")] double RoadAccess { get; }
}
[RuleEntity("logisticsDepotInput", Abstract = true, Display = "Telephelyek")]
internal interface ILogisticsDepot
{
    [RuleField("Már meglévő telephelyek száma az új építése előtt.", Display = "Meglévő telephelyek", Unit = "db")] double Count { get; }
}

[RuleEntity("logisticsOrderInput", Abstract = true, Display = "Megbízás")]
internal interface ILogisticsOrder
{
    [RuleField("1, ha a fogadó sarangtól kijelölt, kitermelhető fa érhető el.", Display = "Elérhető kijelölt fa", Unit = "0–1")] double HasTimber { get; }
    [RuleField("1 malom cél esetén, 0 sarang vagy új lerakó esetén.", Display = "A cél malom", Unit = "0–1")] double DestinationIsMill { get; }
}
