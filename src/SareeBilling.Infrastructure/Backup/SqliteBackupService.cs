using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Backups;
using SareeBilling.Application.Common;
using SareeBilling.Infrastructure.Persistence;

namespace SareeBilling.Infrastructure.Backup;

internal sealed class SqliteBackupService : IBackupService
{
    private const string ManualPrefix = "SareeBilling-Backup-";
    private const string DailyPrefix = "daily-";
    private const string LastBackupMarker = "last-backup.txt";
    private static readonly string[] RequiredTables = ["__EFMigrationsHistory", "CompanyProfile", "Customers", "Invoices", "InvoiceItems"];

    private readonly IAppPaths _paths;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDatabaseInitializer _initializer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SqliteBackupService> _logger;

    public SqliteBackupService(
        IAppPaths paths,
        IDbContextFactory<AppDbContext> contextFactory,
        IDatabaseInitializer initializer,
        TimeProvider timeProvider,
        ILogger<SqliteBackupService> logger)
    {
        _paths = paths;
        _contextFactory = contextFactory;
        _initializer = initializer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string DefaultBackupFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SareeBilling Backups");

    public async Task<string> CreateBackupAsync(string folder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ValidationException([$"Cannot write to the folder \"{folder}\". Please choose another folder or check the pen drive."]);
        }

        var stamp = _timeProvider.GetLocalNow().ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
        var target = UniquePath(Path.Combine(folder, $"{ManualPrefix}{stamp}.db"));

        await Task.Run(() =>
        {
            CopyDatabase(_paths.DatabasePath, target);
            EnsureHealthy(target);
        }, cancellationToken);

        File.WriteAllText(Path.Combine(_paths.RootDirectory, LastBackupMarker),
            _timeProvider.GetLocalNow().ToString("O", CultureInfo.InvariantCulture));

        _logger.LogInformation("Backup created at {Path}", target);
        return target;
    }

    public IReadOnlyList<BackupFile> ListBackups(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        return new DirectoryInfo(folder)
            .EnumerateFiles($"{ManualPrefix}*.db")
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new BackupFile(f.FullName, f.Name, f.LastWriteTime, f.Length))
            .ToList();
    }

    public async Task<BackupContents> InspectAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            throw new ValidationException(["The backup file was not found."]);
        }

        if (string.Equals(Path.GetFullPath(backupPath), Path.GetFullPath(_paths.DatabasePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(["This is the database the app is using right now, not a backup."]);
        }

        string[] knownMigrations;
        await using (var db = await _contextFactory.CreateDbContextAsync(cancellationToken))
        {
            knownMigrations = db.Database.GetMigrations().ToArray();
        }

        return await Task.Run(() => Inspect(backupPath, knownMigrations), cancellationToken);
    }

    public async Task<string> RestoreAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        await InspectAsync(backupPath, cancellationToken);

        var safetyCopy = await Task.Run(() =>
        {
            Directory.CreateDirectory(_paths.AutoBackupDirectory);
            var stamp = _timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var path = UniquePath(Path.Combine(_paths.AutoBackupDirectory, $"before-restore-{stamp}.db"));
            CopyDatabase(_paths.DatabasePath, path);

            // Release pooled connections so nothing holds the old data open, then copy the
            // backup's pages over the live database in one SQLite backup operation.
            SqliteConnection.ClearAllPools();
            CopyDatabase(backupPath, _paths.DatabasePath, sourceReadOnly: true, convertToSingleFile: false);
            SqliteConnection.ClearAllPools();
            return path;
        }, cancellationToken);

        // A backup from an older version is upgraded to the current database schema.
        await _initializer.InitializeAsync(cancellationToken);

        _logger.LogInformation("Database restored from {Backup}; previous data saved to {SafetyCopy}", backupPath, safetyCopy);
        return safetyCopy;
    }

    public async Task CreateDailyBackupIfNeededAsync(int keepDays = 30, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.DatabasePath))
        {
            return;
        }

        var today = _timeProvider.GetLocalNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var target = Path.Combine(_paths.AutoBackupDirectory, $"{DailyPrefix}{today}.db");

        await Task.Run(() =>
        {
            Directory.CreateDirectory(_paths.AutoBackupDirectory);
            if (!File.Exists(target))
            {
                CopyDatabase(_paths.DatabasePath, target);
                _logger.LogInformation("Daily backup created at {Path}", target);
            }

            // Names sort by date, so the oldest files are at the end.
            foreach (var old in Directory.GetFiles(_paths.AutoBackupDirectory, $"{DailyPrefix}*.db")
                         .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                         .Skip(Math.Max(1, keepDays)))
            {
                File.Delete(old);
            }
        }, cancellationToken);
    }

    public DateTime? GetLastManualBackupTime()
    {
        var marker = Path.Combine(_paths.RootDirectory, LastBackupMarker);
        if (!File.Exists(marker))
        {
            return null;
        }

        return DateTimeOffset.TryParse(File.ReadAllText(marker), CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)
            ? when.LocalDateTime
            : null;
    }

    // ------------------------------------------------------------------ helpers

    private static BackupContents Inspect(string path, string[] knownMigrations)
    {
        try
        {
            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();

            if (!string.Equals(Scalar<string>(connection, "PRAGMA quick_check"), "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(["This backup file is damaged and cannot be restored."]);
            }

            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            if (!RequiredTables.All(tables.Contains))
            {
                throw new ValidationException(["This file is not a Saree Billing backup."]);
            }

            var backupMigrations = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    backupMigrations.Add(reader.GetString(0));
                }
            }

            if (backupMigrations.Except(knownMigrations, StringComparer.Ordinal).Any())
            {
                throw new ValidationException(
                    ["This backup was made with a newer version of Saree Billing. Please update the app first, then restore."]);
            }

            string? lastNumber = null;
            DateOnly? lastDate = null;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT InvoiceNumber, InvoiceDate FROM Invoices ORDER BY InvoiceDate DESC, Id DESC LIMIT 1";
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    lastNumber = reader.GetString(0);
                    lastDate = DateOnly.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
                }
            }

            return new BackupContents(
                path,
                Scalar<string?>(connection, "SELECT Name FROM CompanyProfile LIMIT 1"),
                (int)Scalar<long>(connection, "SELECT COUNT(*) FROM Invoices"),
                (int)Scalar<long>(connection, "SELECT COUNT(*) FROM Customers"),
                lastNumber,
                lastDate,
                File.GetLastWriteTime(path));
        }
        catch (SqliteException)
        {
            throw new ValidationException(["This file is not a Saree Billing backup, or it is damaged."]);
        }
    }

    /// <param name="convertToSingleFile">
    /// Switch the copy to rollback-journal mode so it is one self-contained file
    /// (no -wal/-shm side files) that can be moved to a pen drive safely.
    /// </param>
    private static void CopyDatabase(string sourcePath, string targetPath, bool sourceReadOnly = false, bool convertToSingleFile = true)
    {
        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = sourceReadOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false
        };

        using var source = new SqliteConnection(sourceBuilder.ToString());
        using var target = new SqliteConnection(SqliteConnectionStrings.For(targetPath, pooling: false));
        source.Open();
        target.Open();
        source.BackupDatabase(target);

        if (convertToSingleFile)
        {
            using var command = target.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = DELETE";
            command.ExecuteNonQuery();
        }
    }

    private static void EnsureHealthy(string path)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        if (!string.Equals(Scalar<string>(connection, "PRAGMA quick_check"), "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"The backup at {path} failed its integrity check.");
        }
    }

    private static T Scalar<T>(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? default! : (T)value;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(folder, $"{name}-{i}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}
