using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    /// <summary>
    /// Checks and route previews for giving orders on the map. Each check returns null when the pick is valid, or the
    /// reason in plain words, so the order tool can mark the tile green or red and say why.
    /// </summary>
    internal sealed partial class ForestryLogistics
    {
        /// <summary>Path a machine would drive from <paramref name="from"/> to <paramref name="to"/>, or null.</summary>
        internal int[] PreviewMachinePath(int from, int to, ForestMachine machine = null) => machine?.Kind == ForestMachineKind.Forwarder
            ? ForwarderPath(machine, from, to) : FindPath(null, from, t => t == to, to, agent: machine);

        /// <summary>Network route a truck would shuttle between a source stack and a destination, or null.</summary>
        internal int[] PreviewTruckRoute(int source, int destination, FleetTruck truck = null) => TruckRoute(truck, source, destination);
        internal IEnumerable<int> PreviewTruckRouteWork(int source, int destination, FleetTruck truck, WorkResult<int[]> result)
            => TruckRouteWork(truck, source, destination, result);
        internal IEnumerable<int> PreviewMachinePathWork(int from, int to, ForestMachine machine, WorkResult<int[]> result)
            => machine?.Kind == ForestMachineKind.Forwarder ? ForwarderPathWork(machine, from, to, result)
                : FindPathWork(null, from, t => t == to, result, to, agent: machine);

        internal ulong OrderSiteVersion { get; private set; }
        internal void InvalidateOrderPreviews() => OrderSiteVersion++;

        private string MachineOrderRejection(ForestMachine machine, TimberStack source, bool mill = false, bool hasTimber = false)
        {
            string kind = machine?.Kind == ForestMachineKind.Harvester ? "processor" : "forwarder";
            var inputs = new LogisticsInputs(MachineKind: kind == "processor" ? 0 : 1,
                MachineCargo: (machine?.Cargo ?? 0) + (machine?.LogTransferVolume ?? 0),
                MachineCapacity: machine?.Capacity ?? Tuning[Tune.ForwarderCapacity], MachineWear: machine?.Upkeep.Wear ?? 0,
                MachineHomeRequested: machine?.HomeRequested == true ? 1 : 0, SourceStock: source?.Volume ?? 0,
                OrderHasTimber: hasTimber ? 1 : 0, OrderDestinationIsMill: mill ? 1 : 0);
            return OrderRejection(kind, machine?.Id ?? 0, inputs);
        }
        private string TruckOrderRejection(FleetTruck truck, TimberStack source, bool mill) => OrderRejection("truck", truck?.Id ?? 0,
            new(MachineKind: 2, MachineCargo: truck?.Vehicle?.CargoAmount ?? 0, MachineCapacity: truck?.Vehicle?.CargoCapacity ?? 25,
                MachineWear: truck?.Upkeep.Wear ?? 0, MachineHomeRequested: truck?.HomeRequested == true ? 1 : 0,
                SourceStock: source?.Volume ?? 0, OrderDestinationIsMill: mill ? 1 : 0));
        private string OrderRejection(string kind, int id, in LogisticsInputs inputs) =>
            rules.Evaluate(Behaviors, "orders.allow", kind, (ulong)id, inputs) >= .5 ? null
                : "A munkára küldést a szabály nem engedélyezi. A meglévő megbízás és rakomány megmarad.";

        internal string CheckProcessorStack(ForestMachine machine, int tile)
        {
            var result = new WorkResult<string>(); SimulationWork.Complete(CheckProcessorStackWork(machine, tile, result)); return result.Value;
        }
        internal IEnumerable<int> CheckProcessorStackWork(ForestMachine machine, int tile, WorkResult<string> result)
        {
            var stack = StackAt(tile);
            if (stack == null) { result.Value = "Ez nem sarang. A processzort egy sarang helyére küldd: oda hordja a fát."; yield break; }
            var path = new WorkResult<int[]>();
            foreach (int unit in PreviewMachinePathWork(OrderOrigin(machine), tile, machine, path)) yield return unit;
            if (path.Value == null) { result.Value = "A processzor nem jut el ide: nincs engedélyezett út vagy nyom."; yield break; }
            foreach (int unit in FindPathWork(null, tile, t => ContainsTile(t) && forest.AvailableTimber(t) > .001f, path, agent: machine)) yield return unit;
            result.Value = MachineOrderRejection(machine, stack, hasTimber: path.Value != null);
        }

        internal string CheckSource(int tile, bool truck, int vehicleTile, ForestMachine machine = null, FleetTruck unit = null)
        {
            var result = new WorkResult<string>(); SimulationWork.Complete(CheckSourceWork(tile, truck, vehicleTile, machine, result, unit)); return result.Value;
        }
        internal IEnumerable<int> CheckSourceWork(int tile, bool truck, int vehicleTile, ForestMachine machine, WorkResult<string> result, FleetTruck unit = null)
        {
            var stack = StackAt(tile);
            if (stack == null) { result.Value = "A forrás egy sarang legyen."; yield break; }
            if (truck && terrain.FindNetworkDocks(new[] { tile }).Count == 0) { result.Value = "A teherautó csak úthoz vagy nyomhoz közeli sarangnál rakodhat."; yield break; }
            if (truck && vehicleTile >= 0) {
                if (unit != null && unit.Phase != TruckPhase.Parked) { result.Value = "A rönkszállító épp úton van: előbb hívd haza."; yield break; }
                var starts = terrain.IsNetworkTile(vehicleTile) ? new System.Collections.Generic.List<int> { vehicleTile } : DestinationDocks(vehicleTile);
                var ends = DestinationDocks(tile); bool reachable = false;
                foreach (int start in starts) {
                    var approach = new WorkResult<(int[] Path, float Cost)>();
                    foreach (int step in BestTruckPathWork(unit, start, ends, approach)) yield return step;
                    if (approach.Value.Path.Length > 0) { reachable = true; break; }
                }
                if (!reachable) result.Value = "A telephelyről nem vezet engedélyezett út vagy nyom ehhez a saranghoz.";
            }
            if (!truck) {
                var path = new WorkResult<int[]>();
                foreach (int step in ForwarderPathWork(machine, vehicleTile, tile, path)) yield return step;
                if (path.Value == null) result.Value = "A forwarder nem jut el ehhez a saranghoz.";
            }
        }

        internal string CheckDestination(int source, int tile, bool truck, ForestMachine machine = null, FleetTruck unit = null)
        {
            var result = new WorkResult<string>(); SimulationWork.Complete(CheckDestinationWork(source, tile, truck, machine, unit, result)); return result.Value;
        }
        internal IEnumerable<int> CheckDestinationWork(int source, int tile, bool truck, ForestMachine machine, FleetTruck unit, WorkResult<string> result)
        {
            if (tile == source) { result.Value = "A cél nem lehet maga a forrás."; yield break; }
            bool mill = MillAt(tile) != null;
            if (!mill && StackAt(tile) == null && !CanPlaceStack(tile)) { result.Value = "Ide nem rakható le fa: sarang csak nyom vagy út mellé, szabad talajra kerülhet."; yield break; }
            var path = new WorkResult<int[]>();
            if (truck) {
                if (unit != null && unit.Phase != TruckPhase.Parked) { result.Value = "A rönkszállító épp úton van: előbb hívd haza."; yield break; }
                foreach (int work in PreviewTruckRouteWork(source, tile, unit, path)) yield return work;
                result.Value = path.Value == null ? "Nincs engedélyezett út vagy nyom a forrástól idáig." : TruckOrderRejection(unit, StackAt(source), mill);
                yield break;
            }
            var destinationWork = machine?.Kind == ForestMachineKind.Forwarder
                ? ForwarderOrderPathWork(machine, source, tile, path) : ForwarderPathWork(machine, source, tile, path);
            foreach (int work in destinationWork) yield return work;
            result.Value = path.Value == null ? "A forwarder nem jut el a forrástól idáig." : MachineOrderRejection(machine, StackAt(source), mill);
        }
    }
}
