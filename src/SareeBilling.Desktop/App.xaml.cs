using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SareeBilling.Application;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Backups;
using SareeBilling.Desktop.Navigation;
using SareeBilling.Desktop.Services;
using SareeBilling.Desktop.ViewModels;
using SareeBilling.Infrastructure;
using SareeBilling.Infrastructure.Updates;

namespace SareeBilling.Desktop;

public partial class App : System.Windows.Application
{
    // Debug builds use a separate data folder so development never touches real business data.
#if DEBUG
    private const string DataFolderName = "SareeBilling-Dev";
#else
    private const string DataFolderName = "SareeBilling";
#endif

    private const string SingleInstanceMutexName = @"Local\SareeBilling.SingleInstance";

    // Public repository that holds ONLY installers and release notes (no source code).
    // The app reads it without any token, so nothing secret is built into SareeBilling.exe.
    private const string ReleasesOwner = "wakku9780";
    private const string ReleasesRepository = "SareeBilling-Releases";

    private Mutex? _singleInstanceMutex;
    private IHost? _host;
    private ErrorLog? _errorLog;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Two copies writing to the same database could hand out the same invoice number.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Saree Billing is already open.", "Saree Billing",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var paths = new AppPaths(DataFolderName);
        _errorLog = new ErrorLog(paths);
        RegisterGlobalExceptionHandlers();

        try
        {
            _host = CreateHost(paths, _errorLog);
            await _host.StartAsync();

            await _host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            _host.Services.GetRequiredService<INavigationService>().NavigateTo(AppPage.Dashboard);
            mainWindow.Show();

            await RunDailyBackupAsync();

            var updates = _host.Services.GetRequiredService<IUpdateCoordinator>();
            updates.ReportPreviousUpdate();
#if !DEBUG
            // Quiet check: shows a banner if a newer version exists; does nothing when offline.
            await updates.CheckQuietlyAsync();
#endif
        }
        catch (Exception ex)
        {
            _errorLog.Write("Startup failed", ex);
            MessageBox.Show(
                "Saree Billing could not start.\n\n" + ex.Message +
                "\n\nDetails were saved in:\n" + paths.LogDirectory,
                "Saree Billing", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private static IHost CreateHost(IAppPaths paths, ErrorLog errorLog)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(paths);
        builder.Services.AddGitHubUpdates(new UpdateSettings(
            ReleasesOwner,
            ReleasesRepository,
            typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0)));

        // Desktop services
        builder.Services.AddSingleton(errorLog);
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<IDocumentActions, DocumentActions>();
        builder.Services.AddSingleton<IInvoiceActions, InvoiceActions>();
        builder.Services.AddSingleton<IUpdateCoordinator, UpdateCoordinator>();

        // Window and pages (pages are transient: each visit loads fresh data)
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<CompanySettingsViewModel>();
        builder.Services.AddTransient<CustomersViewModel>();
        builder.Services.AddTransient<CreateInvoiceViewModel>();
        builder.Services.AddTransient<InvoiceHistoryViewModel>();
        builder.Services.AddTransient<BackupRestoreViewModel>();
        builder.Services.AddTransient<ReceivePaymentViewModel>();
        builder.Services.AddTransient<ReceiptsViewModel>();
        builder.Services.AddTransient<CustomerLedgerViewModel>();
        builder.Services.AddTransient<OutstandingReceivablesViewModel>();
        builder.Services.AddTransient<NewCustomerDialogViewModel>();

        return builder.Build();
    }

    /// <summary>Daily safety copy in AutoBackups. Failure must never stop the app from working.</summary>
    private async Task RunDailyBackupAsync()
    {
        try
        {
            await _host!.Services.GetRequiredService<IBackupService>().CreateDailyBackupIfNeededAsync();
        }
        catch (Exception ex)
        {
            _errorLog?.Write("Daily automatic backup failed", ex);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _errorLog?.Write("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _errorLog?.Write("Fatal unhandled exception", args.ExceptionObject as Exception);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _errorLog?.Write("Unhandled UI exception", e.Exception);
        MessageBox.Show(
            "Something went wrong:\n\n" + e.Exception.Message +
            "\n\nYour saved data is safe. If this keeps happening, send the file in the Logs folder for support.",
            "Saree Billing", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
