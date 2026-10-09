using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon
{
    sealed partial class Vehicle
    {
        internal VehicleCheckpoint Capture() => new(Id, (int[])Route.Clone(), SpeedTilesPerSecond, CargoCapacity, useRoadPhysics,
            RoadRoute?.Capture(), CurrentSpeed, RoutePosition, PreviousRoutePosition, CargoAmount, CargoStopsEnabled,
            SourceTiles == null ? null : (int[])SourceTiles.Clone(), SawmillTileId, RouteBlocked, TransportState, TransferProgress,
            FuelUsed, Distance);
        internal static Vehicle Restore(VehicleCheckpoint s, int tileCount)
        {
            CheckpointGuard.Require(s != null && s.Id > 0 && s.Route != null && s.Route.Length >= 2 && Enum.IsDefined(s.State), "vehicle identity");
            foreach (int id in s.Route) CheckpointGuard.Require((uint)id < (uint)tileCount, "vehicle route tile");
            if (s.SourceTiles != null) {
                CheckpointGuard.Require(s.SourceTiles.Length > 0 && (uint)s.Mill < (uint)tileCount, "vehicle source/mill");
                foreach (int id in s.SourceTiles) CheckpointGuard.Require((uint)id < (uint)tileCount, "vehicle source tile");
            }
            foreach (double v in new[] { s.CurrentSpeed, s.Position, s.PreviousPosition, s.Cargo }) CheckpointGuard.NonNegative(v, "vehicle state");
            CheckpointGuard.Require(s.PreviousPosition <= s.Position && s.Cargo <= s.Capacity, "vehicle cargo/position");
            CheckpointGuard.Unit(s.TransferProgress, "vehicle transfer");
            CheckpointGuard.Require(!s.Physics || s.RoadGeometry != null, "vehicle physics geometry");
            var result = new Vehicle(s.Id, s.Route, s.Speed, s.Capacity,
                s.RoadGeometry == null ? null : VehicleRoadRoute.Restore(s.RoadGeometry), s.Physics);
            result.CurrentSpeed = s.CurrentSpeed; result.RoutePosition = s.Position; result.PreviousRoutePosition = s.PreviousPosition;
            result.CargoAmount = s.Cargo; result.CargoStopsEnabled = s.CargoStops;
            result.SourceTiles = s.SourceTiles == null ? null : (int[])s.SourceTiles.Clone(); result.SawmillTileId = s.Mill;
            result.RouteBlocked = s.Blocked; result.TransportState = s.State; result.TransferProgress = s.TransferProgress;
            CheckpointGuard.NonNegative(s.FuelUsed, "vehicle fuel"); CheckpointGuard.NonNegative(s.Distance, "vehicle distance");
            result.FuelUsed = s.FuelUsed; result.Distance = s.Distance;
            return result;
        }
    }
    sealed partial class VehicleSystem
    {
        internal VehiclesCheckpoint Capture() => new(nextId, UseRoadPhysics, UseCargoStops, vehicles.Select(v => v.Capture()).ToArray());
        internal void Restore(VehiclesCheckpoint s, int tileCount)
        {
            CheckpointGuard.Require(s != null && s.Vehicles != null && s.NextId > 0, "vehicle system");
            var restored = new List<Vehicle>(); var ids = new HashSet<int>();
            foreach (var v in s.Vehicles) {
                CheckpointGuard.Require(v != null && v.Id < s.NextId && ids.Add(v.Id), "vehicle IDs");
                restored.Add(Vehicle.Restore(v, tileCount));
            }
            vehicles.Clear(); vehicles.AddRange(restored); nextId = s.NextId;
            UseRoadPhysics = s.RoadPhysics; UseCargoStops = s.CargoStops;
        }
    }
    sealed partial class VehicleRoadRoute
    {
        internal RoadRouteCheckpoint Capture() => new(centers.Select(p => new CheckpointPosition(p)).ToArray(),
            gradients.Select(p => new RoadGradientCheckpoint(p.X, p.Y)).ToArray());
        internal static VehicleRoadRoute Restore(RoadRouteCheckpoint s)
        {
            CheckpointGuard.Require(s != null && s.Centers != null && s.Gradients != null &&
                s.Centers.Length >= 2 && s.Centers.Length == s.Gradients.Length, "captured road geometry");
            foreach (var p in s.Centers) p.Validate();
            foreach (var p in s.Gradients) CheckpointGuard.Require(p != null && float.IsFinite(p.X) && float.IsFinite(p.Y), "captured road gradient");
            return new(s.Centers.Select(p => p.Vector).ToArray(),
                s.Gradients.Select(p => new OpenTK.Mathematics.Vector2(p.X, p.Y)).ToArray());
        }
    }
    internal sealed partial class WildlifeSystem
    {
        internal WildlifeCheckpoint Capture() => new(Animals.Select(a => new AnimalCheckpoint(a.Id, a.TileId, a.TargetTile,
            new(a.Position), new(a.PreviousPosition), new(a.Target), a.Yaw, a.PreviousYaw, a.Blend, a.Hunger, a.WanderNeed,
            a.WalkTime, a.Age, a.Seed)).ToArray(), forageTiles.Select(id => new ForageCheckpoint(id, forage[id])).ToArray(), revision, surfaceRevision);
        internal void Restore(WildlifeCheckpoint s, int tileCount)
        {
            CheckpointGuard.Require(s != null && s.Animals != null && s.Forage != null, "wildlife system");
            var restored = new List<Animal>(); var ids = new HashSet<int>(); var food = new Dictionary<int, float>();
            foreach (var a in s.Animals) {
                CheckpointGuard.Require(a != null && ids.Add(a.Id) && (uint)a.Tile < (uint)tileCount && (uint)a.TargetTile < (uint)tileCount,
                    "animal identity/tiles");
                a.Position.Validate(); a.PreviousPosition.Validate(); a.Target.Validate();
                CheckpointGuard.Require(float.IsFinite(a.Yaw) && float.IsFinite(a.PreviousYaw), "animal yaw");
                CheckpointGuard.Unit(a.Blend, "animal gait"); CheckpointGuard.Unit(a.Hunger, "animal hunger"); CheckpointGuard.Unit(a.WanderNeed, "animal wander");
                CheckpointGuard.NonNegative(a.WalkTime, "animal walk clock"); CheckpointGuard.NonNegative(a.Age, "animal age");
                restored.Add(new Animal { Id = a.Id, TileId = a.Tile, TargetTile = a.TargetTile, Position = a.Position.Vector,
                    PreviousPosition = a.PreviousPosition.Vector, Target = a.Target.Vector, Yaw = a.Yaw, PreviousYaw = a.PreviousYaw,
                    Blend = a.Blend, Hunger = a.Hunger, WanderNeed = a.WanderNeed, WalkTime = a.WalkTime, Age = a.Age, Seed = a.Seed });
            }
            foreach (var f in s.Forage) {
                CheckpointGuard.Require(f != null && (uint)f.Tile < (uint)tileCount && food.TryAdd(f.Tile, f.Depletion), "forage tile");
                CheckpointGuard.Unit(f.Depletion, "forage depletion");
            }
            Animals.Clear(); Animals.AddRange(restored); forage.Clear(); forageTiles.Clear(); spots.Clear();
            foreach (var f in food) { forage.Add(f.Key, f.Value); forageTiles.Add(f.Key); }
            revision = s.ForestRevision; surfaceRevision = s.SurfaceRevision;
        }
    }
    internal sealed partial class ForestryLogistics
    {
        internal LogisticsCheckpoint Capture() => new(Sites.Select(s => new HarvestCheckpoint((int[])s.Tiles.Clone(), s.InitialVolume,
                s.Landing, s.Piles.Select(p => new PileCheckpoint(p.Key, p.Value)).ToArray(), s.LandingStock)).ToArray(),
            Mills.Select(m => new MillCheckpoint(m.TileId, (int[])m.Footprint.Clone(), new(m.Position), m.Received, m.Stock, m.Processed)).ToArray(), Status,
            Machines.Select(m => new ForestMachineCheckpoint(m.Id, m.Kind, Sites.IndexOf(m.Site), (int[])m.Path.Clone(), m.PathPosition,
                m.PreviousPathPosition, m.State, m.Goal, m.Cargo, m.WorkTime)).ToArray(), nextMachineId);
        internal void Restore(LogisticsCheckpoint s)
        {
            CheckpointGuard.Require(s != null && s.Sites != null && s.Mills != null && s.Status != null, "logistics system");
            var used = new HashSet<int>(); var sites = new List<HarvestSite>(); var mills = new List<Sawmill>();
            foreach (var site in s.Sites) {
                CheckpointGuard.Require(site != null && site.Tiles != null && site.Tiles.Length > 0, "harvest site");
                foreach (int id in site.Tiles) CheckpointGuard.Require(terrain.IsValidTileId(id) && used.Add(id), "harvest tile");
                CheckpointGuard.NonNegative(site.InitialVolume, "harvest volume");
                CheckpointGuard.Require(site.Landing == -1 || terrain.IsValidTileId(site.Landing), "harvest landing");
                CheckpointGuard.NonNegative(site.LandingStock, "landing stock");
                var restored = new HarvestSite { Tiles = (int[])site.Tiles.Clone(), InitialVolume = site.InitialVolume,
                    Landing = site.Landing, LandingStock = site.LandingStock };
                foreach (var pile in site.Piles ?? Array.Empty<PileCheckpoint>())
                {
                    CheckpointGuard.Require(pile != null && terrain.IsValidTileId(pile.Tile) && !restored.Piles.ContainsKey(pile.Tile), "log pile");
                    CheckpointGuard.NonNegative(pile.Volume, "log pile volume");
                    restored.Piles[pile.Tile] = pile.Volume;
                }
                sites.Add(restored);
            }
            var footprints = new HashSet<int>(); var destinations = new HashSet<int>();
            foreach (var m in s.Mills) {
                CheckpointGuard.Require(m != null && terrain.IsValidTileId(m.TileId) && destinations.Add(m.TileId) &&
                    m.Footprint != null && m.Footprint.Length == 4 && Array.IndexOf(m.Footprint, m.TileId) >= 0, "sawmill footprint");
                foreach (int id in m.Footprint) CheckpointGuard.Require(terrain.IsValidTileId(id) && terrain.IsBuildingTile(id) &&
                    footprints.Add(id) && !used.Contains(id), "sawmill tile");
                m.Position.Validate(); CheckpointGuard.NonNegative(m.Received, "mill received");
                CheckpointGuard.NonNegative(m.Stock, "mill stock"); CheckpointGuard.NonNegative(m.Processed, "mill processed");
                CheckpointGuard.Require(Math.Abs(m.Received - m.Stock - m.Processed) <= Math.Max(1, m.Received) * 1e-5, "mill mass balance");
                mills.Add(new Sawmill { TileId = m.TileId, Footprint = (int[])m.Footprint.Clone(), Position = m.Position.Vector,
                    Received = m.Received, Stock = m.Stock, Processed = m.Processed });
            }
            var machines = new List<ForestMachine>(); var ids = new HashSet<int>();
            foreach (var m in s.Machines ?? Array.Empty<ForestMachineCheckpoint>())
            {
                CheckpointGuard.Require(m != null && ids.Add(m.Id) && m.Id > 0 && m.Id < s.NextMachineId && Enum.IsDefined(m.Kind) &&
                    Enum.IsDefined(m.State) && Enum.IsDefined(m.Goal) && (uint)m.Site < (uint)sites.Count &&
                    m.Path != null && m.Path.Length > 0 && Array.TrueForAll(m.Path, terrain.IsValidTileId), "forest machine");
                CheckpointGuard.Require(double.IsFinite(m.Position) && m.Position >= 0 && m.Position <= m.Path.Length - 1 &&
                    double.IsFinite(m.PreviousPosition) && m.PreviousPosition >= 0 && m.PreviousPosition <= m.Path.Length - 1, "forest machine position");
                CheckpointGuard.NonNegative(m.Cargo, "forest machine cargo"); CheckpointGuard.NonNegative(m.WorkTime, "forest machine work time");
                CheckpointGuard.Require(m.Cargo <= ForestMachine.ForwarderCapacity + 0.001f, "forest machine cargo");
                machines.Add(new ForestMachine { Id = m.Id, Kind = m.Kind, Site = sites[m.Site], Path = (int[])m.Path.Clone(),
                    PathPosition = m.Position, PreviousPathPosition = m.PreviousPosition, State = m.State, Goal = m.Goal, Cargo = m.Cargo, WorkTime = m.WorkTime });
            }
            CheckpointGuard.Require(s.NextMachineId >= 1, "forest machine ids");
            Sites.Clear(); Sites.AddRange(sites); Mills.Clear(); Mills.AddRange(mills); Status = s.Status;
            Machines.Clear(); Machines.AddRange(machines); nextMachineId = s.NextMachineId;
        }
    }
    sealed partial class TimberCargoSystem
    {
        internal void Restore(float available, float delivered) {
            CheckpointGuard.NonNegative(available, "available timber"); CheckpointGuard.NonNegative(delivered, "delivered timber");
            Available = available; Delivered = delivered;
        }
    }
}
