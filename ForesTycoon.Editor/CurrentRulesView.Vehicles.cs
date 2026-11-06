using System;
using Vec2 = System.Numerics.Vector2;
namespace ForesTycoon.Editor;
internal sealed partial class CurrentRulesView
{
    private readonly VehicleGeometryView vehicles;
    internal const int GeometryLayoutTab = VehicleGeometryView.GeometryLayoutTab, GeometryBindingTab = VehicleGeometryView.GeometryBindingTab,
        GeometryTrialTab = VehicleGeometryView.GeometryTrialTab, GeometryOperatingTab = VehicleGeometryView.GeometryOperatingTab;
    internal void SelectGeometryTabForTest(int kind, int tab) => vehicles.SelectGeometryTabForTest(kind, tab);
    internal void DrawVehicleGeometryPreview(GameWorld world) => vehicles.DrawVehicleGeometryPreview(world);
    internal void DrawVehicleWorkspace(GameWorld world, Func<string, bool> operatingRules, Action apply) => vehicles.DrawVehicleWorkspace(world, operatingRules, apply);
    internal double GetGeometry(string key) => vehicles.GetGeometry(key);
    internal void SetGeometry(string key, double value) => vehicles.SetGeometry(key, value);
    internal void SelectGeometryStudyForTest(int kind, StudyManoeuvre manoeuvre, float radius = 20) => vehicles.SelectGeometryStudyForTest(kind, manoeuvre, radius);
    internal float GeometryPlayheadForTest => vehicles.GeometryPlayheadForTest;
    internal void PlayGeometryForTest() => vehicles.PlayGeometryForTest();
    internal VehiclePathStudy GeometryStudyForTest => vehicles.GeometryStudyForTest;
    internal Vec2 GeometryHandleForTest(string key) => vehicles.GeometryHandleForTest(key);
    internal string[] GeometryPresetProblemsForTest() => vehicles.GeometryPresetProblemsForTest();
}
