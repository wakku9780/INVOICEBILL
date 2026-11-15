using SareeBilling.Domain.Entities;

namespace SareeBilling.Application.Abstractions;

public interface ICompanyProfileRepository
{
    /// <returns>The profile, or null if the company has not been set up yet.</returns>
    Task<CompanyProfile?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the profile on first save, otherwise updates it.</summary>
    Task SaveAsync(CompanyProfile profile, CancellationToken cancellationToken = default);
}
