using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon;

// Host algorithms; the project schedules their calls and owns the state equations.
internal sealed partial class WildlifeSystem
{
    internal GameplayProgram Gameplay { get; set; } = GameplayProgram.Default;
    internal Func<double, IEnumerable<int>> RuleStep;
    internal const float WalkingSpeed = 1.5f * DioramaScale.Elk / 1.3f;
    internal const float TurningRadius = 3.2f * DioramaScale.Elk / 1.3f;
    internal const float WalkingClipSpeed = 1.568f * DioramaScale.Elk / 1.333333f;
    internal readonly List<Animal> Animals = new();
    private readonly Dictionary<int, ForageCell> forage = new();
    private readonly List<int> forageTiles = new();
    private readonly List<WildlifeSpot> spots = new();
    private ulong revision = ulong.MaxValue, surfaceRevision = ulong.MaxValue;
    private TerrainMap terrain;
    private ForestSystem forest;
    private EnvironmentSystem environment;
    private bool populationRequested;
    private bool habitatReady;
    internal int NextAnimalId = 1;
    internal bool NeedsPopulationRefresh => terrain != null && (!habitatReady || revision != forest.Revision || surfaceRevision != terrain.SurfaceVersion);
    internal void InvalidateRules() => surfaceRevision = ulong.MaxValue;
    internal bool RequestPopulation() { populationRequested = true; return false; }
    internal IEnumerable<ForageCell> ForageObjects { get { foreach (int id in forageTiles) yield return forage[id]; } }
    internal float Forage(int id) => forage.TryGetValue(id, out var cell) ? cell.Depletion : 0;
    internal void SetForage(int id, double value) {
        if (!forage.TryGetValue(id, out var cell)) { forage.Add(id, cell = new() { Id = id }); forageTiles.Add(id); }
        cell.Depletion = (float)Math.Clamp(value, 0, 1);
    }
    internal void Update(double seconds, TerrainMap terrain, ForestSystem forest, EnvironmentSystem environment)
        => SimulationWork.Complete(UpdateWork(seconds, terrain, forest, environment));
    internal IEnumerable<int> UpdateWork(double seconds, TerrainMap terrain, ForestSystem forest, EnvironmentSystem environment)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        this.terrain = terrain; this.forest = forest; this.environment = environment;
        foreach (var animal in Animals) {
            animal.System = this; animal.StepSeconds = seconds; animal.DistanceMoved = 0;
            animal.PreviousPosition = animal.Position; animal.PreviousYaw = animal.Yaw;
            animal.PreviousWalkTime = animal.WalkTime; animal.PreviousAge = animal.Age;
            yield return 1;
        }
        if (RuleStep != null) foreach (int unit in RuleStep(seconds)) yield return unit;
        Animals.RemoveAll(a => a.Removed);
        if (populationRequested) {
            foreach (int unit in terrain.CollectWildlifeSpotsWork(forest, spots)) yield return unit;
            revision = forest.Revision; surfaceRevision = terrain.SurfaceVersion;
            habitatReady = true;
            populationRequested = false;
        }
        if (PopulationRuleStep != null) foreach (int unit in PopulationRuleStep(seconds)) yield return unit;
        born.Clear();
        foreach (int unit in ApplyPopulationsWork()) yield return unit;
        if (born.Count > 0 && BirthRuleStep != null) foreach (int unit in BirthRuleStep(born.ToArray())) yield return unit;
        Animals.RemoveAll(a => a.Removed);
    }
    internal void ChooseDestination(Animal animal)
    {
        if (!terrain.TryGetWildlifeDestination(animal.TargetTile, out _)) { animal.Target = animal.Position; animal.TargetTile = animal.TileId; }
        var habitat = (IForestHabitat)terrain;
        double best = Gameplay.Evaluate("wildlife.stayScore", animal.Food, animal.Hunger, animal.WanderNeed);
        Span<int> adjacent = stackalloc int[4];
        int count = habitat.GetAdjacentTileIds(animal.TileId, adjacent);
        for (int i = 0; i < count; i++) {
            int id = adjacent[i];
            if (!terrain.TryGetWildlifeDestination(id, out Vector3 target)) continue;
            forest.TryGetStand(id, out var stand);
            var direction = (target.Xy - animal.Position.Xy).Normalized();
            double score = Gameplay.Evaluate("wildlife.destinationScore",
                Gameplay.Evaluate("wildlife.food", Forage(id), habitat.GetMoisture(id)), animal.Hunger,
                Math.Clamp((environment?.RainRate ?? 0) / Math.Max(.001, animal.RainScale), 0, 1), stand.Maturity,
                Math.Sin(animal.Age * animal.NoiseTimeScale + id * animal.NoiseTileScale + animal.Seed % 97),
                Vector2.Dot(direction, new(MathF.Cos(animal.Yaw), MathF.Sin(animal.Yaw))));
            if (score > best) { best = score; animal.TargetTile = id; animal.Target = target; }
        }
    }
    internal void Advance(Animal animal)
    {
        float dt = (float)animal.StepSeconds;
        var delta = animal.Target.Xy - animal.Position.Xy;
        bool walking = delta.Length > animal.ArrivalRadius;
        animal.Blend += ((walking ? 1 : 0) - animal.Blend) * (1 - MathF.Exp(-dt * animal.GaitResponse));
        if (!walking) return;
        float desired = MathF.Atan2(delta.Y, delta.X);
        float turn = MathF.Atan2(MathF.Sin(desired - animal.Yaw), MathF.Cos(desired - animal.Yaw));
        float distance = Math.Min(delta.Length, dt * animal.MovementSpeed * animal.Blend);
        float yaw = animal.Yaw + Math.Clamp(turn, -distance / Math.Max(.001f, animal.TurnRadius), distance / Math.Max(.001f, animal.TurnRadius));
        float midpoint = (animal.Yaw + yaw) * .5f;
        Vector3 next = animal.Position + new Vector3(MathF.Cos(midpoint) * distance, MathF.Sin(midpoint) * distance, 0);
        if (terrain.CanWildlifeWalkAt(next.X, next.Y) && terrain.TryGetSurfaceZ(next.X, next.Y, out float z)) {
            next.Z = z; animal.Position = next; animal.Yaw = yaw;
            animal.WalkTime += distance / WalkingClipSpeed; animal.DistanceMoved = distance;
        } else animal.Yaw += Math.Clamp(turn, -dt * animal.BlockedTurnSpeed, dt * animal.BlockedTurnSpeed);
    }
}
