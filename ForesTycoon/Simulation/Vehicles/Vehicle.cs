using System;

namespace ForesTycoon
{
    enum VehicleTransportState { Waiting, Loading, Hauling, Unloading, Returning }

    sealed class Vehicle
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

        public int Id { get; }
        public int[] Route { get; }
        public double SpeedTilesPerSecond { get; }
        public double PreviousRoutePosition { get; private set; }
        public double RoutePosition { get; private set; }
        public float CargoCapacity { get; }
        public float CargoAmount { get; private set; }
        public float CargoFill => CargoAmount / CargoCapacity;
        internal bool CargoStopsEnabled;
        public VehicleTransportState TransportState { get; internal set; } = VehicleTransportState.Hauling;
        public float TransferProgress { get; private set; }
        public float VisualCargoFill => TransportState == VehicleTransportState.Loading ? CargoFill * TransferProgress :
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
                const double braking = 2.4;
                double target = Math.Min(RoadRoute.TargetSpeed(RoutePosition, SpeedTilesPerSecond, CargoFill),
                    Math.Sqrt(2 * braking * remaining));
                double acceleration = target < CurrentSpeed ? braking : 0.8 / (1 + CargoFill * 0.65);
                double before = CurrentSpeed;
                CurrentSpeed += Math.Clamp(target - CurrentSpeed, -acceleration * dt, acceleration * dt);
                double travel = (before + CurrentSpeed) * 0.5 * dt;
                if (travel >= remaining || remaining < 0.001)
                {
                    RoutePosition = boundary;
                    CurrentSpeed = 0;
                    break; // Loading/unloading occurs at this boundary before restarting.
                }
                RoutePosition += travel;
            }
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
