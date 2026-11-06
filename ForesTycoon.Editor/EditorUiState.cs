using System;
using System.Collections.Generic;
using ForesTycoon.Rules;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal enum EditorPage { Map, Soils, Biomes, Species, Surfaces, Missions, Vehicles, Parameters, Objects, Rules, Responses, Processes, Controllers, Stocks, Library, SystemMap, Overview, Trial }
internal enum WorldTab { Map, Types, Objects, Missions, Rules, Trial }
internal enum MapLayer { Soil, Elevation, Zones, Biomes, Ground, Water, GroundWater, Forest, Tracks, Trafficability }
internal enum TypeTab { Soil, Species, Surfaces, Vehicles, Parameters }
internal enum StructureTool { Macadam, Asphalt, Dirt, SkidTrail, Sawmill, Depot }

/// <summary>Per-window presentation choices. None of these fields is part of a saved rule project.</summary>
internal sealed class EditorUiState
{
    internal EditorPage Page;
    internal bool? NavigationCompact;
    internal readonly Dictionary<EditorPage, string> LastCommon = new();
    internal readonly MapUiState Map = new();
    internal readonly CurrentRulesUiState SystemMap = new();
    internal readonly VehicleGeometryUiState Vehicles = new();
    internal readonly BehaviorViewUiState Behaviors = new(), Expression = new(), FlowEquation = new();
    internal readonly DynamicsViewUiState Dynamics = new();
    internal readonly BiomeUiState Biomes = new();
    internal readonly TrialUiState Trial = new();
    internal int SelectedMission;
    internal readonly RuleWorkspaceUiState Rules = new();
    internal readonly SoilUiState Soils = new();
    internal readonly SpeciesUiState Species = new();
    internal readonly TreePreviewUiState Tree = new();
}
internal sealed class MapUiState
{
    internal MapLayer Layer;
    internal int BrushSoil = 1, BrushSize = 2, SelectedTile = -1, Seed, ElevationTool, WaterTool, BrushSpecies;
    internal float SpringRate = 300;
    internal readonly HashSet<int> Stroke = new();
    internal bool StrokeCancelled;
    internal StructureTool StructureTool;
    internal int RoadStart = -1;
    internal string HighlightedStructure;
    internal bool ShowStructureBuffers = true;
}
internal sealed class SoilUiState { internal int Selected; internal bool ScrollToProfile; }
internal sealed class SpeciesUiState { internal int Selected, Tab; }
internal sealed class TreePreviewUiState
{
    internal int Phase, Light = 2, Variant, Season = 1, Mode, PanMode = -1, HoveredLevel = -1, ShownLevel = -1;
    internal int? TestLevel;
    internal bool ShowAllParameters, TrueScale = true, Compare = true, SeeThrough, LeafPoints = true;
    internal float Yaw = .6f, Pitch = .28f, Zoom = 1;
    internal Vec2 Pan;
    internal string Filter = "";
}

internal sealed class RuleWorkspaceUiState
{
    internal readonly HashSet<string> EmptyGroups = new(StringComparer.CurrentCulture);
    internal readonly Dictionary<string, string> FieldPickerObjects = new();
    internal RuleFieldDefinition NewField = new() { Name = "saját.", Scope = "tile" };
    internal bool TemplateSpecific;
    internal string LibrarySearch = "", TemplateTarget = "", TemplateId;
    internal string TransferPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForesTycoon", "rules", "sablonok.json");
    internal int SelectedBookRule;
    internal int? InitialBookSelection;
    internal string RuleFilter = "";
    internal string ActiveCommonKey;
    internal string CommonSelection;
    internal string ExpressionKey;
    internal string ExpressionSource;
    internal bool EditRuleCalculation;
    internal int SelectedCalculation = -1;
    internal int SelectedInfluence;
    internal int SelectedTerm;
    internal int InfluencePreviewId = -1;
    internal float InfluencePreviewYears = .1f;
    internal int RuleCardTab;
    internal bool ScrollRuleCard;
    internal string RequestedResponseOff;
    internal double RequestedResponseValue;
    internal string NewRuleGroup = "";
    internal int DraggedRule = -1;
    internal string SelectedObject;
    internal string ObjectFilter = "";
    internal bool OverviewOnlyShared;
    internal string CommonFilter = "";
    internal int PassageTile = -1;
    internal int PassageKind;
    internal float PassageMass = 36000;
    internal float PassageFill = 1;
}

internal sealed class BiomeUiState { internal int Selected, Tab, Material; }
internal sealed class TrialUiState { internal int Intensity = 25, Seconds = 60; }

internal sealed class CurrentRulesUiState
{
    internal string Module = "", Selected = "", Search = "", Dragging, Hovered;
    internal Vec2 Pan = new(10, 10);
    internal float Zoom = .85f;
    internal bool FitRequested = true;
}
internal sealed class VehicleGeometryUiState { internal int Kind, Tab; }
internal sealed class BehaviorViewUiState
{
    internal string SelectedGraph, SelectedNode, SelectedController, SelectedState, TargetId = "", TargetSearch = "", Dragging, Linking;
    internal Vec2 Pan = new(20, 20);
    internal float Zoom = .85f;
}
internal sealed class DynamicsViewUiState { internal string SelectedStock; }
