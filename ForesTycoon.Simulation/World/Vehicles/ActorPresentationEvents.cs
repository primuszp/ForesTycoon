using OpenTK.Mathematics;
namespace ForesTycoon;
internal readonly record struct ActorSoundSample(long Id, string Event, Vector3 Position);
internal static class ActorPresentationEvents
{
    internal static string Machine(ForestMachineKind kind, ForestMachineState state, bool broken)
    {
        if (broken || state == ForestMachineState.Parked) return null;
        return (kind, state) switch {
            (ForestMachineKind.Harvester, ForestMachineState.Driving) => "harvester.driving",
            (ForestMachineKind.Harvester, ForestMachineState.Felling) => "harvester.felling",
            (ForestMachineKind.Harvester, ForestMachineState.Unloading) => "harvester.unloading",
            (ForestMachineKind.Forwarder, ForestMachineState.Driving) => "forwarder.driving",
            (ForestMachineKind.Forwarder, ForestMachineState.Loading) => "forwarder.loading",
            (ForestMachineKind.Forwarder, ForestMachineState.Unloading) => "forwarder.unloading", _ => null
        };
    }
    internal static string Truck(Vehicle v) => v.Broken || v.TransitArrived ? null : v.TransportState switch {
        VehicleTransportState.Loading => "truck.loading", VehicleTransportState.Unloading => "truck.unloading",
        _ => v.CurrentSpeed > .001 ? "truck.driving" : null
    };
    internal static ActorSoundSample? Sample(ForestMachine m, TerrainMap map)
    {
        string key = Machine(m.Kind, m.State, m.Upkeep.Broken);
        if (key == null || !map.TryGetTileCenter(m.Tile, out var point)) return null;
        if (m.Kinematics != null) { var axle = m.Kinematics.Units[0].Axle; point.X = (float)axle.X * Engine.WorldScale.MetresToWorld; point.Y = (float)axle.Y * Engine.WorldScale.MetresToWorld; }
        return new(m.Id, key, point);
    }
    internal static ActorSoundSample? Sample(Vehicle v, TerrainMap map)
    {
        string key = Truck(v); if (key == null) return null;
        v.GetSegment(v.RoutePosition, out int from, out int to, out float amount);
        if (!map.TryGetTileCenter(from, out var a) || !map.TryGetTileCenter(to, out var b)) return null;
        var point = Vector3.Lerp(a, b, amount);
        if (v.Kinematics != null) { var axle = v.Kinematics.Units[0].Axle; point.X = (float)axle.X * Engine.WorldScale.MetresToWorld; point.Y = (float)axle.Y * Engine.WorldScale.MetresToWorld; }
        return new((1L << 32) + v.Id, key, point);
    }
}
