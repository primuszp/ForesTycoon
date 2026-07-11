using System;

namespace ForesTycoon
{
    sealed class Vehicle
    {
        public Vehicle(int id, int[] route, double speedTilesPerSecond)
        {
            if (route == null || route.Length < 2) throw new ArgumentException("A vehicle route needs at least two tiles.", nameof(route));
            if (speedTilesPerSecond <= 0.0) throw new ArgumentOutOfRangeException(nameof(speedTilesPerSecond));
            Id = id;
            Route = route;
            SpeedTilesPerSecond = speedTilesPerSecond;
        }

        public int Id { get; }
        public int[] Route { get; }
        public double SpeedTilesPerSecond { get; }
        public double PreviousRoutePosition { get; private set; }
        public double RoutePosition { get; private set; }

        public void Update(double deltaSeconds)
        {
            PreviousRoutePosition = RoutePosition;
            RoutePosition += SpeedTilesPerSecond * deltaSeconds;
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
