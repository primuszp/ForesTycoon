using System;
namespace ForesTycoon.Editor;
internal sealed record RuleLibraryContext(RuleLibrarySession Library, BehaviorEditorView Behaviors, RuleWorkspaceUiState State,
    Action<string> OpenRule, Func<Action, bool> Try, Action<string> SetMessage);
