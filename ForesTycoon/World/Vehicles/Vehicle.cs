using System;

namespace ForesTycoon
{
    enum VehicleTransportState { Waiting, Loading, Hauling, Unloading, Returning }

    sealed partial class Vehicle
    {
        public Vehicle(int id, int[] route, double speedTilesPerSecond, float cargoCapacity = 25f,
            VehicleRoadRoute roadRoute = null, bool roadPhysics = true)
        {
            if (route == null || route.Length < 2) throw new ArgumentException("A vehicle route needs at least two tiles.", nameof(route));
            if (!double.IsFinite(speedTilesPerSecond) || speedTilesPerSecond <= 0.0) throw new ArgumentOutOfRangeException(nameof(speedTilesPerSecond));
            if (!float.IsFinite(cargoCapacity) || cargoCapacity <= 0f) throw new ArgumentOutOfRangeException(nameof(cargoCapacity));
            if (roadRoute != null && roadRoute.Last != route.Length - 1) throw new ArgumentException("Road geometry does not match the route.", nameof(roadRoute));
            Id = id;
            Route = (int[])route.Clone();
            SpeedTilesPerSecond = speedTilesPerSecond;
            CargoCapacity = cargoCapacity;
            RoadRoute = roadRoute;
            useRoadPhysics = roadPhysics && roadRoute != null;
            CurrentSpeed = useRoadPhysics ? 0 : speedTilesPerSecond;
        }

        private readonly bool useRoadPhysics;
        public VehicleRoadRoute RoadRoute { get; }
        public double CurrentSpeed { get; private set; }
        internal TruckSpec Spec { get; } = TruckSpec.Default;
        internal Func<int, (RoadSurface Surface, float Condition)> RoadState;
        internal Action<int, float> RoadWear;
        /// <summary>Wear of the road under the truck (0 new … 1 ruined): it slows the truck and shakes it.</summary>
        public float RoadDamage { get; private set; }
        private int wornTile = -1;
        /// <summary>Diesel burnt so far, litres.</summary>
        public double FuelUsed { get; private set; }
        /// <summary>Distance driven so far, metres.</summary>
        public double Distance { get; private set; }
        /// <summary>Gross mass: truck plus timber, kg.</summary>
        internal float Mass => VehicleDynamics.Mass(Spec, CargoAmount);
        /// <summary>Average consumption, litres per 100 km (0 before the first kilometre).</summary>
        public double FuelPer100Km => Distance > 1000 ? FuelUsed / Distance * 100000 : 0;
        /// <summary>Metres of road per tile of route position.</summary>
        internal double MetresPerTile => RoadRoute == null ? 1 : RoadRoute.TileLength / Engine.WorldScale.MetresToWorld;

        public int Id { get; }
        public int[] Route { get; }
        public double SpeedTilesPerSecond { get; }
        public double PreviousRoutePosition { get; private set; }
        public double RoutePosition { get; private set; }
        public float CargoCapacity { get; }
        public float CargoAmount { get; private set; }
        public float CargoFill => CargoAmount / CargoCapacity;
        internal bool CargoStopsEnabled;
        internal int[] SourceTiles;
        internal int SawmillTileId=-1;
        internal bool LocalCargo=>SourceTiles!=null;
        internal bool RouteBlocked;
        /// <summary>A one-way drive (depot → landing, back home): no loading stops, it just arrives.</summary>
        internal bool Transit;
        internal bool TransitArrived;
        /// <summary>Broken down: it stands still until repaired.</summary>
        internal bool Broken;
        public VehicleTransportState TransportState { get; internal set; } = VehicleTransportState.Hauling;
        public float TransferProgress { get; private set; }
        public float VisualCargoFill => LocalCargo?CargoFill:TransportState == VehicleTransportState.Loading ? CargoFill * TransferProgress :
            TransportState == VehicleTransportState.Unloading ? CargoFill * (1 - TransferProgress) : CargoFill;
        internal void Hold()
        {
            PreviousRoutePosition = RoutePosition;
            CurrentSpeed = 0;
        }
        internal void BeginLoading(float amount)
        {
            Load(amount); TransferProgress = 0;
            TransportState = amount > 0 ? VehicleTransportState.Loading : VehicleTransportState.Waiting;
            Hold();
        }
        internal void BeginUnloading()
        {
            TransferProgress = 0;
            TransportState = VehicleTransportState.Unloading;
            Hold();
        }
        internal bool AdvanceTransfer(double delta)
        {
            Hold();
            TransferProgress = Math.Min(1, TransferProgress + (float)(delta / 3.0));
            return TransferProgress >= 1;
        }

        internal void Load(float amount)
        {
            if (!float.IsFinite(amount) || amount < 0f || CargoAmount + amount > CargoCapacity + 0.0001f)
                throw new ArgumentOutOfRangeException(nameof(amount));
            CargoAmount = Math.Min(CargoCapacity, CargoAmount + amount);
        }

        internal float Unload()
        {
            float amount = CargoAmount;
            CargoAmount = 0f;
            return amount;
        }
        internal float TakeCargo(float requested){float amount=Math.Min(CargoAmount,requested);CargoAmount-=amount;return amount;}

        public void Update(double deltaSeconds)
        {
            if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            PreviousRoutePosition = RoutePosition;
            if (!useRoadPhysics)
            {
                RoutePosition += SpeedTilesPerSecond * deltaSeconds;
                return;
            }
            // Bounded integration steps keep acceleration stable at different tick rates.
            while (deltaSeconds > 0)
            {
                double dt = Math.Min(deltaSeconds, 1.0 / 30);
                deltaSeconds -= dt;
                double last = Route.Length - 1;
                double boundary = (Math.Floor(RoutePosition / last) + 1) * last;
                double remaining = boundary - RoutePosition;
                // Physical dynamics in metres: tractive force against rolling, grade and air resistance.
                double metres = MetresPerTile;
                double braking = Spec.Braking / metres;
                var (surface, damage) = RoadUnderTruck();
                // A worn road is driven slower and rolls heavier: potholes add to the geometric roughness.
                double target = Math.Min(RoadRoute.TargetSpeed(RoutePosition, SpeedTilesPerSecond, CargoFill) * (1 - 0.55 * damage),
                    Math.Sqrt(2 * braking * remaining));
                double before = CurrentSpeed;
                var (speed, fuel) = VehicleDynamics.Step(Spec, Mass, (float)(CurrentSpeed * metres), (float)(target * metres),
                    RoadRoute.Grade(RoutePosition), surface, Math.Max(RoadRoute.Roughness(RoutePosition, 1f), damage), (float)dt);
                CurrentSpeed = speed / metres;
                FuelUsed += fuel;
                double travel = (before + CurrentSpeed) * 0.5 * dt;
                Distance += Math.Min(travel, remaining) * metres;
                if (travel >= remaining || remaining < 0.001)
                {
                    RoutePosition = boundary;
                    CurrentSpeed = 0;
                    break; // Loading/unloading occurs at this boundary before restarting.
                }
                RoutePosition += travel;
            }
        }

        private (RoadSurface Surface, float Damage) RoadUnderTruck()
        {
            GetSegment(RoutePosition, out int from, out int to, out float amount);
            int tile = amount < 0.5f ? from : to;
            if (tile != wornTile)
            {
                // Each tile entered is one pass: heavier trucks wear the road more.
                if (wornTile >= 0) RoadWear?.Invoke(tile, 0.0015f * Mass / 36000f);
                wornTile = tile;
            }
            if (RoadState == null) { RoadDamage = 0; return (RoadRoute.Surface, 0); }
            var (surface, condition) = RoadState(tile);
            RoadDamage = Math.Clamp(1 - condition, 0, 1);
            return (surface, RoadDamage);
        }

        public double InterpolatedRoutePosition(float alpha) =>
            PreviousRoutePosition + (RoutePosition - PreviousRoutePosition) * Math.Clamp(alpha, 0f, 1f);

        public void GetSegment(double position, out int fromTileId, out int toTileId, out float amount)
        {
            int last = Route.Length - 1;
            double cycleLength = last * 2.0;
            position = ((position % cycleLength) + cycleLength) % cycleLength;
            double directed = position <= last ? position : cycleLength - position;
            int from = Math.Min((int)Math.Floor(directed), last - 1);
            amount = (float)(directed - from);
            if (position > last) { fromTileId = Route[from + 1]; toTileId = Route[from]; amount = 1f - amount; }
            else { fromTileId = Route[from]; toTileId = Route[from + 1]; }
        }
    }
}
