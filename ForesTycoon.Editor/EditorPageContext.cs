using System;

namespace ForesTycoon.Editor;

/// <summary>The narrow host contract shared by world pages: authored state, presentation state and application callbacks.</summary>
internal sealed record EditorPageContext(SceneSession Scene, EditorUiState Ui,
    Action<GameWorld, string> ApplyScenario, Action<GameWorld, bool> ApplyProject, Func<Action, bool> Try, Action<string> SetMessage,
    Action<EditorPage> Navigate, Func<bool> WorldCurrent, Func<ForesTycoon.Rules.RuleProject> Capture, Func<string> Revision);
