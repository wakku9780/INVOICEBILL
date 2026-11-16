using Microsoft.Data.Sqlite;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Backups;
using SareeBilling.Application.Common;
using SareeBilling.Application.CompanyProfiles;
using SareeBilling.Application.Customers;
using SareeBilling.Application.Invoices;

namespace SareeBilling.Tests.Application;

public sealed class BackupServiceTests : IAsyncLifetime
{
    private TestHost _host = null!;
    private string _backupFolder = null!;

    private IBackupService Backups => _host.Get<IBackupService>();
    private ICustomerService Customers => _host.Get<ICustomerService>();

    public async Task InitializeAsync()
    {
        _host = await TestHost.StartAsync();
        _backupFolder = Path.Combine(_host.Get<IAppPaths>().RootDirectory, "PenDrive");

        await _host.Get<ICompanyProfileService>().SaveAsync(new CompanyProfileDto
        {
            Name = "J A SAREES", AddressLine1 = "Purani Basti", City = "Azamgarh", StateCode = "09",
            Gstin = "09ACSPH2858M1ZT", DeclarationText = "Declaration"
        });
        var customerId = await Customers.SaveAsync(new CustomerDto
        {
            Name = "KATARIYA TEXTILES", AddressLine1 = "Bhavani Peth", City = "Pune", StateCode = "27", Gstin = "27AAOFK4239P1ZK"
        });
        await _host.Get<IInvoiceService>().CreateAsync(new CreateInvoiceRequest
        {
            CustomerId = customerId,
            InvoiceDate = new DateOnly(2026, 11, 1),
            Items = [new InvoiceItemInput { Description = "Saree", HsnCode = "5007", Quantity = 53, Unit = "Qty", Rate = 6300, GstRate = 5 }]
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Backup_is_a_single_healthy_file_listed_newest_first_and_inspectable()
    {
        var path = await Backups.CreateBackupAsync(_backupFolder);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + "-wal"));
        Assert.Single(Backups.ListBackups(_backupFolder));
        Assert.NotNull(Backups.GetLastManualBackupTime());

        var contents = await Backups.InspectAsync(path);
        Assert.Equal("J A SAREES", contents.CompanyName);
        Assert.Equal(1, contents.InvoiceCount);
        Assert.Equal(1, contents.CustomerCount);
        Assert.Equal("1", contents.LastInvoiceNumber);
        Assert.Equal(new DateOnly(2026, 11, 1), contents.LastInvoiceDate);
    }

    [Fact]
    public async Task Restore_brings_back_old_data_and_keeps_a_safety_copy()
    {
        var backup = await Backups.CreateBackupAsync(_backupFolder);

        // Data changes after the backup...
        await Customers.SaveAsync(new CustomerDto { Name = "Added Later", AddressLine1 = "X", City = "Surat", StateCode = "24" });
        Assert.Equal(2, (await Customers.SearchAsync(null, includeInactive: true)).Count);

        var safetyCopy = await Backups.RestoreAsync(backup);

        // ...and is gone after restoring, but kept in the safety copy.
        var customers = await Customers.SearchAsync(null, includeInactive: true);
        Assert.Single(customers);
        Assert.Equal("KATARIYA TEXTILES", customers[0].Name);
        Assert.True(File.Exists(safetyCopy));
        Assert.Equal(2, (await Backups.InspectAsync(safetyCopy)).CustomerCount);

        // The app keeps working normally after a restore: numbering continues correctly.
        Assert.Equal("2", await _host.Get<IInvoiceService>().PreviewNextNumberAsync());
    }

    [Fact]
    public async Task Non_database_file_is_rejected()
    {
        Directory.CreateDirectory(_backupFolder);
        var fake = Path.Combine(_backupFolder, "SareeBilling-Backup-fake.db");
        await File.WriteAllTextAsync(fake, "this is not a database");

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Backups.InspectAsync(fake));
        Assert.Contains("not a Saree Billing backup", ex.Errors[0]);

        await Assert.ThrowsAsync<ValidationException>(() => Backups.RestoreAsync(fake));
        Assert.Single(await Customers.SearchAsync(null, includeInactive: true));
    }

    [Fact]
    public async Task Other_sqlite_database_is_rejected()
    {
        Directory.CreateDirectory(_backupFolder);
        var other = Path.Combine(_backupFolder, "other.db");
        using (var connection = new SqliteConnection($"Data Source={other};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Something (Id INTEGER)";
            command.ExecuteNonQuery();
        }

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Backups.InspectAsync(other));
        Assert.Equal("This file is not a Saree Billing backup.", ex.Errors[0]);
    }

    [Fact]
    public async Task Backup_from_newer_app_version_is_rejected()
    {
        var backup = await Backups.CreateBackupAsync(_backupFolder);
        using (var connection = new SqliteConnection($"Data Source={backup};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO __EFMigrationsHistory VALUES ('29991231000000_FutureChange', '99.0.0')";
            command.ExecuteNonQuery();
        }

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Backups.InspectAsync(backup));
        Assert.Contains("newer version", ex.Errors[0]);
    }

    [Fact]
    public async Task Current_database_cannot_be_restored_onto_itself()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Backups.InspectAsync(_host.Get<IAppPaths>().DatabasePath));
        Assert.Contains("using right now", ex.Errors[0]);
    }

    [Fact]
    public async Task Daily_backup_runs_once_per_day_and_keeps_only_the_newest()
    {
        var autoFolder = _host.Get<IAppPaths>().AutoBackupDirectory;
        Directory.CreateDirectory(autoFolder);
        for (var day = 1; day <= 5; day++)
        {
            await File.WriteAllTextAsync(Path.Combine(autoFolder, $"daily-2020010{day}.db"), "old");
        }

        await Backups.CreateDailyBackupIfNeededAsync(keepDays: 3);
        await Backups.CreateDailyBackupIfNeededAsync(keepDays: 3);

        var daily = Directory.GetFiles(autoFolder, "daily-*.db").Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(3, daily.Count);
        Assert.Equal("daily-20200104.db", daily[0]);
        Assert.Equal($"daily-{DateTime.Now:yyyyMMdd}.db", daily[^1]);
    }
}
