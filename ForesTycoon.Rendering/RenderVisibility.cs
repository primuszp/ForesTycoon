using OpenTK.Mathematics;
namespace ForesTycoon.Rendering
{
    internal readonly struct RenderVisibility
    {
        private readonly ClipPlane left, right, bottom, top, near, far;

        // Extract once per camera, rather than six square roots per object. Retain the
        // homogeneous equations so edge comparisons have the original rounding behavior.
        internal RenderVisibility(Matrix4 camera)
        {
            Vector4 x = camera.Column0, y = camera.Column1, z = camera.Column2, w = camera.Column3;
            left = new(w + x); right = new(w - x);
            bottom = new(w + y); top = new(w - y);
            near = new(w + z); far = new(w - z);
        }

        internal bool SphereVisible(Vector3 center, float radius)
        {
            Vector4 point = new Vector4(center, 1);
            return left.Contains(point, radius) && right.Contains(point, radius)
                && bottom.Contains(point, radius) && top.Contains(point, radius)
                && near.Contains(point, radius) && far.Contains(point, radius);
        }

        private readonly struct ClipPlane
        {
            private readonly Vector4 equation;
            private readonly float normalLength;
            internal ClipPlane(Vector4 equation) { this.equation = equation; normalLength = equation.Xyz.Length; }
            internal bool Contains(Vector4 point, float radius) => Vector4.Dot(point, equation) >= -radius * normalLength;
        }
    }
}
