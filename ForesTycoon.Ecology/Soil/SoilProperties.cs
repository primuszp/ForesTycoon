using System;
using System.Text.Json.Serialization;

namespace ForesTycoon.Ecology
{
    // Stores and thresholds are mm per unit ground area; fluxes are mm/environment hour.
    internal readonly record struct SoilProperties(
        [property: JsonRequired] double Saturation, [property: JsonRequired] double FieldCapacity,
        [property: JsonRequired] double WiltingPoint, [property: JsonRequired] double InfiltrationPerHour,
        [property: JsonRequired] double DrainagePerHour, [property: JsonRequired] float Fertility)
    {
        internal static SoilProperties Standard => new(180, 130, 25, 12, 2, 1);

        internal void Validate()
        {
            if (!double.IsFinite(Saturation) || !double.IsFinite(FieldCapacity) || !double.IsFinite(WiltingPoint)
                || WiltingPoint < 0 || FieldCapacity <= WiltingPoint || Saturation <= FieldCapacity
                || !double.IsFinite(InfiltrationPerHour) || InfiltrationPerHour < 0
                || !double.IsFinite(DrainagePerHour) || DrainagePerHour < 0
                || !float.IsFinite(Fertility) || Fertility < 0 || Fertility > 1)
                throw new ArgumentOutOfRangeException(nameof(SoilProperties), "Invalid soil storage or flux parameters.");
        }

        internal double Availability(double water) => Math.Clamp((water - WiltingPoint) / (FieldCapacity - WiltingPoint), 0, 1);
        internal double Waterlogging(double water) => Math.Clamp((water - FieldCapacity) / (Saturation - FieldCapacity), 0, 1);
    }
}
