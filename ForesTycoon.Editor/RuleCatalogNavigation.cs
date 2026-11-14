using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;
using static ForesTycoon.Editor.RuleCatalogPresentation;
namespace ForesTycoon.Editor;
/// <summary>Shared selection and navigation; contains no widgets or project operations.</summary>
internal sealed class RuleCatalogNavigation(RuleCatalogSession session, RuleCatalogLayout layout, CurrentRulesUiState state)
{
    internal RuleCatalogSession Session => session;
    internal RuleCatalogLayout Layout => layout;
    internal CurrentRulesUiState State => state;
    internal GameRuleDefinition[] VisibleRules() => session.VisibleRules(state.Module, state.Search);
    internal void SelectModule(string value)
    { state.Module = value; state.Selected = session.Index.InModule(value).FirstOrDefault()?.Id ?? ""; state.FitRequested = true; }
    internal void Navigate(string id)
    { var rule = session.Index.Find(id); if (rule == null) return; state.Search = ""; SelectModule(rule.Module); state.Selected = id; }
    internal void Reset() { state.Module = state.Selected = ""; state.Dragging = null; state.FitRequested = true; }
}
