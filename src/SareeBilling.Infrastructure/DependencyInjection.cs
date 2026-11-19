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
        services.AddTransient<IReceivablesRepository, ReceivablesRepository>();
        services.AddTransient<ICreditNoteRepository, CreditNoteRepository>();
        services.AddSingleton<IInvoicePdfRenderer, Pdf.QuestPdfInvoiceRenderer>();
        services.AddSingleton<Application.Receivables.IAccountsPdfRenderer, Pdf.QuestPdfAccountsRenderer>();
        services.AddSingleton<Application.Backups.IBackupService, Backup.SqliteBackupService>();

        return services;
    }

    /// <summary>Update checks against a public GitHub releases repository.</summary>
    public static IServiceCollection AddGitHubUpdates(this IServiceCollection services, Updates.UpdateSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<Application.Updates.IUpdateService, Updates.GitHubUpdateService>();
        return services;
    }
}
