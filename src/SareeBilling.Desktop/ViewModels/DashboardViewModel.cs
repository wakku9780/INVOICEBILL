using System.Globalization;
using System.Windows.Input;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Backups;
using SareeBilling.Application.CompanyProfiles;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Navigation;

namespace SareeBilling.Desktop.ViewModels;

public sealed class DashboardViewModel : PageViewModel
{
    /// <summary>After this many days without a manual backup, the dashboard shows a reminder.</summary>
    private const int BackupReminderDays = 7;

    private readonly ICompanyProfileService _companyProfiles;
    private readonly IBackupService _backups;
    private readonly TimeProvider _timeProvider;
    private bool _isCompanySetupNeeded;
    private bool _isBackupOverdue;
    private string _backupReminderText = string.Empty;

    public DashboardViewModel(
        INavigationService navigation,
        IAppPaths paths,
        ICompanyProfileService companyProfiles,
        IBackupService backups,
        TimeProvider timeProvider)
    {
        _companyProfiles = companyProfiles;
        _backups = backups;
        _timeProvider = timeProvider;
        DatabasePath = paths.DatabasePath;
        OpenPageCommand = new RelayCommand<AppPage>(page => navigation.NavigateTo(page));
    }

    public override string Title => "Dashboard";

    public string DatabasePath { get; }

    /// <summary>Quick-action buttons pass the target AppPage as CommandParameter.</summary>
    public ICommand OpenPageCommand { get; }

    /// <summary>True until company details are saved; invoices cannot be made before that.</summary>
    public bool IsCompanySetupNeeded
    {
        get => _isCompanySetupNeeded;
        private set => SetProperty(ref _isCompanySetupNeeded, value);
    }

    public bool IsBackupOverdue { get => _isBackupOverdue; private set => SetProperty(ref _isBackupOverdue, value); }

    public string BackupReminderText { get => _backupReminderText; private set => SetProperty(ref _backupReminderText, value); }

    public override async Task InitializeAsync()
    {
        IsCompanySetupNeeded = await _companyProfiles.GetAsync() is null;

        // No point nagging about backups before there is any data.
        if (IsCompanySetupNeeded)
        {
            return;
        }

        var last = _backups.GetLastManualBackupTime();
        var now = _timeProvider.GetLocalNow().DateTime;
        if (last is null)
        {
            IsBackupOverdue = true;
            BackupReminderText = "You have not made a backup yet.";
        }
        else if ((now - last.Value).TotalDays >= BackupReminderDays)
        {
            IsBackupOverdue = true;
            BackupReminderText = $"Last backup was on {last.Value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)} " +
                                 $"({(int)(now - last.Value).TotalDays} days ago).";
        }
    }
}
