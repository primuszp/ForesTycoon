using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public uint Color;
        public static readonly int Stride = Marshal.SizeOf(default(Vertex));

        public Vertex(Vector3 Position, Vector3 Normal, uint Color)
        {
            this.Position = Position;
            this.Normal = Normal;
            this.Color = Color;
        }
    }
}
