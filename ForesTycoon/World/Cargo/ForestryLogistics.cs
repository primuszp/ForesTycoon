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
        private readonly TerrainMap terrain;
        private readonly ForestSystem forest;
        internal readonly List<HarvestSite> Sites=new();
        internal readonly List<Sawmill> Mills=new();
        internal string Status="Jelölj ki kitermelési területet, és helyezz el egy fűrészmalmot.";
        internal ForestryLogistics(TerrainMap terrain,ForestSystem forest){this.terrain=terrain;this.forest=forest;}
        internal float Volume(HarvestSite site){float result=0;foreach(int id in site.Tiles)result+=forest.AvailableTimber(id);return result;}
        internal float Remaining {get {float sum=0;foreach(var site in Sites)sum+=Volume(site);return sum;}}
        internal bool ContainsTile(int id){foreach(var site in Sites)if(Array.IndexOf(site.Tiles,id)>=0)return true;return false;}
        internal int Designate(ReadOnlySpan<int> ids)
        {
            var selected=new List<int>();float volume=0;
            foreach(int id in ids)if(!ContainsTile(id)&&forest.AvailableTimber(id)>0){selected.Add(id);volume+=forest.AvailableTimber(id);}
            if(selected.Count>0)Sites.Add(new HarvestSite{Tiles=selected.ToArray(),InitialVolume=volume});
            Status=selected.Count>0?$"Kitermelés kijelölve: {selected.Count} csempe, {volume:F1} m³.":"Nincs új kitermelhető erdő a kijelölésben.";
            return selected.Count;
        }
        internal bool PlaceMill(int id)
        {
            if(!terrain.TryGetSawmillFootprint(id,out var footprint,out var position)){Status="A malomhoz 2×2 sík, száraz, üres csempe szükséges.";return false;}
            foreach(int tile in footprint){if(forest.TryGetStand(tile,out _)||ContainsTile(tile)){Status="A malom helyét erdő vagy kitermelés foglalja.";return false;}foreach(var mill in Mills)if(Array.IndexOf(mill.Footprint,tile)>=0){Status="Itt már áll egy malom.";return false;}}
            Mills.Add(new Sawmill{TileId=id,Footprint=footprint,Position=position});
            terrain.SetBuildingFootprint(footprint);
            Status="Fűrészmalom elhelyezve. Kösd úttal a kijelölt erdő mellé, majd indíts teherautót.";return true;
        }
        internal bool Dispatch(VehicleSystem vehicles)
        {
            foreach(var site in Sites) {
                if(Volume(site)<0.001f)continue;
                var origins=terrain.FindRoadDocks(site.Tiles);
                foreach(var mill in Mills)foreach(int start in origins)foreach(int end in terrain.FindRoadDocks(mill.Footprint)) {
                    int[] path=terrain.FindLogisticsRoadPath(start,end);
                    if(path.Length<2)continue;
                    vehicles.SpawnLogistics(path,site.Tiles,mill.TileId);
                    Status="Teherautó indult: erdő → fűrészmalom → erdő.";return true;
                }
            }
            Status="Nincs összekötött forrás és cél. Építs összefüggő utat az erdő és a malom mellé.";return false;
        }
        internal float Load(Vehicle vehicle,float requested)
        {
            float loaded=0;
            foreach(int tile in vehicle.SourceTiles){loaded+=forest.ExtractTimber(tile,requested-loaded);if(loaded>=requested-0.00001f)break;}
            return loaded;
        }
        internal void Deliver(Vehicle vehicle,float amount)
        {
            foreach(var mill in Mills)if(mill.TileId==vehicle.SawmillTileId){mill.Received+=amount;mill.Stock+=amount;return;}
            throw new InvalidOperationException("Missing sawmill destination.");
        }
        internal void Update(double seconds){foreach(var mill in Mills){float cut=Math.Min(mill.Stock,(float)seconds*0.25f);mill.Stock-=cut;mill.Processed+=cut;}}
        internal bool RouteConnected(Vehicle vehicle)
        {
            for(int i=1;i<vehicle.Route.Length;i++)
                if(terrain.FindLogisticsRoadPath(vehicle.Route[i-1],vehicle.Route[i]).Length!=2)return false;
            return true;
        }
    }
}
