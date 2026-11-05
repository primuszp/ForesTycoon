using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForesTycoon.Models;

namespace ForesTycoon;

/// <summary>One wheel found in a model, in the model's own units: X forward along the body, Unit 0 the front or tractor part.</summary>
internal readonly record struct SurveyedWheel(string Name, double X, double Side, double Height, int Unit);

/// <summary>What the editor reads from a vehicle's model without creating any GPU resource. Null fields mean the model has no such part.</summary>
internal sealed record VehicleModelSurvey(string File, bool Licensed, bool Jointed, SurveyedWheel[] Wheels, double? Joint)
{
    internal const double GroupTolerance = .22;
    /// <summary>Axle stations of one body part, front to back: wheels within a tyre's width of each other share an axle.</summary>
    internal double[] Axles(int unit)
    {
        var result = new List<double>();
        foreach (double x in Wheels.Where(w => w.Unit == unit).Select(w => w.X).OrderByDescending(x => x)) {
            if (result.Count == 0 || result[^1] - x > GroupTolerance) result.Add(x);
        }
        return result.ToArray();
    }
    /// <summary>Centre of an axle group, as the renderer measures it.</summary>
    internal double Centre(int unit) { var w = Wheels.Where(a => a.Unit == unit).ToArray(); return w.Length == 0 ? double.NaN : w.Average(a => a.X); }
    internal double Track(int unit) { var w = Wheels.Where(a => a.Unit == unit).ToArray(); return w.Length == 0 ? double.NaN : w.Max(a => Math.Abs(a.Side)) * 2; }
    internal double WheelRadius => Wheels.Length == 0 ? double.NaN : Wheels.Average(a => a.Height);

    internal static VehicleModelSurvey Truck()
    {
        string path = VehicleRenderer.ModelPath();
        return path.EndsWith("-textured.glb", StringComparison.Ordinal) ? TexturedTruck(path) : FlatTruck(path);
    }

    internal static VehicleModelSurvey Machine(ForestMachineKind kind)
    {
        string name = kind == ForestMachineKind.Forwarder ? "forwarder.glb" : "harvester.glb";
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Licensed", name);
        if (!System.IO.File.Exists(path)) return null;
        var model = AnimatedGlbModel.Load(path); var pose = model.CreatePose(); pose.Evaluate(null, 0);
        int trailer = Array.FindIndex(model.Nodes, n => n.Name == "trailer");
        bool Rear(int node) { for (int p = node; p >= 0; p = model.Nodes[p].Parent) if (p == trailer && trailer >= 0) return true; return false; }
        var wheels = new List<SurveyedWheel>();
        for (int i = 0; i < model.Nodes.Length; i++)
            if ((model.Nodes[i].Name ?? "").StartsWith("wheel", StringComparison.Ordinal)) {
                var at = pose.World[i].Row3.Xyz; // glTF axes: X forward, Y up, Z sideways
                wheels.Add(new(model.Nodes[i].Name, at.X, at.Z, at.Y, Rear(i) ? 1 : 0));
            }
        return new(name, true, trailer >= 0, wheels.ToArray(), trailer >= 0 ? pose.World[trailer].Row3.X : null);
    }

    private static VehicleModelSurvey FlatTruck(string path)
    {
        using var document = GlbDocument.Load(path);
        var wheels = new Dictionary<(float, float), SurveyedWheel>();
        foreach (var node in document.Root.GetProperty("nodes").EnumerateArray()) {
            var extras = node.GetProperty("extras");
            if (extras.GetProperty("category").GetString() != "wheel") continue;
            var pivot = extras.GetProperty("pivot");
            float x = pivot[0].GetSingle(), y = pivot[1].GetSingle();
            string name = node.GetProperty("name").GetString();
            // One flattened rim and tyre per wheel; a part on the centre line is a twin axle seen as a single pivot.
            bool front = name.Contains("Front", StringComparison.Ordinal);
            wheels[(MathF.Round(x, 2), MathF.Sign(y))] = new(name, x, front ? y : 0, pivot[2].GetSingle(), 0);
        }
        return new(Path.GetFileName(path), false, false, wheels.Values.ToArray(), null);
    }

    private static VehicleModelSurvey TexturedTruck(string path)
    {
        var model = AnimatedGlbModel.Load(path); var pose = model.CreatePose(); pose.Evaluate(null, 0);
        var wheels = new List<SurveyedWheel>(); double? kingpin = null;
        for (int i = 0; i < model.Nodes.Length; i++) {
            string name = model.Nodes[i].Name ?? ""; var at = pose.World[i].Row3.Xyz;
            if (name == "trailer") kingpin = at.X;
            else if (name.StartsWith("trailer_wheel_", StringComparison.Ordinal)) wheels.Add(new(name, at.X, at.Y, at.Z, 1));
            else if (name.StartsWith("wheel_", StringComparison.Ordinal)) wheels.Add(new(name, at.X, at.Y, at.Z, 0));
        }
        return new(Path.GetFileName(path), true, kingpin != null, wheels.ToArray(), kingpin);
    }
}
