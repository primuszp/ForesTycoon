namespace ForesTycoon
{
    internal sealed class SetRuleModelCommand : IWorldCommand
    {
        private readonly string json;
        internal SetRuleModelCommand(string json)
        {
            var rule = new CompiledRoadRule(RuleModel.FromJson(json));
            rule.ValidateRange();
            this.json = rule.Document.ToJson();
        }
        public void Execute(IWorldCommandTarget world) => world.ExecuteRuleModel(json);
        public WorldCommandRecord ToRecord(ulong tick) => new(tick, WorldCommandKind.SetRuleModel, 0, 0, 0, 0, false, json);
    }
}
