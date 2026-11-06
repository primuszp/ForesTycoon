#nullable enable
using ForesTycoon.Rules;
namespace ForesTycoon.Editor.Core;

internal sealed class DynamicsStockView(DynamicsStock model)
{
    public string Id => model.Id;
    public string Name => model.Name;
    public string Unit => model.Unit;
    public double Initial => model.Initial;
    public double Minimum => model.Minimum;
    public double Maximum => model.Maximum;
}

internal sealed class DynamicsFlowView(DynamicsFlow model)
{
    public string Id => model.Id;
    public string Name => model.Name;
    public string From => model.From;
    public string To => model.To;
}
