namespace SareeBilling.Application.Updates;

/// <summary>A published version of the app that can be installed.</summary>
public sealed record ReleaseInfo(
    Version Version,
    string TagName,
    string Title,
    string Notes,
    DateTimeOffset? PublishedAt,
    string InstallerFileName,
    Uri InstallerUrl,
    long InstallerSizeBytes,
    string? InstallerSha256,
    Uri? ReleasePageUrl);

/// <summary>What happened to the update started in the previous session.</summary>
public sealed record PreviousUpdateResult(bool Succeeded, Version TargetVersion);

/// <summary>Raised for problems talking to the update server or downloading; the app keeps working.</summary>
public sealed class UpdateException : Exception
{
    public UpdateException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Checks for, downloads and prepares app updates. Never touches business data apart
/// from taking a safety backup before an update is installed.
/// </summary>
public interface IUpdateService
{
    /// <summary>The version of the running app, e.g. 1.0.1.</summary>
    Version CurrentVersion { get; }

    /// <returns>The latest release if it is newer than the running app; otherwise null.</returns>
    /// <exception cref="UpdateException">No internet, server error or unexpected reply.</exception>
    Task<ReleaseInfo?> FindNewerReleaseAsync(CancellationToken cancellationToken = default);

    /// <summary>True while the user has chosen "Remind me later" for this version.</summary>
    bool IsSnoozed(ReleaseInfo release);

    void Snooze(ReleaseInfo release, TimeSpan duration);

    /// <summary>
    /// Downloads the installer and verifies its size, SHA-256 fingerprint and file type.
    /// A failed or cancelled download leaves nothing behind.
    /// </summary>
    /// <param name="progress">Receives 0–100.</param>
    /// <returns>Path of the verified installer.</returns>
    /// <exception cref="UpdateException">Download or verification failed.</exception>
    Task<string> DownloadInstallerAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a safety backup of the database and records that an update to
    /// <paramref name="release"/> is being installed, so the next start can report the result.
    /// </summary>
    /// <returns>Path of the safety backup.</returns>
    Task<string> PrepareForInstallAsync(ReleaseInfo release, CancellationToken cancellationToken = default);

    /// <summary>Forget a prepared update that could not be started.</summary>
    void ClearPendingUpdate();

    /// <summary>Where the installer writes its log for this update.</summary>
    string GetInstallerLogPath(ReleaseInfo release);

    /// <summary>
    /// Reads (and clears) the record of an update started in the previous session.
    /// </summary>
    /// <returns>Null when no update was started.</returns>
    PreviousUpdateResult? TakePreviousUpdateResult();
}
