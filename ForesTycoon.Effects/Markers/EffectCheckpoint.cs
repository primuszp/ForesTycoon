namespace ForesTycoon.Effects
{
    internal sealed record EffectCheckpoint(WorldEffectKind Kind, CheckpointPosition Position, double Lifetime, double Age, double PreviousAge);
}
