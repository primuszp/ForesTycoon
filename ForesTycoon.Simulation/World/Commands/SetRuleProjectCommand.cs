namespace ForesTycoon;

internal sealed class SetRuleProjectCommand : IWorldCommand
{
    private readonly string json;
    internal SetRuleProjectCommand(string json)
    {
        var document = RuleProject.FromJson(json);
        SimulationWorld.ValidateRuleProject(document);
        this.json = document.ToJson();
    }
    public void Execute(IWorldCommandTarget world) => world.ExecuteRuleProject(json);
    public WorldCommandRecord ToRecord(ulong tick) => new(tick, WorldCommandKind.SetRuleProject, 0, 0, 0, 0, false, json);
}
