namespace ForesTycoon.Rendering
{
    /// <summary>
    /// One shared size table for everything placed on the terrain. The world is a diorama,
    /// not a survey: nothing is to scale, but the order of sizes must still read correctly.
    /// A tile is 5 world units; the log truck defines "about a metre" per unit.
    ///
    ///   elk (≈1.7 tall, ≈1.6 long)  &lt;  truck (≈1.9 tall, ≈5.8 long)
    ///   truck  &lt;  sawmill (building ≈8 × 7, roof ≈6, chimney ≈10; yard props fill the 2×2 tiles)
    ///   sawmill roof  ≈  mature trees (≈7–8 tall)
    ///
    /// Animals and vehicles are drawn larger than true scale so they stay readable from the
    /// default camera, but never so large that a truck rivals the building it delivers to.
    /// </summary>
    internal static class DioramaScale
    {
        /// <summary>Truck width as a share of the paved lane. Wheelbase, wheel spin and steering follow from it.</summary>
        internal const float TruckLaneFill = 0.62f;

        /// <summary>
        /// Longest side of the sawmill building (not its scattered yard props) as a share of the
        /// 2×2 tile footprint. The log piles around it are pulled inside the footprint edge.
        /// </summary>
        internal const float SawmillFootprintFill = 0.80f;

        /// <summary>Uniform scale applied to the imported elk (raw model: ≈2.3 long, ≈2.4 tall).</summary>
        internal const float Elk = 0.7f;

        /// <summary>Longest side of the fish model in world units.</summary>
        internal const float FishLength = 0.55f;

        internal static float SawmillFootprint(float tileWidth, float tileHeight) =>
            2f * System.Math.Min(tileWidth, tileHeight);

        internal static float SawmillWidth(float tileWidth, float tileHeight) =>
            SawmillFootprint(tileWidth, tileHeight) * SawmillFootprintFill;
    }
}
