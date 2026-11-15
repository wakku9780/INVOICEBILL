using System.IO;
using System.Text;
using SareeBilling.Application.Abstractions;

namespace SareeBilling.Desktop.Services;

/// <summary>
/// Appends unexpected errors to Logs\errors-yyyyMM.log so problems on another
/// computer can be diagnosed. Never throws: logging must not crash the app.
/// </summary>
public sealed class ErrorLog
{
    private static readonly object Sync = new();
    private readonly IAppPaths _paths;

    public ErrorLog(IAppPaths paths)
    {
        _paths = paths;
    }

    public void Write(string context, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(_paths.LogDirectory);
            var file = Path.Combine(_paths.LogDirectory, $"errors-{DateTime.Now:yyyyMM}.log");

            var entry = new StringBuilder()
                .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}")
                .AppendLine(exception?.ToString() ?? "(no exception details)")
                .AppendLine()
                .ToString();

            lock (Sync)
            {
                File.AppendAllText(file, entry);
            }
        }
        catch
        {
            // Ignore: there is nowhere left to report a logging failure.
        }
    }
}
