using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    /// <summary>
    /// A stack (sarang) of round logs on a tile the player chose: where the processor carries what it fells, and the
    /// source or destination of forwarder and truck orders. It knows its volume and the value of the wood in it.
    /// </summary>
    internal sealed class TimberStack
    {
        internal int Id;
        internal int Tile;
        internal float Volume;
        /// <summary>Sawmill value of the wood in the stack, thousand forints.</summary>
        internal double Value;
        internal double UnitValue => Volume > 1e-6f ? Value / Volume : 0;

        internal void Add(float volume, double value) { Volume += volume; Value += value; }

        /// <summary>Takes up to <paramref name="request"/> m³ with its share of the value.</summary>
        internal (float Volume, double Value) Take(float request)
        {
            float take = Math.Min(request, Volume);
            if (take <= 0) return (0, 0);
            double value = take >= Volume - 1e-6f ? Value : Value * take / Volume;
            Volume -= take; Value -= value;
            if (Volume <= 1e-6f) { Volume = 0; Value = 0; }
            return (take, value);
        }
    }

    internal sealed partial class ForestryLogistics
    {
        internal readonly List<TimberStack> Stacks = new();
        private int nextStackId = 1;
        /// <summary>Money earned at the sawmills, thousand forints.</summary>
        internal double Income;
        /// <summary>Running costs of the fleet (fuel, repairs, service), thousand forints.</summary>
        internal double RunningCosts;
        /// <summary>Diesel price, thousand forints per litre.</summary>
        internal const double DieselPrice = 0.62;

        /// <summary>Sawmill price of round wood by species, thousand forints per m³.</summary>
        internal static double TimberPrice(ForestSpecies species) => species switch
        {
            ForestSpecies.Oak => 55, ForestSpecies.SessileOak => 50, ForestSpecies.TurkeyOak => 28,
            ForestSpecies.Ash => 40, ForestSpecies.Maple => 38, ForestSpecies.Beech => 35,
            ForestSpecies.Larch => 34, ForestSpecies.Spruce => 32, ForestSpecies.Fir => 30, ForestSpecies.Pine => 28,
            ForestSpecies.Birch => 22,
            _ => 8
        };

        internal TimberStack StackAt(int tile) => Stacks.Find(s => s.Tile == tile);

        /// <summary>A stack stands on open ground beside the network (or on a trail): not on a road, building, water or felling.</summary>
        internal bool CanPlaceStack(int tile)
        {
            if (!terrain.IsValidTileId(tile) || terrain.IsRoadTile(tile) || terrain.IsBuildingTile(tile) || ContainsTile(tile)) return false;
            if (!terrain.IsSkidTrail(tile) && !terrain.CanCarrySkidTrail(tile)) return false;
            return terrain.IsSkidTrail(tile) || terrain.FindNetworkDocks(new[] { tile }).Count > 0;
        }

        /// <summary>Marks a stack site (Sarang tool). The processor carries its logs to the nearest one.</summary>
        internal bool PlaceStack(int tile)
        {
            if (StackAt(tile) != null) { Status = "Itt már van sarang."; return false; }
            if (!CanPlaceStack(tile)) { Status = "A sarang nyom vagy út mellé, szabad talajra kerülhet."; return false; }
            Stacks.Add(new TimberStack { Id = nextStackId++, Tile = tile });
            Status = "Sarang helye kijelölve.";
            return true;
        }

        /// <summary>Removes an empty stack site nobody is working with.</summary>
        internal bool RemoveStack(int tile)
        {
            var stack = StackAt(tile);
            if (stack == null) return false;
            if (stack.Volume > 0.01f) { Status = "Teli sarangot nem lehet törölni: előbb szállítsd el a fát."; return false; }
            if (Machines.Exists(m => m.Source == stack || m.Target == stack) || Trucks.Exists(t => t.Source == stack || t.Target == stack))
            { Status = "Ezzel a saranggal egy jármű dolgozik."; return false; }
            Stacks.Remove(stack);
            return true;
        }

        /// <summary>Wood arriving at a destination tile: at a sawmill it is sold, elsewhere it goes on (or starts) the stack there.</summary>
        private void Receive(int tile, float volume, double value)
        {
            if (volume <= 0) return;
            foreach (var mill in Mills)
                if (Array.IndexOf(mill.Footprint, tile) >= 0 || mill.TileId == tile)
                {
                    mill.Received += volume; mill.Stock += volume; Income += value;
                    return;
                }
            var stack = StackAt(tile);
            if (stack == null) { stack = new TimberStack { Id = nextStackId++, Tile = tile }; Stacks.Add(stack); }
            stack.Add(volume, value);
        }

        internal Sawmill MillAt(int tile) => Mills.Find(m => Array.IndexOf(m.Footprint, tile) >= 0);

        private void Burn(double litres) { if (litres > 0) RunningCosts += litres * Tuning[Tune.DieselPrice]; }
    }
}
