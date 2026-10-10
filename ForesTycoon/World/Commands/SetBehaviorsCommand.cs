namespace ForesTycoon
{
    internal sealed class SetBehaviorsCommand : IWorldCommand
    {
        private readonly string json;
        internal SetBehaviorsCommand(string json) => this.json = new WorldBehaviorPolicy(BehaviorModel.FromJson(json)).Document.ToJson();
        public void Execute(IWorldCommandTarget world) => world.ExecuteBehaviors(json);
        public WorldCommandRecord ToRecord(ulong tick) => new(tick, WorldCommandKind.SetBehaviors, 0, 0, 0, 0, false, json);
    }
}
