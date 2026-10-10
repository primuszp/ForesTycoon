using System;
using System.IO;

namespace ForesTycoon.Rules
{
    /// <summary>Reuses validation and compiled equations until a mutable draft's calculation changes.</summary>
    public sealed class RoadRuleDraftCompiler
    {
        private RuleModel snapshot;
        public string ValidationMessage { get; private set; } = "";
        public CompiledRoadRule Compiled { get; private set; }

        public CompiledRoadRule Compile(RuleModel draft)
        {
            ArgumentNullException.ThrowIfNull(draft);
            if (Matches(draft)) return Compiled;
            snapshot = draft.Clone();
            Compiled = null;
            try
            {
                var next = new CompiledRoadRule(draft);
                next.ValidateRange();
                Compiled = next;
                ValidationMessage = "A modell érvényes.";
            }
            catch (InvalidDataException error) { ValidationMessage = error.Message; }
            return Compiled;
        }

        private bool Matches(RuleModel draft)
        {
            if (snapshot == null || snapshot.Version != draft.Version || snapshot.Name != draft.Name ||
                snapshot.Binding != draft.Binding || snapshot.Output != draft.Output) return false;
            if (snapshot.Nodes == null || draft.Nodes == null) return snapshot.Nodes == draft.Nodes;
            if (snapshot.Nodes.Count != draft.Nodes.Count) return false;
            for (int i = 0; i < draft.Nodes.Count; i++)
            {
                var a = snapshot.Nodes[i]; var b = draft.Nodes[i];
                if (a == null || b == null) { if (a != b) return false; continue; }
                if (a.Id != b.Id || a.Name != b.Name || a.Operation != b.Operation || !a.Value.Equals(b.Value) ||
                    a.A != b.A || a.B != b.B || float.IsFinite(a.X) != float.IsFinite(b.X) ||
                    float.IsFinite(a.Y) != float.IsFinite(b.Y)) return false;
            }
            return true;
        }
    }
}
