using System;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor;
internal sealed record VehicleGeometryContext(EditorTuning Tuning, VehicleGeometryUiState Ui,
    Func<GameRuleCatalog> Catalog, Action<string, double> SetValue, Action<GameWorld> EnsureCatalog,
    Func<GameWorld, int> Unapplied, Action<GameWorld, Func<string, bool>> DrawTuning);
internal sealed partial class VehicleGeometryView(VehicleGeometryContext context);
