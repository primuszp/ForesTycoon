using ForesTycoon.Rules;

namespace ForesTycoon;

[RuleEntity("vehicleResponse", Abstract = true, Display = "Járműválasz")]
internal interface IVehicleResponseQuery
{
    [RuleRelation("A lekérdezés mért bemenetei", Display = "Járműadatok")] IVehicleResponseSpec Spec { get; }
    [RuleRelation("A lekérdezés mért bemenetei", Display = "Jármű")] IVehicleResponseVehicle Vehicle { get; }
    [RuleRelation("A lekérdezés mért bemenetei", Display = "Útfelület")] IVehicleResponseRoad Road { get; }
    [RuleRelation("A lekérdezés mért bemenetei", Display = "Fizikai bemenet")] IVehicleResponsePhysics Physics { get; }
    [RuleRelation("A lekérdezés mért bemenetei", Display = "Időlépés")] IVehicleResponseStep Step { get; }
    [RuleRelation("A lekérdezés mért bemenetei", Display = "Útvonal")] IVehicleResponseRoute Route { get; }
    [RuleRelation("A lekérdezés mért bemenetei", Display = "Karbantartás")] IVehicleResponseService Service { get; }
}
[RuleEntity("vehicleResponseSpec", Abstract = true, Display = "Járműadatok")]
internal interface IVehicleResponseSpec
{
    [RuleField("Fék lassulása: a számításhoz átadott érték.", Display = "Fék lassulása")] double Braking { get; }
    [RuleField("Meghibásodási hajlam: a számításhoz átadott érték.", Display = "Meghibásodási hajlam")] double BreakdownHazard { get; }
    [RuleField("Légellenállási felület: a számításhoz átadott érték.", Display = "Légellenállási felület", Unit = "m²")] double DragArea { get; }
    [RuleField("Hajtás hatásfoka: a számításhoz átadott érték.", Display = "Hajtás hatásfoka")] double Drivetrain { get; }
    [RuleField("Saját tömeg: a számításhoz átadott érték.", Display = "Saját tömeg", Unit = "kg")] double EmptyMass { get; }
    [RuleField("Helyszíni kopásenyhítés: a számításhoz átadott érték.", Display = "Helyszíni kopásenyhítés")] double FieldRepairRelief { get; }
    [RuleField("Fajlagos motorfogyasztás: a számításhoz átadott érték.", Display = "Fajlagos motorfogyasztás", Unit = "l/kWh")] double FuelPerKilowattHour { get; }
    [RuleField("Alapjárati fogyasztás: a számításhoz átadott érték.", Display = "Alapjárati fogyasztás", Unit = "l/h")] double IdleFuelPerHour { get; }
    [RuleField("Tapadási vonóerőhatár: a számításhoz átadott érték.", Display = "Tapadási vonóerőhatár", Unit = "N")] double MaxTractiveForce { get; }
    [RuleField("Motorteljesítmény: a számításhoz átadott érték.", Display = "Motorteljesítmény", Unit = "W")] double Power { get; }
    [RuleField("Javítás alapára: a számításhoz átadott érték.", Display = "Javítás alapára")] double RepairBaseCost { get; }
    [RuleField("Javítás alapideje: a számításhoz átadott érték.", Display = "Javítás alapideje", Unit = "s")] double RepairSeconds { get; }
    [RuleField("Kopásarányos javítási ár: a számításhoz átadott érték.", Display = "Kopásarányos javítási ár")] double RepairWearCost { get; }
    [RuleField("Aszfalt gördülése: a számításhoz átadott érték.", Display = "Aszfalt gördülése")] double RollingAsphalt { get; }
    [RuleField("Terep gördülése: a számításhoz átadott érték.", Display = "Terep gördülése")] double RollingDirt { get; }
    [RuleField("Makadám gördülése: a számításhoz átadott érték.", Display = "Makadám gördülése")] double RollingGravel { get; }
    [RuleField("Egyenetlenség gördülési hatása: a számításhoz átadott érték.", Display = "Egyenetlenség gördülési hatása")] double RoughnessRolling { get; }
    [RuleField("Karbantartás egységára: a számításhoz átadott érték.", Display = "Karbantartás egységára")] double ServiceCostPerWear { get; }
    [RuleField("Karbantartási ráta: a számításhoz átadott érték.", Display = "Karbantartási ráta")] double ServicePerSecond { get; }
    [RuleField("Rakomány sűrűsége: a számításhoz átadott érték.", Display = "Rakomány sűrűsége", Unit = "kg/m³")] double TimberDensity { get; }
    [RuleField("Kopási többletfogyasztás: a számításhoz átadott érték.", Display = "Kopási többletfogyasztás")] double WearFuelPenalty { get; }
    [RuleField("Kopási lassulás: a számításhoz átadott érték.", Display = "Kopási lassulás")] double WearPacePenalty { get; }
    [RuleField("Kopási alapráta: a számításhoz átadott érték.", Display = "Kopási alapráta")] double WearPerSecond { get; }
}
[RuleEntity("vehicleResponseVehicle", Abstract = true, Display = "Jármű")]
internal interface IVehicleResponseVehicle
{
    [RuleField("Rakomány: a számításhoz átadott érték.", Display = "Rakomány", Unit = "m³")] double Cargo { get; }
    [RuleField("Telítettség: a számításhoz átadott érték.", Display = "Telítettség")] double CargoFill { get; }
    [RuleField("Tényleges motorteljesítmény: a számításhoz átadott érték.", Display = "Tényleges motorteljesítmény", Unit = "W")] double EnginePower { get; }
    [RuleField("1 vágás, 0 más munka", Display = "Vágás folyik")] double Felling { get; }
    [RuleField("0 teherautó, 1 processzor, 2 forwarder", Display = "Járműfajta")] double Kind { get; }
    [RuleField("Össztömeg: a számításhoz átadott érték.", Display = "Össztömeg", Unit = "kg")] double Mass { get; }
    [RuleField("Gördülési tényező: a számításhoz átadott érték.", Display = "Gördülési tényező")] double Rolling { get; }
    [RuleField("Sebesség: a számításhoz átadott érték.", Display = "Sebesség", Unit = "m/s")] double Speed { get; }
    [RuleField("Terhelés: a számításhoz átadott érték.", Display = "Terhelés")] double Strain { get; }
    [RuleField("Kopás: a számításhoz átadott érték.", Display = "Kopás")] double Wear { get; }
}
[RuleEntity("vehicleResponseRoad", Abstract = true, Display = "Útfelület")]
internal interface IVehicleResponseRoad
{
    [RuleField("A tényleges haladási irány szerinti lejtés", Display = "Lejtés")] double Grade { get; }
    [RuleField("1 kanyar, 0 egyenes szakasz", Display = "Kanyar", Min = 0, Max = 1)] double Corner { get; }
    [RuleField("Útsérültség: a számításhoz átadott érték.", Display = "Útsérültség")] double Damage { get; }
    [RuleField("1 nyom vagy terepi munka, 0 szilárdabb felszín", Display = "Nehéz terep")] double Rough { get; }
    [RuleField("Egyenetlenség: a számításhoz átadott érték.", Display = "Egyenetlenség")] double Roughness { get; }
    [RuleField("Lejtő koszinusza: a számításhoz átadott érték.", Display = "Lejtő koszinusza")] double SlopeCos { get; }
    [RuleField("Lejtő szinusza: a számításhoz átadott érték.", Display = "Lejtő szinusza")] double SlopeSin { get; }
    [RuleField("0 aszfalt, 1 makadám, 2 földfelszín", Display = "Burkolat")] double Surface { get; }
}
[RuleEntity("vehicleResponsePhysics", Abstract = true, Display = "Fizikai bemenet")]
internal interface IVehicleResponsePhysics
{
    [RuleField("Levegő sűrűsége: a számításhoz átadott érték.", Display = "Levegő sűrűsége")] double AirDensity { get; }
    [RuleField("Nehézségi gyorsulás: a számításhoz átadott érték.", Display = "Nehézségi gyorsulás")] double Gravity { get; }
}
[RuleEntity("vehicleResponseStep", Abstract = true, Display = "Időlépés")]
internal interface IVehicleResponseStep
{
    [RuleField("Eltelt idő: a számításhoz átadott érték.", Display = "Eltelt idő", Unit = "s")] double Seconds { get; }
}
[RuleEntity("vehicleResponseRoute", Abstract = true, Display = "Útvonal")]
internal interface IVehicleResponseRoute
{
    [RuleField("A jármű előírt utazósebessége", Display = "Utazósebesség", Unit = "csempe/s", Min = 0)] double Cruise { get; }
    [RuleField("Csempe úthossza: a számításhoz átadott érték.", Display = "Csempe úthossza", Unit = "m/csempe")] double MetresPerTile { get; }
    [RuleField("Megállásig hátralévő út: a számításhoz átadott érték.", Display = "Megállásig hátralévő út", Unit = "csempe")] double Remaining { get; }
    [RuleField("Útvonali célsebesség: a számításhoz átadott érték.", Display = "Útvonali célsebesség")] double TargetSpeed { get; }
}
[RuleEntity("vehicleResponseService", Abstract = true, Display = "Karbantartás")]
internal interface IVehicleResponseService
{
    [RuleField("Enyhített kopás: a számításhoz átadott érték.", Display = "Enyhített kopás")] double Relief { get; }
}
