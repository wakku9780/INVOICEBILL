using Microsoft.EntityFrameworkCore;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Entities;

namespace SareeBilling.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    private readonly TimeProvider _timeProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider timeProvider)
        : base(options)
    {
        _timeProvider = timeProvider;
    }

    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<InvoiceCounter> InvoiceCounters => Set<InvoiceCounter>();
    public DbSet<OpeningBill> OpeningBills => Set<OpeningBill>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<BillAllocation> BillAllocations => Set<BillAllocation>();
    public DbSet<VoucherCounter> VoucherCounters => Set<VoucherCounter>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<CreditNoteItem> CreditNoteItems => Set<CreditNoteItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void StampTimestamps()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
                entry.Property(e => e.CreatedAtUtc).IsModified = false;
            }
        }
    }
}
