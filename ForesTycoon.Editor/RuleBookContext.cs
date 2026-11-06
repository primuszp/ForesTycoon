using System;
using System.Collections.Generic;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor;
internal sealed record RuleBookContext(RuleBookSession Book, RuleWorkspaceUiState State, BehaviorViewUiState CalculationUi,
    Func<RuleProject> Capture, Func<bool> Initialized, Action<GameWorld, bool> ApplyProject, Action<string> SetMessage,
    Action<EditorPage> Navigate, Func<IReadOnlyList<string>> StockIds, Func<HashSet<string>> NamedValues, Action RequestApply);
