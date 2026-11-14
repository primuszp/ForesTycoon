#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ForesTycoon.Rules;

/// <summary>Revision-checked storage shared by the editor, CLI and game host.</summary>
internal static class RuleProjectFile
{
    internal static string? Revision(string path) => RevisionOf(ReadBytes(path));

    internal static (string? Text, string? Revision) Read(string path)
    {
        byte[]? bytes = ReadBytes(path);
        if (bytes == null) return (null, null);

        using var reader = new StreamReader(new MemoryStream(bytes));
        return (reader.ReadToEnd(), RevisionOf(bytes));
    }

    internal static void RequireRevision(string path, string? expected)
    {
        if (Revision(path) != expected) throw Conflict(path);
    }

    internal static string Write(string path, string text, string? expected)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var gate = OpenWriteGate(path);

        byte[]? previous = ReadBytes(path);
        if (RevisionOf(previous) != expected) throw Conflict(path);

        if (previous != null) {
            string archive = Path.Combine(path + ".history", expected + ".json");
            if (!File.Exists(archive))
                RuleProject.WriteAtomic(archive, temporary => File.WriteAllBytes(temporary, previous), overwrite: false);
        }

        byte[] next = Encoding.UTF8.GetBytes(text);
        RuleProject.WriteAtomic(path, temporary => {
            File.WriteAllBytes(temporary, next);
            // External tools may write without taking our lock. Check again before committing.
            RequireRevision(path, expected);
        }, overwrite: expected != null);
        return RevisionOf(next)!;
    }

    private static FileStream OpenWriteGate(string path)
    {
        try {
            // Keep the lock file after closing. Recreating it would allow processes to lock different files.
            return new FileStream(path + ".write.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error) {
            throw new IOException("A projekt írási zára nem nyitható meg. Ha másik folyamat ment, próbáld újra: " + path, error);
        }
    }

    private static byte[]? ReadBytes(string path)
    {
        try {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var bytes = new MemoryStream();
            source.CopyTo(bytes);
            return bytes.ToArray();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static string? RevisionOf(byte[]? bytes) =>
        bytes == null ? null : Convert.ToHexString(SHA256.HashData(bytes));

    private static IOException Conflict(string path) => new(
        "A fájl verziója időközben megváltozott. Nyisd meg a friss változatot, vagy ments másik útvonalra: " + path);
}
