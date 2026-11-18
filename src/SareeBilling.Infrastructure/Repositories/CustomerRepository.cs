using Microsoft.EntityFrameworkCore;
using SareeBilling.Application.Abstractions;
using SareeBilling.Domain.Entities;
using SareeBilling.Infrastructure.Persistence;

namespace SareeBilling.Infrastructure.Repositories;

internal sealed class CustomerRepository : ICustomerRepository
{
    private const string LikeEscape = "\\";

    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public CustomerRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<Customer>> SearchAsync(
        string? searchText, bool includeInactive, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<Customer> query = db.Customers.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            // SQLite LIKE is case-insensitive for English letters.
            var pattern = "%" + EscapeLike(searchText.Trim()) + "%";
            query = query.Where(c =>
                EF.Functions.Like(c.Name, pattern, LikeEscape) ||
                EF.Functions.Like(c.Gstin, pattern, LikeEscape) ||
                EF.Functions.Like(c.City, pattern, LikeEscape) ||
                EF.Functions.Like(c.Phone, pattern, LikeEscape));
        }

        return await query.OrderBy(c => c.Name.ToLower()).ToListAsync(cancellationToken);
    }

    public async Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<bool> GstinInUseAsync(string gstin, int excludeCustomerId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Customers.AnyAsync(c => c.Gstin == gstin && c.Id != excludeCustomerId, cancellationToken);
    }

    public async Task<bool> HasInvoicesAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Invoices.AnyAsync(i => i.CustomerId == customerId, cancellationToken)
               || await db.Receipts.AnyAsync(r => r.CustomerId == customerId, cancellationToken)
               || await db.OpeningBills.AnyAsync(b => b.CustomerId == customerId, cancellationToken);
    }

    public async Task<int> AddAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        return customer.Id;
    }

    public async Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Customers.FindAsync([customer.Id], cancellationToken)
                       ?? throw new InvalidOperationException("This customer no longer exists.");

        db.Entry(existing).CurrentValues.SetValues(customer);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Customers.FindAsync([customerId], cancellationToken);
        if (existing is not null)
        {
            db.Customers.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static string EscapeLike(string value) =>
        value.Replace(LikeEscape, LikeEscape + LikeEscape)
             .Replace("%", LikeEscape + "%")
             .Replace("_", LikeEscape + "_");
}
