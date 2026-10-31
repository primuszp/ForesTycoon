namespace ForesTycoon
{
    /// <summary>Road building and repair prices per tile, thousand forints.</summary>
    internal static class RoadCosts
    {
        internal const double AsphaltBuild = 650, MacadamBuild = 200, RepairShare = 0.8;
        internal const double DirtBuild = 70;

        internal static double Build(RoadPaving surface, GameTuning t = null) =>
            (t ?? GameTuning.Default)[surface switch { RoadPaving.Asphalt => Tune.AsphaltBuild, RoadPaving.Macadam => Tune.MacadamBuild,
                RoadPaving.Dirt => Tune.DirtBuild, _ => throw new System.ArgumentOutOfRangeException(nameof(surface)) }];

        internal static double Buffer(RoadPaving surface, GameTuning t) => t[surface switch {
            RoadPaving.Asphalt => Tune.AsphaltBufferMetres, RoadPaving.Macadam => Tune.MacadamBufferMetres,
            RoadPaving.Dirt => Tune.DirtBufferMetres, _ => throw new System.ArgumentOutOfRangeException(nameof(surface)) }];
        internal static string Name(RoadPaving surface) => surface switch { RoadPaving.Asphalt => "Aszfalt", RoadPaving.Macadam => "Makadám", RoadPaving.Dirt => "Földút", _ => "Út" };

        /// <summary>Marking a skid trail: no building, only clearing and marking the line.</summary>
        internal const double Trail = 30;

        /// <summary>Restoring <paramref name="damage"/> (lost condition, 0…1) costs a share of the new surface.</summary>
        internal static double Repair(RoadPaving surface, float damage, GameTuning t = null) =>
            Build(surface, t) * (t ?? GameTuning.Default)[Tune.RoadRepairShare] * System.Math.Clamp(damage, 0, 1);
    }
}
