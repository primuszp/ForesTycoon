using OpenTK.Mathematics;
namespace ForesTycoon
{
    internal static class RenderVisibility
    {
        // Homogeneous clip planes work for both orthographic and perspective views.
        internal static bool SphereVisible(Vector3 center, float radius, Matrix4 camera)
        {
            Vector4 point = new Vector4(center, 1);
            Vector4 x = camera.Column0, y = camera.Column1, z = camera.Column2, w = camera.Column3;
            return Inside(w+x) && Inside(w-x) && Inside(w+y) && Inside(w-y) && Inside(w+z) && Inside(w-z);
            bool Inside(Vector4 plane) => Vector4.Dot(point, plane) >= -radius * plane.Xyz.Length;
        }
    }
}
