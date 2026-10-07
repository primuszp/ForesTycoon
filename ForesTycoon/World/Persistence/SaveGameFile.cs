using System;
using System.IO;

namespace ForesTycoon
{
    internal static class SaveGameFile
    {
        // Write and flush beside the destination before replacing the previous save.
        internal static void Write(string path, Action<Stream> save)
        {
            ArgumentNullException.ThrowIfNull(save);
            string destination = Path.GetFullPath(path);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    save(stream);
                    stream.Flush(true);
                }
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
