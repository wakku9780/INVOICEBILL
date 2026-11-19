using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SareeBilling.Domain.Enums;
using SareeBilling.Infrastructure.Persistence;

namespace SareeBilling.Tests.Persistence;

/// <summary>
/// Upgrading a version 1.1.x database (before receipts) must keep every customer and invoice
/// exactly as it was, give old invoices a due date and start receipts at R-1.
/// </summary>
public sealed class ReceivablesMigrationTests : IDisposable
{
    private const string Version11Migration = "20261115203608_ContinuousInvoiceNumbering";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sareebilling-migration-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Upgrade_keeps_invoices_and_customers_and_adds_due_dates()
    {
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(Version11Migration);

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO CompanyProfile (Id, Name, AddressLine1, City, State, StateCode, Gstin, DeclarationText, CreatedAtUtc)
                VALUES (1, 'TEST SAREES', 'A', 'Azamgarh', 'Uttar Pradesh', '09', '09ACSPH2858M1ZT', 'D', '2026-11-01');
                INSERT INTO Customers (Id, Name, AddressLine1, City, State, StateCode, IsActive, CreatedAtUtc)
                VALUES (1, 'KATARIYA TEXTILES', 'A', 'Pune', 'Maharashtra', '27', 1, '2026-11-01');
                UPDATE InvoiceCounter SET LastNumber = 2 WHERE Id = 1;
                """);

            foreach (var (number, date, total, status) in new[] { ("1", "2026-11-01", "350595", 1), ("2", "2026-12-31", "105.50", 2) })
            {
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO Invoices (InvoiceNumber, FinancialYear, SequenceNumber, InvoiceDate, Status, CustomerId,
                        SellerName, SellerAddress, SellerGstin, SellerState, SellerStateCode, DeclarationText,
                        BuyerName, BuyerAddress, BuyerState, BuyerStateCode, TaxType, TotalQuantity, TaxableTotal,
                        CgstTotal, SgstTotal, IgstTotal, TotalTax, RoundOff, GrandTotal, AmountInWords, CreatedAtUtc)
                    VALUES ({0}, '2026-27', {0}, {1}, {3}, 1, 'S', 'A', '09ACSPH2858M1ZT', 'Uttar Pradesh', '09', 'D',
                        'B', 'A', 'Maharashtra', '27', 2, '1', '100', '0', '0', '5', '5', '0', {2}, 'W', '2026-11-01')
                    """,
                    number, date, total, status);
            }

            await db.Database.MigrateAsync();
        }

        await using (var db = CreateContext())
        {
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());

            var invoices = await db.Invoices.OrderBy(i => i.Id).ToListAsync();
            Assert.Equal(["1", "2"], invoices.Select(i => i.InvoiceNumber));
            Assert.Equal([350595m, 105.50m], invoices.Select(i => i.GrandTotal));
            Assert.Equal([InvoiceStatus.Final, InvoiceStatus.Cancelled], invoices.Select(i => i.Status));
            Assert.Equal([new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 30)], invoices.Select(i => i.DueDate));

            var customer = await db.Customers.SingleAsync();
            Assert.Equal("KATARIYA TEXTILES", customer.Name);
            Assert.Equal(30, customer.CreditDays);

            Assert.Equal(2, (await db.InvoiceCounters.SingleAsync()).LastNumber);
            var counter = await db.VoucherCounters.SingleAsync(c => c.VoucherType == VoucherType.Receipt);
            Assert.Equal(0, counter.LastNumber);
            Assert.Empty(await db.Receipts.ToListAsync());
        }
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={_dbPath};Foreign Keys=True")
                .Options,
            TimeProvider.System);
}
