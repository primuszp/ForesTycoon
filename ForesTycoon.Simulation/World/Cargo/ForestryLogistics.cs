using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Mathematics;
namespace ForesTycoon
{
    internal sealed class HarvestSite
    {
        internal int[] Tiles;
        // Null designates the whole stand. A thinning mark follows the tree ID through growth and regeneration.
        internal HashSet<ulong> MarkedTrees;
        internal float InitialVolume;
    }
    internal sealed class Sawmill
    {
        internal int TileId;
        internal int[] Footprint;
        internal Vector3 Position;
        // Lifetime totals and many small processing debits must retain double precision.
        internal double Received, Stock, Processed;
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
        internal ForestSystem Forest => forest;
        internal string Status="Jelölj ki kitermelési területet, és helyezz el egy fűrészmalmot.";
        internal ForestryLogistics(TerrainMap terrain,ForestSystem forest){
            this.terrain=terrain;this.forest=forest;
            terrain.GroundReserved = id => StackAt(id) != null;
            terrain.VegetationOccupied = forest.HasPhysicalObjects;
            terrain.MobileOccupied = id => Machines.Exists(m => m.Path is { Length: > 0 } && m.Tile == id)
                || Vehicles?.Vehicles.Any(v => { v.GetSegment(v.RoutePosition, out int from, out int to, out float fraction); return (fraction < .5f ? from : to) == id; }) == true;
        }
        /// <summary>Standing timber of a site still to fell, m³.</summary>
        internal float Volume(HarvestSite site){float result=0;foreach(int id in site.Tiles)result+=forest.AvailableTimber(id, site.MarkedTrees);return result;}
        internal float DesignatedTimber(int tile) => SiteAt(tile) is { } site ? forest.AvailableTimber(tile, site.MarkedTrees) : 0;

        /// <summary>Timber not yet at a mill: standing in the sites, in stacks and on the machines, m³.</summary>
        internal float Remaining {get {float sum=0;foreach(var site in Sites)sum+=Volume(site);foreach(var s in Stacks)sum+=s.Volume;foreach(var m in Machines)sum+=m.Cargo+m.LogTransferVolume;foreach(var t in Trucks)sum+=t.Vehicle?.CargoAmount??0;return sum;}}
        /// <summary>Last complete inventory publication. Presentation reads this without traversing the world.</summary>
        internal float PublishedRemaining { get; private set; }
        internal IEnumerable<int> PublishInventoryWork()
        {
            var result = new WorkResult<float>();
            foreach (int unit in RemainingWork(result)) yield return unit;
            PublishedRemaining = result.Value;
        }
        internal bool ContainsTile(int id){foreach(var site in Sites)if(Array.IndexOf(site.Tiles,id)>=0)return true;return false;}
        internal int Designate(ReadOnlySpan<int> ids)
        {
            var result = new WorkResult<int>(); SimulationWork.Complete(DesignateWork(ids.ToArray(), result)); return result.Value;
        }
        internal IEnumerable<int> DesignateWork(int[] ids, WorkResult<int> result, int thinningPercent = 0, WorkResult<ThinningPrescription> prescription = null)
        {
            var occupied = new HashSet<int>();
            // A completed thinning can be prescribed again. Keep worksite access until the fleet has left.
            for (int i = Sites.Count - 1; i >= 0; i--) {
                var site = Sites[i];
                var releasable = new WorkResult<bool>();
                foreach (int unit in CanReleaseSiteWork(site, releasable)) yield return unit;
                if (releasable.Value) {
                    Sites.RemoveAt(i);
                    continue;
                }
                foreach (int tile in site.Tiles) { occupied.Add(tile); yield return 1; }
            }
            if (thinningPercent != 0) {
                prescription ??= new();
                prescription.Value = new(thinningPercent, 0, 0, 0, 0);
            }
            var marked = thinningPercent == 0 ? null : new HashSet<ulong>();
            var selected = new List<int>(); float volume = 0;
            foreach (int id in ids) {
                yield return 1;
                if (!occupied.Add(id)) continue;
                if (!CanDesignate(id, out float timber)) continue;
                if (marked != null) {
                    int previous = marked.Count;
                    foreach (int unit in MarkThinningWork(id, thinningPercent, marked, prescription)) yield return unit;
                    if (marked.Count == previous) continue;
                    timber = forest.AvailableTimber(id, marked);
                }
                selected.Add(id); volume += timber;
            }
            if (selected.Count > 0) Sites.Add(new HarvestSite { Tiles = selected.ToArray(), InitialVolume = volume, MarkedTrees = marked });
            Status = selected.Count > 0 ? $"{(marked == null ? "Kitermelés" : "Gyérítés")} kijelölve: {selected.Count} csempe, {volume:F1} m³." : "Nincs új kitermelhető erdő a kijelölésben.";
            if (prescription != null && prescription.Value.SurveyedTrees > 0)
                Status = selected.Count == 0
                    ? $"A megadott keretbe egy vágható törzs sem fért bele. {prescription.Value.Description}"
                    : $"{Status} {prescription.Value.Description}";
            if (selected.Count > 0) Status += " Kösd nyommal az úthálózathoz, jelölj ki mellé sarangot, és küldd ki a processzort.";
            result.Value = selected.Count;
        }
        private bool CanDesignate(int tile, out float timber)
        {
            timber = forest.AvailableTimber(tile);
            forest.TryGetStand(tile, out var stand);
            return timber > 0 && Gameplay.Evaluate("forest.designateAllow", timber, stand.AgeYears, stand.Maturity) >= .5;
        }

        private IEnumerable<int> CanReleaseSiteWork(HarvestSite site, WorkResult<bool> result)
        {
            result.Value = false;
            if (site.MarkedTrees == null) yield break;
            foreach (int tile in site.Tiles) {
                yield return 1;
                if (forest.AvailableTimber(tile, site.MarkedTrees) > .001f) yield break;
            }
            result.Value = !Machines.Exists(m => m.Site == site || m.Path is { Length: > 0 } && Array.IndexOf(site.Tiles, m.Tile) >= 0);
        }

        internal (ThinningPrescription Survey, HashSet<ulong> Marks, bool Occupied) PreviewThinning(int tile, int percent)
        {
            if (percent is < 1 or > 99) throw new ArgumentOutOfRangeException(nameof(percent));
            var marks = new HashSet<ulong>();
            var survey = new WorkResult<ThinningPrescription> { Value = new(percent, 0, 0, 0, 0) };
            if (SiteAt(tile) is { } site) {
                var releasable = new WorkResult<bool>();
                SimulationWork.Complete(CanReleaseSiteWork(site, releasable));
                if (!releasable.Value) return (survey.Value, marks, true);
            }
            if (CanDesignate(tile, out _)) SimulationWork.Complete(MarkThinningWork(tile, percent, marks, survey));
            return (survey.Value, marks, false);
        }

        private IEnumerable<int> MarkThinningWork(int tile, int percent, HashSet<ulong> marked, WorkResult<ThinningPrescription> prescription)
        {
            if (!forest.IndividualTrees.TryGet(tile, out var patch) || patch.Count == 0) yield break;
            // pi/4 cancels when comparing a fraction of basal area. Rank at designation time.
            double sum = 0;
            var candidates = new List<(double Area, ulong Id)>(patch.Count);
            for (int i = 0; i < patch.Count; i++) {
                yield return 1;
                var tree = patch.Trees[i];
                var size = tree.At(forest.ForestYear);
                double area = (double)size.Diameter * size.Diameter;
                sum += area;
                if (patch.Count >= 2 && area > 0 && Gameplay.Evaluate("forest.fellAllow", tree.Age(forest.ForestYear),
                    size.Height, size.Diameter, (int)tree.Species) >= .5) candidates.Add((area, tree.Id));
            }
            candidates.Sort();
            double remaining = sum * percent / 100;
            double markedArea = 0;
            int markedCount = 0;
            foreach (var tree in candidates) {
                yield return 1;
                if (tree.Area > remaining) break;
                remaining -= tree.Area;
                marked.Add(tree.Id);
                markedArea += tree.Area;
                markedCount++;
            }
            var prior = prescription.Value;
            prescription.Value = new(percent, prior.SurveyedTrees + patch.Count, prior.MarkedTrees + markedCount,
                prior.StandingBasalArea + sum * (Math.PI / 4), prior.MarkedBasalArea + markedArea * (Math.PI / 4));
        }

        internal IEnumerable<int> RemainingWork(WorkResult<float> result)
        {
            float sum = 0;
            foreach (var site in Sites) {
                float volume = 0;
                foreach (int id in site.Tiles) { volume += forest.AvailableTimber(id, site.MarkedTrees); yield return 1; }
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
                if (StackAt(tile) != null) { Status = "A területet sarang foglalja. Előbb szállítsd el a fát és szüntesd meg a sarangot."; yield break; }
                if (forest.HasPhysicalObjects(tile)) { Status = "A malom helyét fa, tuskó vagy faanyag foglalja."; yield break; }
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
                if (!AtDock(vehicle, terrain.FindNetworkDocks(new[] { truck.Source.Tile }))) return 0;
                var (taken,value)=truck.Source.Take(requested);truck.CargoValue+=value;return taken;
            }
            return 0;
        }
        internal void Deliver(Vehicle vehicle,float amount)
        {
            var truck=TruckOf(vehicle);
            if(truck!=null){
                if (!AtDock(vehicle, DestinationDocks(truck.Destination))) {
                    vehicle.Load(amount); vehicle.RouteBlocked = true; return;
                }
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
                double cut = Math.Min(mill.Stock, rules.Evaluate(Behaviors, "mill.process", "mill", (ulong)mill.TileId,
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
