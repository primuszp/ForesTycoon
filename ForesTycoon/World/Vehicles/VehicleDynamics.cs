using System;

namespace ForesTycoon
{
    /// <summary>Running surface of a road: sets the rolling resistance.</summary>
    internal enum RoadSurface : byte { Asphalt, Gravel, Dirt }

    /// <summary>A timber truck's physical data (SI units).</summary>
    /// <param name="EmptyMass">Tractor and trailer, kg.</param>
    /// <param name="Power">Engine power at the wheels' disposal, W.</param>
    /// <param name="MaxTractiveForce">Grip-limited pulling force, N.</param>
    /// <param name="DragArea">Drag coefficient × frontal area, m².</param>
    /// <param name="TimberDensity">Fresh round wood, kg per cargo m³.</param>
    /// <param name="FuelPerKilowattHour">Diesel per engine kWh (brake-specific consumption), litres.</param>
    /// <param name="IdleFuelPerHour">Diesel while running without load, litres per hour.</param>
    internal sealed record TruckSpec(float EmptyMass = 16000, float Power = 330000, float MaxTractiveForce = 170000,
        float DragArea = 7.5f, float Drivetrain = 0.9f, float TimberDensity = 800, float FuelPerKilowattHour = 0.235f,
        float IdleFuelPerHour = 2.5f, float Braking = 2.5f)
    {
        internal static TruckSpec Default { get; } = new();
        internal float RollingAsphalt { get; init; } = VehicleDynamics.DefaultRollingAsphalt;
        internal float RollingGravel { get; init; } = VehicleDynamics.DefaultRollingGravel;
        internal float RollingDirt { get; init; } = VehicleDynamics.DefaultRollingDirt;
        internal float RoughnessRolling { get; init; } = VehicleDynamics.DefaultRoughnessRolling;
    }

    /// <summary>
    /// Longitudinal truck dynamics: the engine's tractive force (power-limited, grip-capped) against rolling,
    /// grade and air resistance. A loaded truck therefore slows on a climb until its power just holds the
    /// resistance, and its fuel use follows the work its engine really does.
    /// </summary>
    internal static class VehicleDynamics
    {
        internal const float Gravity = 9.81f, AirDensity = 1.2f;

        internal const float DefaultRollingAsphalt = 0.008f, DefaultRollingGravel = 0.02f, DefaultRollingDirt = 0.04f, DefaultRoughnessRolling = 1.5f;

        internal static float RollingCoefficient(RoadSurface surface, float roughness) => RollingCoefficient(TruckSpec.Default, surface, roughness);
        internal static float RollingCoefficient(TruckSpec spec, RoadSurface surface, float roughness) =>
            surface switch { RoadSurface.Asphalt => spec.RollingAsphalt, RoadSurface.Gravel => spec.RollingGravel, _ => spec.RollingDirt }
            * (1 + spec.RoughnessRolling * Math.Clamp(roughness, 0, 1));

        internal static float Mass(TruckSpec spec, float cargoCubicMetres) => spec.EmptyMass + Math.Max(0, cargoCubicMetres) * spec.TimberDensity;

        /// <summary>Total driving resistance at speed <paramref name="speed"/> (m/s) on a grade (rise/run, + uphill), N.</summary>
        internal static float Resistance(TruckSpec spec, float mass, float speed, float grade, RoadSurface surface, float roughness)
        {
            float angle = MathF.Atan(grade);
            return RollingCoefficient(spec, surface, roughness) * mass * Gravity * MathF.Cos(angle)
                + mass * Gravity * MathF.Sin(angle)
                + 0.5f * AirDensity * spec.DragArea * speed * speed;
        }

        /// <summary>Largest pulling force at this speed: power-limited, capped by grip, N.</summary>
        internal static float AvailableForce(TruckSpec spec, float speed) =>
            Math.Min(spec.MaxTractiveForce, spec.Power * spec.Drivetrain / Math.Max(speed, 1f));

        /// <summary>Steady speed where full power just balances the resistance (bisection), m/s.</summary>
        internal static float BalanceSpeed(TruckSpec spec, float mass, float grade, RoadSurface surface, float roughness)
        {
            float lo = 0, hi = 60;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (AvailableForce(spec, mid) > Resistance(spec, mass, mid, grade, surface, roughness)) lo = mid; else hi = mid;
            }
            return lo;
        }

        /// <summary>One integration step toward <paramref name="target"/> speed; returns the new speed and the fuel burnt.</summary>
        internal static (float Speed, float Fuel) Step(TruckSpec spec, float mass, float speed, float target, float grade,
            RoadSurface surface, float roughness, float dt)
        {
            float resistance = Resistance(spec, mass, speed, grade, surface, roughness);
            float drive, next;
            if (target > speed)
            {
                // Full throttle: on a steep climb the net force is negative and the truck slows down.
                drive = AvailableForce(spec, speed);
                next = Math.Max(0, speed + (drive - resistance) / mass * dt);
                if (next > target) { next = target; drive = Math.Clamp(resistance + mass * (next - speed) / dt, 0, drive); }
            }
            else
            {
                // Hold or brake: the engine only covers what the resistance takes; brakes do the rest.
                next = Math.Max(target, speed - spec.Braking * dt);
                float needed = resistance + mass * (next - speed) / dt;
                drive = Math.Clamp(needed, 0, AvailableForce(spec, speed));
            }
            float power = drive * (speed + next) * 0.5f / spec.Drivetrain;
            float hours = dt / 3600f;
            float fuel = power / 1000f * hours * spec.FuelPerKilowattHour + spec.IdleFuelPerHour * hours;
            return (next, fuel);
        }
    }
}
