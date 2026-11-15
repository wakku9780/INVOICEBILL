using SareeBilling.Application.Abstractions;

namespace SareeBilling.Infrastructure;

/// <summary>
/// Data lives under %LOCALAPPDATA%\{folderName}. It is not stored in Documents,
/// because OneDrive sync can corrupt a live SQLite file.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    public AppPaths(string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        RootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            folderName);
        DataDirectory = Path.Combine(RootDirectory, "Data");
        DatabasePath = Path.Combine(DataDirectory, "sareebilling.db");
        AutoBackupDirectory = Path.Combine(RootDirectory, "AutoBackups");
        LogDirectory = Path.Combine(RootDirectory, "Logs");
    }

    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string AutoBackupDirectory { get; }
    public string LogDirectory { get; }
}
