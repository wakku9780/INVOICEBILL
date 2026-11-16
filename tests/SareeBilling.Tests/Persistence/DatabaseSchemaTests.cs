using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Enums;
using SareeBilling.Infrastructure.Persistence;

namespace SareeBilling.Tests.Persistence;

/// <summary>
/// Applies the real migrations to a temporary SQLite file and checks that the
/// database itself enforces the core billing rules.
/// </summary>
public sealed class DatabaseSchemaTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sareebilling-test-{Guid.NewGuid():N}.db");

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            File.Delete(file);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Migration_creates_all_tables()
    {
        await using var db = CreateContext();

        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ToListAsync();

        Assert.Contains("CompanyProfile", tables);
        Assert.Contains("Customers", tables);
        Assert.Contains("Invoices", tables);
        Assert.Contains("InvoiceItems", tables);
        Assert.Contains("InvoiceCounter", tables);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Invoice_with_items_round_trips_exact_decimals_and_timestamps()
    {
        int invoiceId;
        await using (var db = CreateContext())
        {
            var invoice = NewInvoice(await AddCustomerAsync(db), "1", 1);
            invoice.Items.Add(NewItem(1, quantity: 53m, rate: 6300m));
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
        }

        await using (var db = CreateContext())
        {
            var saved = await db.Invoices.Include(i => i.Items).SingleAsync(i => i.Id == invoiceId);

            Assert.Equal(333900.00m, saved.TaxableTotal);
            Assert.Equal(16695.00m, saved.IgstTotal);
            Assert.Equal(350595.00m, saved.GrandTotal);
            Assert.Equal(new DateOnly(2026, 11, 1), saved.InvoiceDate);
            Assert.Single(saved.Items);
            Assert.Equal(53m, saved.Items.First().Quantity);
            Assert.NotEqual(default, saved.CreatedAtUtc);
        }
    }

    [Fact]
    public async Task Duplicate_invoice_number_is_rejected()
    {
        await using var db = CreateContext();
        var customer = await AddCustomerAsync(db);

        db.Invoices.Add(NewInvoice(customer, "5", 5));
        await db.SaveChangesAsync();

        db.Invoices.Add(NewInvoice(customer, "5", 6));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(1, -0.01)]
    public async Task Zero_or_negative_quantity_and_negative_rate_are_rejected(decimal quantity, decimal rate)
    {
        await using var db = CreateContext();
        var invoice = NewInvoice(await AddCustomerAsync(db), "9", 9);
        invoice.Items.Add(NewItem(1, quantity, rate));
        db.Invoices.Add(invoice);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Customer_with_invoices_cannot_be_deleted()
    {
        int customerId;
        await using (var db = CreateContext())
        {
            var customer = await AddCustomerAsync(db);
            db.Invoices.Add(NewInvoice(customer, "2", 2));
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        await using (var db = CreateContext())
        {
            db.Customers.Remove(await db.Customers.SingleAsync(c => c.Id == customerId));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={_dbPath};Foreign Keys=True")
                .Options,
            TimeProvider.System);

    private static async Task<Customer> AddCustomerAsync(AppDbContext db)
    {
        var customer = new Customer
        {
            Name = "KATARIYA TEXTILES",
            AddressLine1 = "24 Bhavani Peth Palki Chowk",
            City = "Pune",
            State = "Maharashtra",
            StateCode = "27",
            Gstin = "27AAOFK4239P1ZK"
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private static Invoice NewInvoice(Customer customer, string number, int sequence) => new()
    {
        InvoiceNumber = number,
        FinancialYear = "2026-27",
        SequenceNumber = sequence,
        InvoiceDate = new DateOnly(2026, 11, 1),
        Customer = customer,
        SellerName = "J A SAREES",
        SellerAddress = "Purani Basti Lalkunwan Mubarakpur Azamgarh",
        SellerGstin = "09ACSPH2858M1ZT",
        SellerState = "Uttar Pradesh",
        SellerStateCode = "09",
        DeclarationText = "Declaration",
        BuyerName = customer.Name,
        BuyerAddress = customer.AddressLine1,
        BuyerGstin = customer.Gstin,
        BuyerState = customer.State,
        BuyerStateCode = customer.StateCode,
        TaxType = TaxType.InterState,
        TotalQuantity = 53m,
        TaxableTotal = 333900.00m,
        IgstTotal = 16695.00m,
        TotalTax = 16695.00m,
        GrandTotal = 350595.00m,
        AmountInWords = "Three Lakh Fifty Thousand Five Hundred Ninety Five Only"
    };

    private static InvoiceItem NewItem(int line, decimal quantity, decimal rate) => new()
    {
        LineNumber = line,
        Description = "Pure Lachcha Bdr Booti Tisu Golden Zari",
        HsnCode = "5007",
        Quantity = quantity,
        Unit = "Qty",
        Rate = rate,
        TaxableAmount = quantity * rate,
        GstRate = 5m,
        IgstRate = 5m,
        IgstAmount = quantity * rate * 0.05m,
        LineTotal = quantity * rate * 1.05m
    };
}
