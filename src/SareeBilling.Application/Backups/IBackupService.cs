namespace SareeBilling.Application.Backups;

/// <summary>A backup file found in a folder.</summary>
public sealed record BackupFile(string Path, string FileName, DateTime CreatedLocal, long SizeBytes);

/// <summary>What a backup file contains, shown to the user before restoring it.</summary>
public sealed record BackupContents(
    string Path,
    string? CompanyName,
    int InvoiceCount,
    int CustomerCount,
    string? LastInvoiceNumber,
    DateOnly? LastInvoiceDate,
    DateTime FileDateLocal);

/// <summary>
/// Copies of the whole database (company, customers, invoices, logo).
/// All operations use SQLite's online backup API, so copies are consistent
/// even while the app is open.
/// </summary>
public interface IBackupService
{
    /// <summary>Default folder for manual backups: Documents\SareeBilling Backups.</summary>
    string DefaultBackupFolder { get; }

    /// <returns>Full path of the new backup file.</returns>
    Task<string> CreateBackupAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>Backups in a folder, newest first. Missing folder → empty list.</summary>
    IReadOnlyList<BackupFile> ListBackups(string folder);

    /// <summary>
    /// Opens a backup read-only and checks that it is a healthy Saree Billing database
    /// made by this or an older version of the app.
    /// </summary>
    /// <exception cref="Common.ValidationException">When the file cannot be restored.</exception>
    Task<BackupContents> InspectAsync(string backupPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces all current data with the backup. A safety copy of the current data is
    /// saved first (AutoBackups\before-restore-*.db), so a restore can always be undone.
    /// </summary>
    /// <returns>Path of the safety copy.</returns>
    /// <exception cref="Common.ValidationException">When the file cannot be restored.</exception>
    Task<string> RestoreAsync(string backupPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Once per day: copies the database to AutoBackups\daily-yyyyMMdd.db and keeps the newest
    /// <paramref name="keepDays"/> daily copies.
    /// </summary>
    Task CreateDailyBackupIfNeededAsync(int keepDays = 30, CancellationToken cancellationToken = default);

    /// <summary>Time of the newest manual backup in the default folder, or null if none.</summary>
    DateTime? GetLastManualBackupTime();

    /// <summary>
    /// Safety copy in AutoBackups named "{label}-yyyyMMdd-HHmmss.db" (e.g. before an app update).
    /// Does not count as a manual backup.
    /// </summary>
    /// <returns>Path of the copy, or null when there is no database yet.</returns>
    Task<string?> CreateSafetyCopyAsync(string label, CancellationToken cancellationToken = default);
}
