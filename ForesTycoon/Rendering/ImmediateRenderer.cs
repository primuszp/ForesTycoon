using System;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    internal static class ImmediateRenderer
    {
        public static void Draw(PrimitiveType primitiveType, Action draw)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));

            GL.Begin(primitiveType);
            try
            {
                draw();
            }
            finally
            {
                GL.End();
            }
        }
    }
}
