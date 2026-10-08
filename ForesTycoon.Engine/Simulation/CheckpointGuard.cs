using System;
using System.IO;

namespace ForesTycoon.Engine
{
    internal static class CheckpointGuard
    {
        internal static void Require(bool valid, string message)
        { if (!valid) throw new InvalidDataException("Invalid checkpoint: " + message); }
        internal static void NonNegative(double value, string name) => Require(double.IsFinite(value) && value >= 0, name);
        internal static void Unit(double value, string name) => Require(double.IsFinite(value) && value >= 0 && value <= 1, name);
        internal static void Length<T>(T[] values, int count, string name) => Require(values != null && values.Length == count, name);
    }
    internal readonly record struct CheckpointPosition(float X, float Y, float Z)
    {
        internal CheckpointPosition(OpenTK.Mathematics.Vector3 p) : this(p.X, p.Y, p.Z) { }
        internal OpenTK.Mathematics.Vector3 Vector => new(X, Y, Z);
        internal void Validate() => CheckpointGuard.Require(float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z), "position");
    }
}
