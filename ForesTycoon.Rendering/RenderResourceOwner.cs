using System;

namespace ForesTycoon.Rendering
{
    /// <summary>Lazy GPU attachment: CPU construction is portable; first GPU use fixes ownership.</summary>
    internal sealed class RenderResourceOwner
    {
        private RenderEnvironment environment;
        internal void Check()
        {
            var candidate = environment ?? RenderDevice.Environment;
            candidate.VerifyAccess();
            environment = candidate;
        }
        internal void CheckIfBound() => environment?.VerifyAccess();
    }
}
