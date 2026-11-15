using Microsoft.EntityFrameworkCore;
using SareeBilling.Application.Abstractions;
using SareeBilling.Domain.Entities;
using SareeBilling.Infrastructure.Persistence;
using SareeBilling.Infrastructure.Persistence.Configurations;

namespace SareeBilling.Infrastructure.Repositories;

internal sealed class CompanyProfileRepository : ICompanyProfileRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public CompanyProfileRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<CompanyProfile?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.CompanyProfiles.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == CompanyProfileConfiguration.SingletonId, cancellationToken);
    }

    public async Task SaveAsync(CompanyProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Id = CompanyProfileConfiguration.SingletonId;

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.CompanyProfiles.FindAsync([profile.Id], cancellationToken);

        if (existing is null)
        {
            db.CompanyProfiles.Add(profile);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(profile);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
