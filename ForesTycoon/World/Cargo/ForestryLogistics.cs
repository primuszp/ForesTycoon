using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
namespace ForesTycoon
{
    internal sealed class HarvestSite
    {
        internal int[] Tiles;
        internal float InitialVolume;
    }
    internal sealed class Sawmill
    {
        internal int TileId;
        internal int[] Footprint;
        internal Vector3 Position;
        internal float Received,Stock,Processed;
    }
    internal sealed partial class ForestryLogistics
    {
        private readonly LogisticsRuleEvaluator rules = new();
        internal Action<TrafficPassage> Passage;
        internal IBehaviorPolicy Behaviors { get; set; }
        internal GameplayProgram Gameplay { get; set; } = GameplayProgram.Default;
        internal CompiledBehaviorControllers Controllers { get; set; }
        private readonly TerrainMap terrain;
        private readonly ForestSystem forest;
        internal readonly List<HarvestSite> Sites=new();
        internal readonly List<Sawmill> Mills=new();
        internal string Status="Jelölj ki kitermelési területet, és helyezz el egy fűrészmalmot.";
        internal ForestryLogistics(TerrainMap terrain,ForestSystem forest){this.terrain=terrain;this.forest=forest;}
        /// <summary>Standing timber of a site still to fell, m³.</summary>
        internal float Volume(HarvestSite site){float result=0;foreach(int id in site.Tiles)result+=forest.AvailableTimber(id);return result;}
        /// <summary>Timber not yet at a mill: standing in the sites, in stacks and on the machines, m³.</summary>
        internal float Remaining {get {float sum=0;foreach(var site in Sites)sum+=Volume(site);foreach(var s in Stacks)sum+=s.Volume;foreach(var m in Machines)sum+=m.Cargo+m.LogTransferVolume;foreach(var t in Trucks)sum+=t.Vehicle?.CargoAmount??0;return sum;}}
        internal bool ContainsTile(int id){foreach(var site in Sites)if(Array.IndexOf(site.Tiles,id)>=0)return true;return false;}
        internal int Designate(ReadOnlySpan<int> ids)
        {
            var result = new WorkResult<int>(); SimulationWork.Complete(DesignateWork(ids.ToArray(), result)); return result.Value;
        }
        internal IEnumerable<int> DesignateWork(int[] ids, WorkResult<int> result)
        {
            var occupied = new HashSet<int>();
            foreach (var site in Sites) foreach (int id in site.Tiles) { occupied.Add(id); yield return 1; }
            var selected = new List<int>(); float volume = 0;
            foreach (int id in ids) {
                yield return 1;
                float timber = forest.AvailableTimber(id);
                forest.TryGetStand(id, out var stand);
                if (!occupied.Contains(id) && timber > 0 && Gameplay.Evaluate("forest.designateAllow", timber, stand.AgeYears, stand.Maturity) >= .5) { selected.Add(id); volume += timber; }
            }
            if (selected.Count > 0) Sites.Add(new HarvestSite { Tiles = selected.ToArray(), InitialVolume = volume });
            Status = selected.Count > 0 ? $"Kitermelés kijelölve: {selected.Count} csempe, {volume:F1} m³." : "Nincs új kitermelhető erdő a kijelölésben.";
            if (selected.Count > 0) Status += " Kösd nyommal az úthálózathoz, jelölj ki mellé sarangot, és küldd ki a processzort.";
            result.Value = selected.Count;
        }
        internal IEnumerable<int> RemainingWork(WorkResult<float> result)
        {
            float sum = 0;
            foreach (var site in Sites) {
                float volume = 0;
                foreach (int id in site.Tiles) { volume += forest.AvailableTimber(id); yield return 1; }
                sum += volume;
            }
            foreach (var stack in Stacks) { sum += stack.Volume; yield return 1; }
            foreach (var machine in Machines) { sum += machine.Cargo + machine.LogTransferVolume; yield return 1; }
            foreach (var truck in Trucks) { sum += truck.Vehicle?.CargoAmount ?? 0; yield return 1; }
            result.Value = sum;
        }

        internal bool PlaceMill(int id)
        {
            var result = new WorkResult<bool>(); SimulationWork.Complete(PlaceMillWork(id, result)); return result.Value;
        }
        internal IEnumerable<int> PlaceMillWork(int id, WorkResult<bool> result)
        {
            result.Value = false;
            if (Gameplay.Evaluate("mill.placeAllow", Mills.Count) < .5) { Status = "A projekt szabálya nem engedélyezi a malom építését."; yield break; }
            if (!terrain.TryGetSawmillFootprint(id, out var footprint, out var position)) {
                Status = "A malomhoz 2×2 sík, száraz, üres csempe szükséges."; yield break;
            }
            foreach (int tile in footprint) {
                yield return 1;
                if (forest.TryGetStand(tile, out _) || ContainsTile(tile)) { Status = "A malom helyét erdő vagy kitermelés foglalja."; yield break; }
                foreach (var mill in Mills) {
                    yield return 1;
                    if (Array.IndexOf(mill.Footprint, tile) >= 0) { Status = "Itt már áll egy malom."; yield break; }
                }
            }
            Mills.Add(new Sawmill { TileId = id, Footprint = footprint, Position = position });
            terrain.SetBuildingFootprint(footprint);
            Status = "Fűrészmalom elhelyezve. Kösd úttal a kijelölt erdő mellé, majd indíts teherautót.";
            result.Value = true;
        }
        internal float Load(Vehicle vehicle,float requested)
        {
            var truck=TruckOf(vehicle);
            if(truck!=null){
                var (taken,value)=truck.Source.Take(requested);truck.CargoValue+=value;return taken;
            }
            return 0;
        }
        internal void Deliver(Vehicle vehicle,float amount)
        {
            var truck=TruckOf(vehicle);
            if(truck!=null){
                // The share of the value goes with the share of the load (the vehicle already handed this amount over).
                float carried=vehicle.CargoAmount+amount;
                double value=carried>1e-6f?truck.CargoValue*amount/carried:truck.CargoValue;
                truck.CargoValue-=value;if(vehicle.CargoAmount<=1e-6f)truck.CargoValue=0;
                Receive(truck.Destination,amount,value);return;
            }
            foreach(var mill in Mills)if(mill.TileId==vehicle.SawmillTileId){mill.Received+=amount;mill.Stock+=amount;Vehicles?.ReceiveAtMill(amount);return;}
            throw new InvalidOperationException("Missing sawmill destination.");
        }
        internal void Update(double seconds) => SimulationWork.Complete(UpdateWork(seconds));
        internal IEnumerable<int> UpdateWork(double seconds){
            foreach(var mill in Mills){yield return 1;
                float cut = Math.Min(mill.Stock, (float)rules.Evaluate(Behaviors, "mill.process", "mill", (ulong)mill.TileId,
                    new(StepSeconds: seconds, MillStock: mill.Stock, MillProcessed: mill.Processed)));
                mill.Stock-=cut;mill.Processed+=cut;}
            var supply = new Dictionary<int, List<SupplyEdge>>();
            foreach (int unit in BuildSupplyIndexWork(supply)) yield return unit;
            foreach(int unit in UpdateMachinesWork(seconds, supply)) yield return unit;
            if(Vehicles!=null) foreach(int unit in UpdateTrucksWork(seconds, supply)) yield return unit;
        }
        internal bool RouteConnected(Vehicle vehicle)
        {
            for(int i=1;i<vehicle.Route.Length;i++)
                if(!terrain.AreNetworkNeighbours(vehicle.Route[i-1],vehicle.Route[i]))return false;
            return true;
        }
    }
}
