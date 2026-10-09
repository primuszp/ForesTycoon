namespace ForesTycoon
{
    /// <summary>Road building and repair prices per tile, thousand forints.</summary>
    internal static class RoadCosts
    {
        internal static double Build(RoadPaving surface) => surface == RoadPaving.Asphalt ? 650 : 200;

        /// <summary>Marking a skid trail: no building, only clearing and marking the line.</summary>
        internal const double Trail = 30;

        /// <summary>Restoring <paramref name="damage"/> (lost condition, 0…1) costs a share of the new surface.</summary>
        internal static double Repair(RoadPaving surface, float damage) => Build(surface) * 0.8 * System.Math.Clamp(damage, 0, 1);
    }
}
