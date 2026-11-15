namespace SareeBilling.Application.Abstractions;

/// <summary>
/// Creates the database on first run and applies schema upgrades on later runs.
/// </summary>
public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
