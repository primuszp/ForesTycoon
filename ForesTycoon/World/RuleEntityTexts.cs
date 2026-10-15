using System;
using System.Collections.Generic;
using ForesTycoon.Rules;

namespace ForesTycoon;

/// <summary>
/// Hungarian names and help of the rule object model (RuleEntities.cs), as the rule editor shows them to designers.
/// Keys are the declaring entity's id and the code name: <c>road</c>, <c>road.condition</c>, <c>trail.remove</c>.
/// A field without an entry is shown with its English code name.
/// </summary>
internal static class RuleEntityTexts
{
    internal static RuleTexts Hungarian { get; } = new() {
        Units = new(StringComparer.Ordinal) { ["forest years"] = "erdőév", ["tiles/s"] = "csempe/s", ["mm/h"] = "mm/óra" },
        Items = new(StringComparer.Ordinal) {
            ["object"] = new("Világobjektum", "Bármi a világban, amire szabály vonatkozhat."),
            ["object.index"] = new("Sorszám", "Az elem belső sorszáma (csempéknél a csempe azonosítója)."),
            ["place"] = new("Hely", "A térkép egy pontja: talaj, víz, borítás."),

            ["tile"] = new("Csempe", "A térkép egy csempéje: talaja, vize, borítása. Más objektumok a „Csempe” kapcsolaton át olvassák.") { Element = "csempe" },
            ["tile.moisture"] = new("Nedvesség", "A hidrológiai térkép nedvessége; az útromlás ezt használja (nem a gyökérzóna vize)."),
            ["tile.rootWater"] = new("Gyökérzóna vize", "A gyökérzónában tárolt víz."),
            ["tile.surfaceWater"] = new("Felszíni víz", "A felszínen álló víz."),
            ["tile.deepWater"] = new("Mélyvíz", "A gyökérzóna alatti víz."),
            ["tile.excessWater"] = new("Többletvíz", "A szabadföldi vízkapacitás feletti víz a gyökérzónában."),
            ["tile.waterSupply"] = new("Vízellátottság", "A növények számára felvehető víz aránya."),
            ["tile.waterlogging"] = new("Pangóvíz", "A talaj állóvíz alatti része."),
            ["tile.infiltration"] = new("Beszivárgás", "A talaj beszivárgási sebessége."),
            ["tile.saturation"] = new("Telítettség", "Telített állapotban a talajban tárolt teljes víz."),
            ["tile.fieldCapacity"] = new("Vízkapacitás", "A talaj szabadföldi vízkapacitása."),
            ["tile.wiltingPoint"] = new("Hervadáspont", "A talaj hervadáspontja."),
            ["tile.drainage"] = new("Drénezés", "A talaj elvezetési sebessége."),
            ["tile.bearingCapacity"] = new("Teherbírás", "Átázva mennyire bírja a talaj a földutat és a nyomot."),
            ["tile.cover"] = new("Lombborítás", "A lombkorona záródása."),
            ["tile.paved"] = new("Burkolt", "Út vagy épület fedi a csempét.") { True = "a csempe burkolt", False = "a csempe nem burkolt" },
            ["tile.paved.true"] = new("igen"), ["tile.paved.false"] = new("nem"),
            ["tile.wetness"] = new("Átnedvesedés", "Mennyire ázott át a talaj."),

            ["world"] = new("Világ", "Az egész játék: idő, időjárás, pénz. Minden szabály olvashatja world.<mező> néven.") { Element = "világ" },
            ["world.time"] = new("Idő", "A játék kezdete óta eltelt erdőévek."),
            ["world.treeCount"] = new("Fák száma", "Élő fák."),
            ["world.roadCount"] = new("Utak száma", "Útcsempék."),
            ["world.rain"] = new("Eső", "A folyékony csapadék intenzitása."),
            ["world.temperature"] = new("Hőmérséklet", "Léghőmérséklet."),
            ["world.snow"] = new("Hó", "A térképen tárolt hó vízértéke."),
            ["world.water"] = new("Víz", "A térképen tárolt víz."),
            ["world.income"] = new("Bevétel", "Az eddigi bevétel."),
            ["world.expenses"] = new("Kiadás", "Építésre és útjavításra eddig költött összeg."),
            ["world.runningCosts"] = new("Működési költség", "Az eddigi üzemeltetési költség."),
            ["world.balance"] = new("Egyenleg", "Bevétel mínusz kiadások."),

            ["road"] = new("Út", "Burkolt útcsempe; az időjárási szabályok az állapotát változtatják.") { Element = "út" },
            ["road.condition"] = new("Állapot", "Az út állapota: 1 = új, 0 = teljesen elkopott."),
            ["road.surface"] = new("Burkolat", "Az út burkolata: aszfalt vagy makadám."),
            ["road.surface.asphalt"] = new("aszfalt"), ["road.surface.macadam"] = new("makadám"),
            ["road.rainIntensity"] = new("Eső erőssége", "A pillanatnyi csapadék 20 mm/óra arányában, 0–1 közé szorítva."),
            ["road.tile"] = new("Csempe", "Az út csempéje: talaj, nedvesség."),

            ["trail"] = new("Közelítőnyom", "Ideiglenes erdei nyom. A gépek mélyítik a vályúit és nullázzák az állásidejét.") { Element = "nyom" },
            ["trail.wear"] = new("Vályúmélység", "0 = sima, 1 = mélyen kivájt nyom."),
            ["trail.idleYears"] = new("Állásidő", "A legutóbbi gépáthaladás óta eltelt erdőévek."),
            ["trail.tile"] = new("Csempe", "A nyom csempéje: talaj, nedvesség."),
            ["trail.remove"] = new("Eltüntetés", "A benőtt nyom eltűnik, az útvonalak frissülnek."),

            ["plant"] = new("Növény", "Élő növény: kora és egészsége van, egy csempén áll."),
            ["plant.age"] = new("Kor", "A növény életkora."),
            ["plant.health"] = new("Egészség", "Életerő: szorozza a növekedést; a tartósan gyenge növény elpusztul."),
            ["plant.tile"] = new("Csempe", "A csempe, amelyen a növény áll."),

            ["tree"] = new("Fa", "Egyedi fa: méret, erőforrások, stressz. A havi növekedés a játékban fut, és látja a szabályok módosításait.") { Element = "fa" },
            ["tree.height"] = new("Magasság", "A fa magassága."),
            ["tree.diameter"] = new("Átmérő", "Mellmagassági átmérő."),
            ["tree.light"] = new("Fény", "A szomszédok árnyékában a fához jutó fény."),
            ["tree.water"] = new("Víz", "A gyökérversengés után a fához jutó víz."),
            ["tree.stressYears"] = new("Stresszévek", "Egymást követő hiányos évek; 4 fölött a gyenge fa elpusztul."),
            ["tree.shadeTolerance"] = new("Árnyéktűrés", "A fafaj árnyéktűrése."),

            ["stump"] = new("Tönk", "A kivágott fa maradványa. Lebomlik; a szabály dönti el, mikor tűnik el.") { Element = "tönk" },
            ["stump.decayProgress"] = new("Lebomlás", "0 = friss, 1 = teljesen lebomlott."),
            ["stump.age"] = new("Kor", "A kivágás óta eltelt erdőévek."),
            ["stump.diameter"] = new("Átmérő", "A kivágott fa törzsátmérője."),
            ["stump.tile"] = new("Csempe", "A csempe, amelyen a tönk áll."),
            ["stump.remove"] = new("Eltüntetés", "A tönk elkorhadt és eltűnik."),

            ["vehicle"] = new("Jármű", "Bármi, ami az úthálózaton mozog."),
            ["vehicle.speed"] = new("Sebesség", "A jármű pillanatnyi sebessége."),
            ["vehicle.distance"] = new("Megtett út", "Az eddig megtett távolság."),
            ["vehicle.fuel"] = new("Elhasznált gázolaj", "Az eddig elégetett gázolaj."),
            ["vehicle.roadDamage"] = new("Útkárosítás", "A jármű alatti út kopása."),

            ["truck"] = new("Rönkszállító", "Rönkszállító teherautó. Mezői egyelőre csak olvashatók.") { Element = "teherautó" },
            ["truck.cargo"] = new("Rakomány", "A szállított faanyag."),
            ["truck.fill"] = new("Telítettség", "A rakomány a kapacitás arányában."),
            ["truck.emptyMass"] = new("Önsúly", "A jármű tömege rakomány nélkül."),
        }
    };
}
