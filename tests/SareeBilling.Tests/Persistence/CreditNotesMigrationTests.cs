using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SareeBilling.Domain.Enums;
using SareeBilling.Infrastructure.Persistence;

namespace SareeBilling.Tests.Persistence;

/// <summary>
/// Upgrading a version 1.2.0 database (receipts, no credit notes) must keep every invoice, receipt,
/// opening bill and bill-wise match exactly as it was. SQLite rebuilds the BillAllocations table to
/// change its check constraint, so the rows are compared one by one.
/// </summary>
public sealed class CreditNotesMigrationTests : IDisposable
{
    private const string Version12Migration = "20261118195441_ReceiptsAndCustomerBalances";

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
    public async Task Upgrade_keeps_invoices_receipts_and_matches_and_starts_credit_notes_at_CN_1()
    {
        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(Version12Migration);

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO CompanyProfile (Id, Name, AddressLine1, City, State, StateCode, Gstin, DeclarationText, CreatedAtUtc)
                VALUES (1, 'TEST SAREES', 'A', 'Azamgarh', 'Uttar Pradesh', '09', '09ACSPH2858M1ZT', 'D', '2026-11-01');
                INSERT INTO Customers (Id, Name, AddressLine1, City, State, StateCode, IsActive, CreditDays, CreatedAtUtc)
                VALUES (1, 'KATARIYA TEXTILES', 'A', 'Pune', 'Maharashtra', '27', 1, 45, '2026-11-01');
                UPDATE InvoiceCounter SET LastNumber = 7 WHERE Id = 1;
                INSERT INTO Invoices (Id, InvoiceNumber, FinancialYear, SequenceNumber, InvoiceDate, DueDate, Status, CustomerId,
                    SellerName, SellerAddress, SellerGstin, SellerState, SellerStateCode, DeclarationText,
                    BuyerName, BuyerAddress, BuyerState, BuyerStateCode, TaxType, TotalQuantity, TaxableTotal,
                    CgstTotal, SgstTotal, IgstTotal, TotalTax, RoundOff, GrandTotal, AmountInWords, CreatedAtUtc)
                VALUES (1, '7', '2026-27', 7, '2026-11-01', '2026-12-16', 1, 1, 'S', 'A', '09ACSPH2858M1ZT', 'Uttar Pradesh', '09', 'D',
                    'B', 'A', 'Maharashtra', '27', 2, '10', '10000', '0', '0', '500', '500', '0', '10500', 'W', '2026-11-01');
                INSERT INTO InvoiceItems (Id, InvoiceId, LineNumber, Description, HsnCode, Quantity, Unit, Rate, TaxableAmount,
                    GstRate, CgstRate, CgstAmount, SgstRate, SgstAmount, IgstRate, IgstAmount, LineTotal)
                VALUES (1, 1, 1, 'Silk saree', '5007', '10', 'Pcs', '1000', '10000', '5', '0', '0', '0', '0', '5', '500', '10500');
                INSERT INTO OpeningBills (Id, CustomerId, BillNumber, BillDate, DueDate, Side, Amount, CreatedAtUtc)
                VALUES (1, 1, '145', '2026-03-10', '2026-04-24', 1, '2000.50', '2026-11-01');
                INSERT INTO Receipts (Id, ReceiptNumber, SequenceNumber, ReceiptDate, CustomerId, CustomerName, Amount, Mode, Reference, Status, CreatedAtUtc)
                VALUES (1, 'R-1', 1, '2026-11-05', 1, 'KATARIYA TEXTILES', '6000', 2, 'UTR 1', 1, '2026-11-05');
                UPDATE VoucherCounters SET LastNumber = 1 WHERE VoucherType = 1;
                INSERT INTO BillAllocations (Id, CustomerId, AllocatedOn, ReceiptId, OpeningCreditId, InvoiceId, OpeningBillId, Amount, CreatedAtUtc)
                VALUES (1, 1, '2026-11-05', 1, NULL, NULL, 1, '2000.50', '2026-11-05'),
                       (2, 1, '2026-11-05', 1, NULL, 1, NULL, '3999.50', '2026-11-05');
                """);

            await db.Database.MigrateAsync();
        }

        await using (var db = CreateContext())
        {
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());

            var invoice = await db.Invoices.Include(i => i.Items).SingleAsync();
            Assert.Equal(("7", 10500m, InvoiceStatus.Final, new DateOnly(2026, 12, 16)),
                (invoice.InvoiceNumber, invoice.GrandTotal, invoice.Status, invoice.DueDate));
            Assert.Equal(10000m, Assert.Single(invoice.Items).TaxableAmount);
            Assert.Equal(45, (await db.Customers.SingleAsync()).CreditDays);
            Assert.Equal(2000.50m, (await db.OpeningBills.SingleAsync()).Amount);
            Assert.Equal(("R-1", 6000m), await db.Receipts.Select(r => ValueTuple.Create(r.ReceiptNumber, r.Amount)).SingleAsync());

            var matches = await db.BillAllocations.OrderBy(a => a.Id).ToListAsync();
            Assert.Equal([1, 2], matches.Select(a => a.Id));
            Assert.Equal([2000.50m, 3999.50m], matches.Select(a => a.Amount));
            Assert.Equal([(int?)1, 1], matches.Select(a => a.ReceiptId));
            Assert.Equal([null, (int?)1], matches.Select(a => a.InvoiceId));
            Assert.Equal([(int?)1, null], matches.Select(a => a.OpeningBillId));
            Assert.All(matches, a => Assert.Null(a.CreditNoteId));

            Assert.Equal(7, (await db.InvoiceCounters.SingleAsync()).LastNumber);
            Assert.Equal(1, (await db.VoucherCounters.SingleAsync(c => c.VoucherType == VoucherType.Receipt)).LastNumber);
            Assert.Equal(0, (await db.VoucherCounters.SingleAsync(c => c.VoucherType == VoucherType.CreditNote)).LastNumber);
            Assert.Empty(await db.CreditNotes.ToListAsync());
        }
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={_dbPath};Foreign Keys=True")
                .Options,
            TimeProvider.System);
}
