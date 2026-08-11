namespace ForesTycoon
{
    /// <summary>
    /// A deterministic simulation subsystem owned by <see cref="GameWorld"/>.
    /// Systems must not render or read UI state from this interface.
    /// </summary>
    interface IWorldSystem
    {
        void Update(double fixedDeltaSeconds);
        void Clear();
    }
}
