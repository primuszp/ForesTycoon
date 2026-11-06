using System;
using System.IO;
using System.Text.Json;
namespace ForesTycoon.Editor;
internal static class EditorErrors
{
    internal static bool Expected(Exception error) => error is IOException or InvalidDataException or JsonException
        or ArgumentException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException;
}
