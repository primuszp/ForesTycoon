using System;

namespace ForesTycoon
{
    /// <summary>
    /// Wear and breakdowns of a fleet vehicle. Every working second wears it (rough ground and loads more); a worn
    /// vehicle burns more fuel, works slower and breaks down more often. A breakdown stops it where it stands until the
    /// mobile mechanic has repaired it (part of the wear goes, at a cost); at its depot it is serviced back to new.
    /// The breakdown draws come from the vehicle's own seeded generator, so a replay breaks the same vehicle at the
    /// same moment.
    /// </summary>
    internal sealed class VehicleUpkeep
    {
        /// <summary>0 new … 1 worn out.</summary>
        internal float Wear;
        internal bool Broken;
        /// <summary>Seconds until the mechanic has the broken vehicle running again.</summary>
        internal float RepairLeft;
        internal uint Seed;
        internal int Breakdowns;

        internal const float WearPerSecond = 0.00025f, RepairSeconds = 40f, FieldRepairRelief = 0.3f, ServicePerSecond = 0.02f;
        internal const double RepairBaseCost = 150, RepairWearCost = 300, ServiceCostPerWear = 200;
        internal const double BreakdownHazard = 0.004, WearFuelPenalty = 0.5, WearPacePenalty = 0.3;

        /// <summary>Fuel, and slowdown of work and driving, grow with the wear (default tuning).</summary>
        internal double FuelFactor => Fuel(GameTuning.Default);
        internal float PaceFactor => Pace(GameTuning.Default);
        internal double Fuel(GameTuning t) => 1 + t[Tune.WearFuelPenalty] * Wear;
        internal float Pace(GameTuning t) => 1 - t.F(Tune.WearPacePenalty) * Wear;

        /// <param name="seed">Any number; it is scrambled first so small ids do not start with tiny draws.</param>
        internal VehicleUpkeep(uint seed)
        {
            uint x = seed + 0x9E3779B9u;
            x = (x ^ (x >> 16)) * 0x7FEB352Du; x = (x ^ (x >> 15)) * 0x846CA68Bu; x ^= x >> 16;
            Seed = x == 0 ? 0x9E3779B9u : x;
        }

        /// <summary>Restores a generator exactly as saved.</summary>
        internal static VehicleUpkeep FromState(uint state) { var u = new VehicleUpkeep(0); u.Seed = state == 0 ? 0x9E3779B9u : state; return u; }

        private float NextUnit()
        {
            // xorshift32: small, fast, and exactly reproducible across runs and saves.
            Seed ^= Seed << 13; Seed ^= Seed >> 17; Seed ^= Seed << 5;
            return (Seed >> 8) / 16777216f;
        }

        /// <summary>
        /// One working step of <paramref name="seconds"/> under a strain factor (1 normal; rough ground and loads more).
        /// Returns false while broken (the step is spent waiting for the mechanic); the repair cost is returned when it ends.
        /// </summary>
        internal bool Operate(double seconds, float strain, out double repairCost, GameTuning t = null)
        {
            t ??= GameTuning.Default;
            repairCost = 0;
            if (Broken)
            {
                RepairLeft -= (float)seconds;
                if (RepairLeft > 0) return false;
                repairCost = t[Tune.RepairBaseCost] + t[Tune.RepairWearCost] * Wear;
                Broken = false; RepairLeft = 0; Wear = Math.Max(0, Wear - t.F(Tune.FieldRepairRelief));
                return true;
            }
            Wear = Math.Min(1, Wear + (float)(t[Tune.WearPerSecond] * strain * seconds));
            // Hazard rises steeply with wear: a new vehicle almost never stops, a worn-out one every few minutes.
            double hazard = t[Tune.BreakdownHazard] * Wear * Wear * strain;
            if (NextUnit() < hazard * seconds)
            {
                Broken = true; RepairLeft = t.F(Tune.RepairSeconds); Breakdowns++;
                return false;
            }
            return true;
        }

        /// <summary>Servicing at the depot: wear comes off over time. Returns the cost of this step.</summary>
        internal double Service(double seconds, GameTuning t = null)
        {
            t ??= GameTuning.Default;
            if (Broken || Wear <= 0) return 0;
            float relief = Math.Min(Wear, (float)(t[Tune.ServicePerSecond] * seconds));
            Wear -= relief;
            return relief * t[Tune.ServiceCostPerWear];
        }
    }
}
