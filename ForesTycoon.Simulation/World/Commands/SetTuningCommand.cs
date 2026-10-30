namespace ForesTycoon
{
    /// <summary>Replaces the world's tunable numbers; the JSON holds only the values that differ from the defaults.</summary>
    internal sealed class SetTuningCommand : IWorldCommand
    {
        private readonly string json;
        internal SetTuningCommand(string json) => this.json = GameTuning.FromJson(json).ToJson();
        public void Execute(IWorldCommandTarget world) => world.ExecuteTuning(json);
        public WorldCommandRecord ToRecord(ulong tick) => new(tick, WorldCommandKind.SetTuning, 0, 0, 0, 0, false, json);
    }
}
