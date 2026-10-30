using ForesTycoon.Rules;

namespace ForesTycoon;

// Immutable query snapshots, discovered exactly like the other response inputs.
// They never expose a live derived property that could recurse into its own graph.
[RuleEntity("environment", Abstract = true, Display = "Környezeti válasz")]
internal interface IEnvironmentQuery
{
    [RuleRelation("Az esemény mért bemenetei", Display = "Időjárási esemény")] IEnvironmentEvent Event { get; }
    [RuleRelation("A szimuláció naptára", Display = "Naptár")] IEnvironmentCalendar Calendar { get; }
    [RuleRelation("A lekérdezéshez átadott légköri pillanatkép", Display = "Levegő")] IEnvironmentAir Weather { get; }
    [RuleRelation("A seedelt régióminta", Display = "Régió")] IEnvironmentRegion Region { get; }
    [RuleRelation("A klímakatalógus adatai", Display = "Klímabeállítás")] IEnvironmentClimate Climate { get; }
    [RuleRelation("A csempe mért adatai", Display = "Csempe")] IEnvironmentTile Tile { get; }
    [RuleRelation("A talajtípus bemenetei", Display = "Talaj")] IEnvironmentSoil Soil { get; }
    [RuleRelation("Egy fa mért adatai", Display = "Fa")] IEnvironmentTree Tree { get; }
    [RuleRelation("A fa típusadatai", Display = "Fafaj")] IEnvironmentSpecies Species { get; }
    [RuleRelation("Az előző koronagráfok összegzett eredményei", Display = "Lombkorona")] IEnvironmentCanopy Canopy { get; }
    [RuleRelation("A vizsgált szomszéd geometriája", Display = "Szomszéd")] IEnvironmentNeighbor Neighbor { get; }
    [RuleRelation("A hóválasz korábbi eredménye", Display = "Hó")] IEnvironmentSnow Snow { get; }
    [RuleRelation("A környezeti lépés ideje", Display = "Lépés")] IEnvironmentStep Step { get; }
}
[RuleEntity("environmentEvent", Abstract = true, Display = "Időjárási esemény")]
internal interface IEnvironmentEvent
{
    [RuleField("0 napos, 1 felhős, 2 eső, 3 hó, 4 vihar", Display = "Időjárás típusa")] WeatherPreset Type { get; }
    [RuleField("Az eseményhez sorban kapott, mentett generátorból származó szám", Display = "Seedelt sorsolás", Min = 0, Max = 1)] double Random { get; }
    [RuleField("A megkezdett esemény teljes időtartama", Display = "Teljes időtartam", Unit = "s", Min = 0)] double Seconds { get; }
    [RuleField("Az esemény kezdete óta eltelt idő", Display = "Eltelt idő", Unit = "s", Min = 0)] double Elapsed { get; }
    [RuleField("A rámpák szerinti jelenlegi csapadékintenzitás", Display = "Jelenlegi csapadék", Unit = "mm/h", Min = 0)] double RainRate { get; }
    [RuleField("Az esemény kezdetén rögzített csúcsintenzitás", Display = "Csapadék csúcsa", Unit = "mm/h", Min = 0)] double Peak { get; }
    [RuleField("Az eseményváltáskor rögzített felhőzet", Display = "Előző felhőzet", Min = 0, Max = 1)] double PreviousCloud { get; }
}
[RuleEntity("environmentCalendar", Abstract = true, Display = "Naptár")]
internal interface IEnvironmentCalendar
{
    [RuleField("0 tavasz, 1 nyár, 2 ősz, 3 tél", Display = "Évszak", Min = 0, Max = 3)] double Season { get; }
    [RuleField("Az év hossza a referencia-tempóhoz képest", Display = "Időlépték", Min = .1, Max = 3)] double TimeScale { get; }
    [RuleField("Az évszakos szinuszjel; a hatását a gráf választja meg", Display = "Évszakos hullám", Min = -1, Max = 1)] double SeasonWave { get; }
    [RuleField("Az éven belüli helyzet, saját éves görbéhez is beköthető", Display = "Év aránya", Min = 0, Max = 1)] double YearPhase { get; }
}
[RuleEntity("environmentAir", Abstract = true, Display = "Levegő")]
internal interface IEnvironmentAir
{
    [RuleField("A lekérdezés légköri hőmérséklete; párolgásnál a helyi klíma eredménye", Display = "Hőmérséklet", Unit = "°C", Min = -100, Max = 100)] double Temperature { get; }
    [RuleField("A levegő relatív páratartalma", Display = "Páratartalom", Min = 0, Max = 1)] double Humidity { get; }
    [RuleField("A légköri szélsebesség", Display = "Szélsebesség", Unit = "m/s", Min = 0, Max = 100)] double Wind { get; }
    [RuleField("A felhőzet gráfjának eredménye", Display = "Felhőzet", Min = 0, Max = 1)] double Cloud { get; }
    [RuleField("A sugárzási gráf eredménye", Display = "Sugárzás", Min = 0, Max = 10)] double Radiation { get; }
}
[RuleEntity("environmentRegion", Abstract = true, Display = "Régió")]
internal interface IEnvironmentRegion
{
    [RuleField("A seedelt régió hőmérsékleti eltérése", Display = "Hőmérsékleti eltérés", Unit = "°C")] double TemperatureAnomaly { get; }
    [RuleField("A seedelt régió páratartalmi eltérése", Display = "Páratartalmi eltérés", Min = -.4, Max = .4)] double HumidityAnomaly { get; }
    [RuleField("A seedelt régió csapadék-aránya", Display = "Csapadékszorzó", Min = .05, Max = 1.95)] double RainMultiplier { get; }
}
[RuleEntity("environmentClimate", Abstract = true, Display = "Klímabeállítás")]
internal interface IEnvironmentClimate
{
    [RuleField("A klímakatalógus magassági hűtése", Display = "Magassági hűtés", Unit = "°C", Min = 0, Max = 30)] double ElevationCooling { get; }
}
[RuleEntity("environmentTile", Abstract = true, Display = "Csempe")]
internal interface IEnvironmentTile
{
    [RuleField("Relatív terepmagasság", Display = "Magassági helyzet", Min = 0, Max = 1)] double Elevation { get; }
    [RuleField("A csempe geometriai területe", Display = "Terület", Unit = "m²", Min = .01)] double Area { get; }
    [RuleField("A szél felőli szomszédok legnagyobb magassági többlete", Display = "Szél felőli domborzat", Min = 0)] double UpwindRelief { get; }
    [RuleField("A tárolt hó vízegyenértéke", Display = "Hókészlet", Unit = "mm", Min = 0)] double Snow { get; }
    [RuleField("A gyökérzóna vízkészlete", Display = "Talajvízkészlet", Unit = "mm", Min = 0)] double SoilWater { get; }
    [RuleField("A felszín vízkészlete", Display = "Felszíni víz", Unit = "mm", Min = 0)] double SurfaceWater { get; }
    [RuleField("Az átnedvesedési gráf eredménye", Display = "Átnedvesedés", Min = 0, Max = 1)] double Wetness { get; }
}
[RuleEntity("environmentSoil", Abstract = true, Display = "Talaj")]
internal interface IEnvironmentSoil
{
    [RuleField("Talajtípus vízkapacitása", Display = "Szabadföldi vízkapacitás", Unit = "mm", Min = 0)] double FieldCapacity { get; }
    [RuleField("Talajtípus telítési készlete", Display = "Telítési készlet", Unit = "mm", Min = 0)] double Saturation { get; }
    [RuleField("Teherbírás-válasznál a típusadat, járhatóságnál a teherbírási gráf eredménye", Display = "Teherbírás", Min = 0, Max = 1)] double BearingCapacity { get; }
    [RuleField("1, ha a talajtípus explicit teherbírást tartalmaz", Display = "Megadott teherbírás", Min = 0, Max = 1)] double BearingDefined { get; }
    [RuleField("A talajtípus drénezése", Display = "Drénezés", Unit = "mm/h", Min = 0)] double Drainage { get; }
}
[RuleEntity("environmentTree", Abstract = true, Display = "Fa")]
internal interface IEnvironmentTree
{
    [RuleField("A fa aktuális koronasugara", Display = "Koronasugár", Unit = "m", Min = 0)] double CrownRadius { get; }
    [RuleField("A fa aktuális egészsége", Display = "Egészség", Min = 0, Max = 1)] double Health { get; }
}
[RuleEntity("environmentSpecies", Abstract = true, Display = "Fafaj")]
internal interface IEnvironmentSpecies
{
    [RuleField("1 örökzöld, 0 lombhullató", Display = "Örökzöld", Min = 0, Max = 1)] double Evergreen { get; }
}
[RuleEntity("environmentCanopy", Abstract = true, Display = "Lombkorona")]
internal interface IEnvironmentCanopy
{
    [RuleField("A koronaterület-gráf eredménye; csempegráfnál a fák eredményeinek összege", Display = "Hatásos koronaterület", Unit = "m²", Min = 0)] double CrownArea { get; }
    [RuleField("A borításgráf eredménye", Display = "Lombborítás", Min = 0, Max = 1)] double Cover { get; }
    [RuleField("A fák vízfogási eredményeinek összege", Display = "Vízfogás összege", Min = 0)] double Interception { get; }
    [RuleField("A fák lombfelületi eredményeinek összege", Display = "Lombfelület összege", Unit = "m²", Min = 0)] double LeafArea { get; }
}
[RuleEntity("environmentNeighbor", Abstract = true, Display = "Szomszéd")]
internal interface IEnvironmentNeighbor
{
    [RuleField("A szomszédhoz mutató egységvektor X komponense", Display = "Irány X", Min = -1, Max = 1)] double DirectionX { get; }
    [RuleField("A szomszédhoz mutató egységvektor Y komponense", Display = "Irány Y", Min = -1, Max = 1)] double DirectionY { get; }
}
[RuleEntity("environmentSnow", Abstract = true, Display = "Hó")]
internal interface IEnvironmentSnow
{
    [RuleField("A hóvédettségi gráf eredménye", Display = "Szélvédettség", Min = 0, Max = 1)] double Shelter { get; }
}
[RuleEntity("environmentStep", Abstract = true, Display = "Lépés")]
internal interface IEnvironmentStep
{
    [RuleField("Az eltelt környezeti idő", Display = "Eltelt idő", Unit = "h", Min = 0)] double Hours { get; }
}
