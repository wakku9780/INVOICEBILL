using Microsoft.Extensions.DependencyInjection;
using SareeBilling.Application.CompanyProfiles;
using SareeBilling.Application.Customers;
using SareeBilling.Application.Dashboard;
using SareeBilling.Application.Invoices;

namespace SareeBilling.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddTransient<ICompanyProfileService, CompanyProfileService>();
        services.AddTransient<ICustomerService, CustomerService>();
        services.AddTransient<IInvoiceService, InvoiceService>();
        services.AddTransient<IInvoiceNumberingService, InvoiceNumberingService>();
        services.AddTransient<IInvoicePrintService, InvoicePrintService>();
        services.AddTransient<IDashboardService, DashboardService>();
        return services;
    }
}
