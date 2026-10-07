using System;
using System.IO;
using System.Text.Json;

namespace ForesTycoon
{
    static class WorldSaveSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static void Write(Stream destination, WorldSaveData save)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (save == null) throw new ArgumentNullException(nameof(save));
            save.Validate();
            JsonSerializer.Serialize(destination, save, Options);
        }

        public static WorldSaveData Read(Stream source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            WorldSaveData save = JsonSerializer.Deserialize<WorldSaveData>(source, Options)
                ?? throw new InvalidDataException("The save file is empty or invalid.");
            save.Validate();
            return save;
        }
    }

    static class SaveGamePath
    {
        public static string Default
        {
            get
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForesTycoon");
                Directory.CreateDirectory(directory);
                return Path.Combine(directory, "quicksave.json");
            }
        }
    }
}
