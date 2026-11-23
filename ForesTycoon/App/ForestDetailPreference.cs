using System;
using System.IO;

namespace ForesTycoon
{
    internal static class ForestDetailPreference
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ForesTycoon", "forest-detail.txt");

        internal static ForestDetailMode Read(string path = null)
        {
            path ??= FilePath;
            try {
                if (!File.Exists(path)) return ForestDetailMode.Adaptive;
                return Enum.TryParse(File.ReadAllText(path).Trim(), out ForestDetailMode mode) && Enum.IsDefined(mode)
                    ? mode : ForestDetailMode.Adaptive;
            }
            catch (IOException) { return ForestDetailMode.Adaptive; }
            catch (UnauthorizedAccessException) { return ForestDetailMode.Adaptive; }
        }

        internal static bool Save(ForestDetailMode mode, string path = null)
        {
            path ??= FilePath;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(temporary, mode.ToString());
                File.Move(temporary, path, overwrite: true);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            finally {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
