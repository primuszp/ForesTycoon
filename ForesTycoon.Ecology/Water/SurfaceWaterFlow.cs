using System;

namespace ForesTycoon.Ecology
{
    /// <summary>
    /// Surface water that flows by its level, not only by the ground: the water surface of a tile is its ground height plus
    /// its water depth, and water moves from a higher surface to every lower neighbouring one. So it runs downhill, spreads
    /// out on flat ground, follows a dug ditch even where the ditch is level, fills hollows up to their rim and spills over
    /// banks when more arrives than a channel carries away — and never flows uphill. Each step moves water at most one tile;
    /// all transfers are computed from the levels at the start of the step and applied together, so the volume is conserved
    /// (only outlets export water) and the result does not depend on the order of the tiles.
    /// </summary>
    internal sealed class SurfaceWaterFlow
    {
        /// <summary>Water every tile keeps in puddles and roughness (mm); it does not run off.</summary>
        internal const double RetainedDepth = 0.5;
        /// <summary>Share of a level difference that crosses one tile edge per environment hour, before the stability cap.
        /// Overland flow covers a 5 m tile in tens of seconds, so in practice the cap applies.</summary>
        internal const double ExchangePerHour = 50;
        /// <summary>Sub-steps per environment step: water can travel this many tiles per step.</summary>
        internal const int SubSteps = 4;
        /// <summary>At most this share of a level difference crosses an edge in one step: keeps the explicit scheme stable with four neighbours.</summary>
        private const double MaximumExchange = 0.2;

        private readonly int[][] neighbours;
        private readonly double[] ground, outgoing, change;
        private readonly bool[] outlets;
        private readonly SurfaceRunoffLaw outletLaw = SurfaceRunoffLaw.Legacy;

        /// <param name="groundMillimetres">Ground height of every tile, in millimetres.</param>
        /// <param name="neighbours">Edge neighbours of every tile.</param>
        /// <param name="outlets">Tiles where water leaves the map (edge, sea).</param>
        internal SurfaceWaterFlow(double[] groundMillimetres, int[][] neighbours, bool[] outlets)
        {
            ground = groundMillimetres ?? throw new ArgumentNullException(nameof(groundMillimetres));
            this.neighbours = neighbours ?? throw new ArgumentNullException(nameof(neighbours));
            this.outlets = outlets ?? throw new ArgumentNullException(nameof(outlets));
            if (neighbours.Length != ground.Length || outlets.Length != ground.Length) throw new ArgumentException("Every tile needs a ground height, neighbours and an outlet flag.");
            outgoing = new double[ground.Length]; change = new double[ground.Length];
        }

        internal void SetGround(int tile, double millimetres) => ground[tile] = millimetres;

        /// <summary>Moves water between tiles for one step; returns the depth (summed over tiles) that left the map.</summary>
        internal double Step(double[] surface, double hours)
        {
            if (surface.Length != ground.Length) throw new ArgumentException("Surface raster size mismatch.");
            if (!double.IsFinite(hours) || hours <= 0) return 0;
            double exported = 0;
            for (int s = 0; s < SubSteps; s++) exported += SubStep(surface, hours / SubSteps);
            return exported;
        }

        private double SubStep(double[] surface, double hours)
        {
            double exchange = Math.Min(MaximumExchange, ExchangePerHour * hours);
            var outletStep = outletLaw.Prepare(hours);
            Array.Clear(change);
            double exported = 0;
            // Desired outflow of every tile, from the levels at the start of the step.
            for (int i = 0; i < ground.Length; i++) {
                double level = ground[i] + surface[i], total = 0;
                foreach (int j in neighbours[i]) {
                    double drop = level - (ground[j] + surface[j]);
                    if (drop > 0) total += drop * exchange;
                }
                outgoing[i] = total;
            }
            for (int i = 0; i < ground.Length; i++) {
                double movable = Math.Max(0, surface[i] - RetainedDepth);
                if (outlets[i]) {
                    double leaving = outletStep.Amount(movable);
                    exported += leaving; change[i] -= leaving; movable -= leaving;
                }
                if (outgoing[i] <= 0 || movable <= 0) continue;
                // A tile cannot give more than the water it holds: scale every outflow by the same share.
                double scale = Math.Min(1, movable / outgoing[i]), level = ground[i] + surface[i];
                foreach (int j in neighbours[i]) {
                    double drop = level - (ground[j] + surface[j]);
                    if (drop <= 0) continue;
                    double amount = drop * exchange * scale;
                    change[i] -= amount; change[j] += amount;
                }
            }
            for (int i = 0; i < ground.Length; i++) surface[i] = Math.Max(0, surface[i] + change[i]);
            return exported;
        }
    }
}
