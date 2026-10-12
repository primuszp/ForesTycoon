namespace ForesTycoon.Engine
{
    /// <summary>Optional pure rule override. Identity is stable in saves; no rendering or editor dependency.</summary>
    internal readonly record struct BehaviorQuery(string Hook, string Kind, ulong Id, double Native,
        double Delta = 0, double Time = 0, double State = 0, double Amount = 0,
        double Capacity = 0, double Available = 0, double From = 0, double To = 0, double Surface = 0, double[] Extras = null);

    internal interface IBehaviorPolicy
    {
        double Evaluate(in BehaviorQuery query);
        /// <summary>False when every graph of the hook only returns the built-in result: hot loops may skip the call.</summary>
        bool Overrides(string hook, string kind) => true;
    }
}
