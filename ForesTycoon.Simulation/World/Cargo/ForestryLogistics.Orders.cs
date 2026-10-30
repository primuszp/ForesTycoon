using System;

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
            var stack = StackAt(tile);
            if (stack == null) return "Ez nem sarang. A processzort egy sarang helyére küldd: oda hordja a fát.";
            if (PreviewMachinePath(machine.Tile, tile, machine) == null) return "A processzor nem jut el ide: nincs engedélyezett út vagy nyom.";
            return MachineOrderRejection(machine, stack, hasTimber: NearestTimber(machine, tile) != null);
        }

        internal string CheckSource(int tile, bool truck, int vehicleTile, ForestMachine machine = null)
        {
            var stack = StackAt(tile);
            if (stack == null) return "A forrás egy sarang legyen.";
            if (truck && terrain.FindNetworkDocks(new[] { tile }).Count == 0) return "A teherautó csak úthoz vagy nyomhoz közeli sarangnál rakodhat.";
            if (!truck && PreviewMachinePath(vehicleTile, tile, machine) == null) return "A forwarder nem jut el ehhez a saranghoz.";
            return null;
        }

        internal string CheckDestination(int source, int tile, bool truck, ForestMachine machine = null, FleetTruck unit = null)
        {
            if (tile == source) return "A cél nem lehet maga a forrás.";
            bool mill = MillAt(tile) != null;
            if (!mill && StackAt(tile) == null && !CanPlaceStack(tile)) return "Ide nem rakható le fa: sarang csak nyom vagy út mellé, szabad talajra kerülhet.";
            if (truck) {
                if (unit != null && unit.Phase != TruckPhase.Parked) return "A rönkszállító épp úton van: előbb hívd haza.";
                if (PreviewTruckRoute(source, tile, unit) == null) return "Nincs engedélyezett út vagy nyom a forrástól idáig.";
                return TruckOrderRejection(unit, StackAt(source), mill);
            }
            if (PreviewMachinePath(source, tile, machine) == null) return "A forwarder nem jut el a forrástól idáig.";
            return MachineOrderRejection(machine, StackAt(source), mill);
        }
    }
}
