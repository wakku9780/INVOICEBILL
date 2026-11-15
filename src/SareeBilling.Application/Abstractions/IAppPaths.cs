namespace SareeBilling.Application.Abstractions;

/// <summary>
/// Locations of user data. All of it lives outside the install folder,
/// so updating or reinstalling the app never touches it.
/// </summary>
public interface IAppPaths
{
    /// <summary>Root folder for all app data, e.g. %LOCALAPPDATA%\SareeBilling.</summary>
    string RootDirectory { get; }

    string DataDirectory { get; }

    string DatabasePath { get; }

    /// <summary>Automatic safety backups taken before database upgrades.</summary>
    string AutoBackupDirectory { get; }

    string LogDirectory { get; }
}
