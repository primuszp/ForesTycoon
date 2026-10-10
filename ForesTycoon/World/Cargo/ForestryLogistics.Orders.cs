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

        internal string CheckProcessorStack(ForestMachine machine, int tile)
        {
            var stack = StackAt(tile);
            if (stack == null) return "Ez nem sarang. A processzort egy sarang helyére küldd: oda hordja a fát.";
            if (PreviewMachinePath(machine.Tile, tile, machine) == null) return "A processzor nem jut el ide: nincs engedélyezett út vagy nyom.";
            if (NearestTimber(machine, tile) == null) return "Innen nem érhető el kijelölt fa. Jelölj ki kitermelést, és kösd nyommal.";
            return null;
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
            if (truck) return PreviewTruckRoute(source, tile, unit) == null ? "Nincs engedélyezett út vagy nyom a forrástól idáig." : null;
            return PreviewMachinePath(source, tile, machine) == null ? "A forwarder nem jut el a forrástól idáig." : null;
        }
    }
}
