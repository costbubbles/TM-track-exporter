using System.IO;
using Tm2Ac.Core;

namespace Tm2Ac.App.Services;

/// <summary>Appends errors to %LOCALAPPDATA%\Tm2Ac\logs\app.log.</summary>
internal static class AppLog
{
    private static readonly Lock Gate = new();

    public static string PathName => Path.Combine(Tm2AcPaths.LogsDirectory, "app.log");

    public static void Write(Exception exception) => Write(exception.ToString());

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Tm2AcPaths.LogsDirectory);
                File.AppendAllText(PathName, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down.
        }
    }
}
