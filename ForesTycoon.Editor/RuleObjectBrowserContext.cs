using System;
namespace ForesTycoon.Editor;
internal sealed record RuleObjectBrowserContext(RuleBookSession Book, RuleWorkspaceUiState State, Action<int> OpenRule,
    Action RefreshCatalog, Func<string> LiveStatus, Action<string> SetMessage);
