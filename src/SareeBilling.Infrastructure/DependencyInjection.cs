using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SareeBilling.Application.Abstractions;
using SareeBilling.Infrastructure.Persistence;
using SareeBilling.Infrastructure.Repositories;

namespace SareeBilling.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IAppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);

        // A desktop app keeps screens open for a long time, so each operation
        // creates its own short-lived DbContext from the factory.
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(SqliteConnectionStrings.For(paths.DatabasePath)));

        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddTransient<ICompanyProfileRepository, CompanyProfileRepository>();
        services.AddTransient<ICustomerRepository, CustomerRepository>();
        services.AddTransient<IInvoiceRepository, InvoiceRepository>();

        return services;
    }
}
