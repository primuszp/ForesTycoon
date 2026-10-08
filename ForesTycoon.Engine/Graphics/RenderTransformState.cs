using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.Engine
{
    /// <summary>CPU transform state, independent of graphics APIs and native contexts.</summary>
    public sealed class RenderTransformState
    {
        private readonly Stack<Matrix4> modelStack = new Stack<Matrix4>();
        public Matrix4 ViewProjection { get; private set; } = Matrix4.Identity;
        public Matrix4 Model { get; private set; } = Matrix4.Identity;

        public void SetCamera(Matrix4 viewProjection)
        {
            ViewProjection = viewProjection;
            Model = Matrix4.Identity;
            modelStack.Clear();
        }

        public void SetViewProjection(Matrix4 viewProjection) => ViewProjection = viewProjection;
        public void SetModel(Matrix4 model) => Model = model;
        public void PushModel() => modelStack.Push(Model);

        public void PopModel()
        {
            if (modelStack.Count == 0) throw new InvalidOperationException("Render model stack underflow.");
            Model = modelStack.Pop();
        }

        // OpenTK row-vector convention, retained across backends.
        public void Translate(float x, float y, float z) =>
            Model = Matrix4.CreateTranslation(x, y, z) * Model;

        public void Reset() => SetCamera(Matrix4.Identity);
    }
}
