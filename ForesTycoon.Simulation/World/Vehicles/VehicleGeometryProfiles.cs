using System;
using System.Collections.Generic;

namespace ForesTycoon;

internal static class VehicleGeometryProfiles
{
    private const double Degrees = Math.PI / 180;
    internal static RigProfile Truck(GameTuning tuning)
    {
        double V(Tune key) => tuning[key];
        var axles = new List<RigAxle>(); var trailers = new List<RigTrailer>();
        double track = V(Tune.TruckTrack), radius = V(Tune.TruckWheelRadius);
        Group(axles, "truck.rear", 0, 0, (int)Math.Round(V(Tune.TruckRearAxles)), V(Tune.TruckAxleSpacing), track, radius);
        axles.Add(new("truck.front", 0, V(Tune.TruckWheelbase), track, radius, true));
        int layout = (int)Math.Round(V(Tune.TruckTrailerLayout));
        if (layout != 0) {
            int unit = 1;
            double hitch = V(Tune.TruckHitchOffset), max = V(Tune.TruckTrailerMaxAngle) * Degrees;
            if (layout == 2) {
                // A turntable drawbar trailer is two passive constraints, not a long semi-trailer.
                trailers.Add(new(hitch, V(Tune.TruckDrawbarLength), max));
                axles.Add(new("trailer.turntable", unit++, 0, track, radius)); hitch = 0;
            }
            trailers.Add(new(hitch, V(Tune.TruckTrailerLength), max));
            Group(axles, "trailer.rear", unit, 0, (int)Math.Round(V(Tune.TruckTrailerAxles)), V(Tune.TruckTrailerAxleSpacing), track, radius);
        }
        return new(SteeringGeometry.Axle, V(Tune.TruckWheelbase), 0, V(Tune.TruckMaxSteer) * Degrees,
            V(Tune.TruckSteerRate) * Degrees, axles.ToArray(), trailers.ToArray());
    }

    internal static RigProfile Machine(GameTuning tuning, ForestMachineKind kind)
    {
        bool forwarder = kind == ForestMachineKind.Forwarder;
        double V(Tune f, Tune p) => tuning[forwarder ? f : p];
        var axles = new List<RigAxle>();
        double track = V(Tune.ForwarderTrack, Tune.ProcessorTrack), radius = V(Tune.ForwarderWheelRadius, Tune.ProcessorWheelRadius);
        double spacing = V(Tune.ForwarderAxleSpacing, Tune.ProcessorAxleSpacing);
        if (!forwarder && tuning[Tune.ProcessorSteeringModel] < .5) {
            double length = tuning[Tune.ProcessorWheelbase];
            Group(axles, "rear", 0, 0, (int)Math.Round(tuning[Tune.ProcessorRearAxles]), spacing, track, radius);
            int count = Math.Clamp((int)Math.Round(tuning[Tune.ProcessorFrontAxles]), 1, 3);
            for (int i = 0; i < count; i++) axles.Add(new("front." + i, 0, length + (i - (count - 1) * .5) * spacing, track, radius, true));
            return new(SteeringGeometry.Axle, length, 0, tuning[Tune.ProcessorMaxSteer] * Degrees,
                tuning[Tune.ProcessorSteerRate] * Degrees, axles.ToArray(), []);
        }
        Group(axles, "front", 0, 0, (int)Math.Round(V(Tune.ForwarderFrontAxles, Tune.ProcessorFrontAxles)), spacing, track, radius);
        Group(axles, "rear", 1, 0, (int)Math.Round(V(Tune.ForwarderRearAxles, Tune.ProcessorRearAxles)), spacing, track, radius);
        return new(SteeringGeometry.Articulated, V(Tune.ForwarderFrontLength, Tune.ProcessorFrontLength),
            V(Tune.ForwarderRearLength, Tune.ProcessorRearLength), V(Tune.ForwarderMaxSteer, Tune.ProcessorMaxSteer) * Degrees,
            V(Tune.ForwarderSteerRate, Tune.ProcessorSteerRate) * Degrees, axles.ToArray(), []);
    }

    private static void Group(List<RigAxle> axles, string name, int unit, double centre, int count, double spacing, double track, double radius)
    {
        count = Math.Clamp(count, 1, 3);
        for (int i = 0; i < count; i++) axles.Add(new(name + "." + i, unit, centre + (i - (count - 1) * .5) * spacing, track, radius));
    }
}
