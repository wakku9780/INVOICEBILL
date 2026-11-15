using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SareeBilling.Application.Abstractions;

namespace SareeBilling.Infrastructure.Persistence;

/// <summary>
/// Runs at startup. Creates the database on first launch; on later launches applies
/// any schema upgrades shipped with a new version, taking a safety copy first.
/// </summary>
internal sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IAppPaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IDbContextFactory<AppDbContext> contextFactory,
        IAppPaths paths,
        TimeProvider timeProvider,
        ILogger<DatabaseInitializer> logger)
    {
        _contextFactory = contextFactory;
        _paths = paths;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.DataDirectory);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var databaseExists = File.Exists(_paths.DatabasePath) && new FileInfo(_paths.DatabasePath).Length > 0;
        if (databaseExists)
        {
            BackupBeforeUpgrade();
        }

        _logger.LogInformation("Applying {Count} database migration(s): {Migrations}",
            pending.Count, string.Join(", ", pending));

        await db.Database.MigrateAsync(cancellationToken);
    }

    private void BackupBeforeUpgrade()
    {
        Directory.CreateDirectory(_paths.AutoBackupDirectory);

        var stamp = _timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss");
        var backupPath = Path.Combine(_paths.AutoBackupDirectory, $"before-upgrade-{stamp}.db");

        // SQLite's online backup API produces a consistent copy even if the file is open.
        using (var source = new SqliteConnection(SqliteConnectionStrings.For(_paths.DatabasePath, pooling: false)))
        using (var target = new SqliteConnection(SqliteConnectionStrings.For(backupPath, pooling: false)))
        {
            source.Open();
            target.Open();
            source.BackupDatabase(target);
        }

        _logger.LogInformation("Safety backup created at {BackupPath}", backupPath);
    }
}
