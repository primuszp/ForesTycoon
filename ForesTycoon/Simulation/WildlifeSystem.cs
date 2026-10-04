using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    // Local food depletion, hunger and shelter preference drive movement; no activity timeline.
    internal sealed class WildlifeSystem
    {
        // Gait and turning follow the drawn body size, so the stride cadence stays natural.
        internal const float WalkingSpeed = 1.5f * DioramaScale.Elk / 1.3f;
        internal const float TurningRadius = 3.2f * DioramaScale.Elk / 1.3f;
        // WalkSlow advances 156.8 cm in 1.333333 s at the rendered elk scale.
        internal const float WalkingClipSpeed = 1.568f * DioramaScale.Elk / 1.333333f;
        internal sealed class Animal
        {
            internal int Id, TileId, TargetTile;
            internal Vector3 Position, PreviousPosition, Target;
            internal float Yaw, PreviousYaw, Blend, Hunger = 0.5f;
            internal float WanderNeed = 0.35f;
            internal double WalkTime, Age;
            internal uint Seed;
        }
        internal readonly List<Animal> Animals = new();
        private readonly Dictionary<int, float> forage = new();
        private readonly List<WildlifeSpot> spots = new();
        private readonly List<int> forageTiles = new();
        private ulong revision = ulong.MaxValue, surfaceRevision = ulong.MaxValue;
        internal void Update(double seconds, Terrain terrain, ForestSystem forest, EnvironmentSystem environment)
        {
            if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (revision != forest.Revision || surfaceRevision != terrain.WeatherSurfaceRevision)
            {
                // Existing animals only need to know whether any habitat remains. Ranked spawn
                // positions are needed when populating an empty world, not at every forest month.
                terrain.CollectWildlifeSpots(forest, spots, stopAfterFirst: Animals.Count > 0 && revision != ulong.MaxValue);
                if (spots.Count == 0) Animals.Clear();
                if (revision == ulong.MaxValue || Animals.Count == 0)
                    foreach (var spot in spots) Animals.Add(new Animal { Id = spot.TileId, TileId = spot.TileId,
                        TargetTile = spot.TileId, Position = spot.Position, PreviousPosition = spot.Position, Target = spot.Position,
                        Seed = spot.Rank, Yaw = spot.Rank % 6283 * 0.001f });
                revision = forest.Revision; surfaceRevision = terrain.WeatherSurfaceRevision;
            }
            float dt = (float)seconds;
            foreach (int id in forageTiles) forage[id] = Math.Max(0, forage[id] - dt * 0.005f);
            var habitat = (IForestHabitat)terrain;
            Span<int> adjacent = stackalloc int[4];
            foreach (var animal in Animals)
            {
                animal.PreviousPosition = animal.Position; animal.PreviousYaw = animal.Yaw;
                animal.Age += seconds;
                if (!terrain.TryGetWildlifeDestination(animal.TargetTile, out _)) { animal.Target = animal.Position; animal.TargetTile = animal.TileId; }
                animal.Hunger = Math.Clamp(animal.Hunger + dt * 0.014f, 0, 1);
                forage.TryGetValue(animal.TileId, out float depleted);

                Vector2 delta = animal.Target.Xy - animal.Position.Xy;
                if (delta.Length < 0.8f)
                {
                    animal.TileId = animal.TargetTile;
                    forage.TryGetValue(animal.TileId, out depleted);
                    float food = (1 - depleted) * (0.35f + habitat.GetMoisture(animal.TileId) * 0.65f);
                    animal.Hunger = Math.Max(0, animal.Hunger - food * dt * 0.07f);
                    depleted = Math.Clamp(depleted + dt * 0.025f, 0, 1);
                    int count = habitat.GetAdjacentTileIds(animal.TileId, adjacent);
                    animal.WanderNeed = Math.Min(1, animal.WanderNeed + dt * 0.035f);
                    double best = food * (1 - animal.Hunger) + 0.18 - animal.WanderNeed;
                    for (int i = 0; i < count; i++)
                    {
                        int id = adjacent[i];
                        if (!terrain.TryGetWildlifeDestination(id, out Vector3 target)) continue;
                        forage.TryGetValue(id, out float eaten);
                        forest.TryGetStand(id, out var stand);
                        double rain = environment?.RainRate / 25 ?? 0;
                        double score = (1 - eaten) * (0.35 + habitat.GetMoisture(id) * 0.65) * animal.Hunger
                            + Math.Clamp(rain, 0, 1) * stand.Maturity * 0.8
                            + 0.12 * Math.Sin(animal.Age * 0.07 + id * 1.7 + animal.Seed % 97);
                        Vector2 direction=(target.Xy-animal.Position.Xy).Normalized();
                        score += 0.24 * Vector2.Dot(direction,new Vector2(MathF.Cos(animal.Yaw),MathF.Sin(animal.Yaw)));
                        if (score > best) { best = score; animal.TargetTile = id; animal.Target = target; }
                    }
                    delta = animal.Target.Xy - animal.Position.Xy;
                }
                if (!forage.ContainsKey(animal.TileId)) forageTiles.Add(animal.TileId);
                forage[animal.TileId] = depleted;
                bool walking = delta.Length > 0.8f;
                animal.Blend += ((walking ? 1 : 0) - animal.Blend) * (1 - MathF.Exp(-dt * 3));
                if (!walking) continue;
                float desired = MathF.Atan2(delta.Y, delta.X);
                float turn = MathF.Atan2(MathF.Sin(desired - animal.Yaw), MathF.Cos(desired - animal.Yaw));
                float distance = Math.Min(delta.Length, dt * WalkingSpeed * animal.Blend);
                float yaw = animal.Yaw + Math.Clamp(turn, -distance / TurningRadius, distance / TurningRadius);
                // Advance along the facing direction while turning, forming an actual broad arc.
                float midpoint=(animal.Yaw+yaw)*0.5f;
                Vector3 next = animal.Position + new Vector3(MathF.Cos(midpoint)*distance,MathF.Sin(midpoint)*distance,0);
                if (terrain.CanWildlifeWalkAt(next.X,next.Y) && terrain.TryGetSurfaceZ(next.X, next.Y, out float z)) {
                    next.Z = z; animal.Position = next; animal.Yaw=yaw;
                    animal.WalkTime += distance / WalkingClipSpeed;
                    animal.WanderNeed = Math.Max(0,animal.WanderNeed-distance*0.012f);
                } else {
                    // At a shoreline or road, turn gently toward the safe destination before advancing.
                    animal.Yaw += Math.Clamp(turn,-dt*0.3f,dt*0.3f);
                }
            }
        }
    }
}

