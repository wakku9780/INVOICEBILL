using System.Windows.Input;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.CompanyProfiles;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Navigation;

namespace SareeBilling.Desktop.ViewModels;

public sealed class DashboardViewModel : PageViewModel
{
    private readonly ICompanyProfileService _companyProfiles;
    private bool _isCompanySetupNeeded;

    public DashboardViewModel(INavigationService navigation, IAppPaths paths, ICompanyProfileService companyProfiles)
    {
        _companyProfiles = companyProfiles;
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

    public override async Task InitializeAsync()
    {
        IsCompanySetupNeeded = await _companyProfiles.GetAsync() is null;
    }
}
