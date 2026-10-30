namespace ForesTycoon
{
    /// <summary>Road building and repair prices per tile, thousand forints.</summary>
    internal static class RoadCosts
    {
        internal const double AsphaltBuild = 650, MacadamBuild = 200, RepairShare = 0.8;

        internal static double Build(RoadPaving surface, GameTuning t = null) =>
            (t ?? GameTuning.Default)[surface == RoadPaving.Asphalt ? Tune.AsphaltBuild : Tune.MacadamBuild];

        /// <summary>Marking a skid trail: no building, only clearing and marking the line.</summary>
        internal const double Trail = 30;

        /// <summary>Restoring <paramref name="damage"/> (lost condition, 0…1) costs a share of the new surface.</summary>
        internal static double Repair(RoadPaving surface, float damage, GameTuning t = null) =>
            Build(surface, t) * (t ?? GameTuning.Default)[Tune.RoadRepairShare] * System.Math.Clamp(damage, 0, 1);
    }
}
