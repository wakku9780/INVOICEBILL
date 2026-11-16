using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SareeBilling.Infrastructure.Persistence;

namespace SareeBilling.Tests.Persistence;

/// <summary>
/// Upgrading a database made before plain invoice numbers must keep every existing invoice
/// number unchanged and continue after the highest number already used or reserved.
/// </summary>
public sealed class NumberingMigrationTests : IDisposable
{
    private const string LastPerYearMigration = "20261115190737_StoreLogoInDatabase";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sareebilling-migration-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData(161, 2, "162")] // a "continue from Tally" start number was set higher than the invoices made
    [InlineData(0, 2, "3")]     // no start number: continue after the highest invoice
    public async Task Upgrade_keeps_old_numbers_and_continues_after_highest(int reservedLastNumber, int invoiceCount, string expectedNext)
    {
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(LastPerYearMigration);

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO CompanyProfile (Id, Name, AddressLine1, City, State, StateCode, Gstin, InvoicePrefix, DeclarationText, CreatedAtUtc)
                VALUES (1, 'J A SAREES', 'A', 'Azamgarh', 'Uttar Pradesh', '09', '09ACSPH2858M1ZT', 'JAS', 'D', '2026-11-01');
                INSERT INTO Customers (Id, Name, AddressLine1, City, State, StateCode, IsActive, CreatedAtUtc)
                VALUES (1, 'KATARIYA TEXTILES', 'A', 'Pune', 'Maharashtra', '27', 1, '2026-11-01');
                """);

            for (var n = 1; n <= invoiceCount; n++)
            {
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO Invoices (InvoiceNumber, FinancialYear, SequenceNumber, InvoiceDate, Status, CustomerId,
                        SellerName, SellerAddress, SellerGstin, SellerState, SellerStateCode, DeclarationText,
                        BuyerName, BuyerAddress, BuyerState, BuyerStateCode, TaxType, TotalQuantity, TaxableTotal,
                        CgstTotal, SgstTotal, IgstTotal, TotalTax, RoundOff, GrandTotal, AmountInWords, CreatedAtUtc)
                    VALUES ({0}, '2026-27', {1}, '2026-11-01', 1, 1, 'S', 'A', '09ACSPH2858M1ZT', 'Uttar Pradesh', '09', 'D',
                        'B', 'A', 'Maharashtra', '27', 2, '1', '100', '0', '0', '5', '5', '0', '105', 'W', '2026-11-01')
                    """,
                    $"JAS/2026-27/{n}", n);
            }

            if (reservedLastNumber > 0)
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO InvoiceSequences (FinancialYear, LastNumber) VALUES ('2026-27', {0})", reservedLastNumber);
            }

            // Upgrade to the current version.
            await db.Database.MigrateAsync();
        }

        await using (var db = CreateContext())
        {
            var numbers = await db.Invoices.OrderBy(i => i.Id).Select(i => i.InvoiceNumber).ToListAsync();
            Assert.Equal(Enumerable.Range(1, invoiceCount).Select(n => $"JAS/2026-27/{n}"), numbers);

            var counter = await db.InvoiceCounters.SingleAsync();
            Assert.Equal(expectedNext, (counter.LastNumber + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={_dbPath};Foreign Keys=True")
                .Options,
            TimeProvider.System);
}
