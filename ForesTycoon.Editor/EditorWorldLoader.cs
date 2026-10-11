using System.IO;
using ForesTycoon.RuleCompiler;

namespace ForesTycoon.Editor;

internal static class EditorWorldLoader
{
    internal static string Load(GameWorld world, string file)
    {
        using var input = File.OpenRead(file); var data = WorldSaveSerializer.Read(input); input.Position = 0;
        bool migration = data.Checkpoint?.Algorithms != null && data.Checkpoint.Algorithms.Revision != AlgorithmBuild.CurrentRevision;
        world.Load(input, allowAlgorithmMigration: migration);
        return migration ? "Az objektumállapot betöltve az editor saját futtatójába. A teljes próba az eredeti mentés és algoritmusok alapján fut."
            : "A világ betöltve. A munkapéldány külön dokumentum maradt.";
    }
}
