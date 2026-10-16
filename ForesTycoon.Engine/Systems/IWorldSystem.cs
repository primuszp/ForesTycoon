namespace ForesTycoon.Engine
{
    /// <summary>
    /// A deterministic simulation subsystem owned by the game world.
    /// Systems must not render or read UI state from this interface.
    /// </summary>
    interface IWorldSystem
    {
        void Update(double fixedDeltaSeconds);
        // Required: new systems must explicitly define their bounded suspension points.
        System.Collections.Generic.IEnumerable<int> UpdateWork(double fixedDeltaSeconds);
        void Clear();
    }
}
