using System.IO;
using System.Windows.Input;
using SareeBilling.Application.Common;
using SareeBilling.Application.CompanyProfiles;
using SareeBilling.Application.Invoices;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Desktop.Services;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Gst;

namespace SareeBilling.Desktop.ViewModels;

public sealed class CompanySettingsViewModel : PageViewModel
{
    private readonly ICompanyProfileService _service;
    private readonly IInvoiceNumberingService _numbering;
    private readonly IDialogService _dialogs;
    private readonly IUpdateCoordinator _updates;
    private string? _updateStatusText;

    private int _loadedNextNumber;
    private string _nextInvoiceNumber = string.Empty;

    private bool _isLoaded;
    private bool _isFirstSetup;
    private string _name = string.Empty;
    private string _gstin = string.Empty;
    private GstinHint _gstinHint = GstinHint.For(null);
    private string? _stateCode;
    private string _addressLine1 = string.Empty;
    private string _addressLine2 = string.Empty;
    private string _city = string.Empty;
    private string _pincode = string.Empty;
    private string _phone = string.Empty;
    private string _email = string.Empty;
    private string _declarationText = new CompanyProfile().DeclarationText;
    private string _bankName = string.Empty;
    private string _bankAccountNumber = string.Empty;
    private string _bankIfsc = string.Empty;
    private string _bankBranch = string.Empty;
    private byte[]? _logo;
    private string? _errorMessage;
    private string? _successMessage;

    public CompanySettingsViewModel(
        ICompanyProfileService service,
        IInvoiceNumberingService numbering,
        IDialogService dialogs,
        IUpdateCoordinator updates)
    {
        _service = service;
        _numbering = numbering;
        _dialogs = dialogs;
        _updates = updates;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => _isLoaded);
        ChooseLogoCommand = new RelayCommand(ChooseLogo);
        RemoveLogoCommand = new RelayCommand(() => Logo = null, () => Logo is not null);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
    }

    public override string Title => "Company Settings";

    public IReadOnlyList<IndianState> States => IndianStates.All;

    public ICommand SaveCommand { get; }
    public ICommand ChooseLogoCommand { get; }
    public ICommand RemoveLogoCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }

    public string AppVersionText => $"You are using Saree Billing version {_updates.CurrentVersionText}.";

    public string? UpdateStatusText { get => _updateStatusText; private set => SetProperty(ref _updateStatusText, value); }

    public bool IsFirstSetup
    {
        get => _isFirstSetup;
        private set => SetProperty(ref _isFirstSetup, value);
    }

    public string Name { get => _name; set => SetProperty(ref _name, value); }

    public string Gstin
    {
        get => _gstin;
        set
        {
            if (!SetProperty(ref _gstin, value))
            {
                return;
            }

            GstinHint = GstinHint.For(value);
            if (GstinHint.StateCode is not null)
            {
                StateCode = GstinHint.StateCode;
            }
        }
    }

    public GstinHint GstinHint
    {
        get => _gstinHint;
        private set => SetProperty(ref _gstinHint, value);
    }

    public string? StateCode { get => _stateCode; set => SetProperty(ref _stateCode, value); }
    public string AddressLine1 { get => _addressLine1; set => SetProperty(ref _addressLine1, value); }
    public string AddressLine2 { get => _addressLine2; set => SetProperty(ref _addressLine2, value); }
    public string City { get => _city; set => SetProperty(ref _city, value); }
    public string Pincode { get => _pincode; set => SetProperty(ref _pincode, value); }
    public string Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public string Email { get => _email; set => SetProperty(ref _email, value); }

    /// <summary>Shows staff what their invoice numbers will look like.</summary>
    public string InvoiceNumberExample
    {
        get
        {
            var next = int.TryParse(NextInvoiceNumber, out var n) && n > 0 ? n : 1;
            return $"The next invoice will be number {next}, then {next + 1}, {next + 2} …";
        }
    }

    /// <summary>Lets numbering continue after the last invoice made in another program (e.g. Tally).</summary>
    public string NextInvoiceNumber
    {
        get => _nextInvoiceNumber;
        set
        {
            if (SetProperty(ref _nextInvoiceNumber, value))
            {
                OnPropertyChanged(nameof(InvoiceNumberExample));
            }
        }
    }

    public string DeclarationText { get => _declarationText; set => SetProperty(ref _declarationText, value); }
    public string BankName { get => _bankName; set => SetProperty(ref _bankName, value); }
    public string BankAccountNumber { get => _bankAccountNumber; set => SetProperty(ref _bankAccountNumber, value); }
    public string BankIfsc { get => _bankIfsc; set => SetProperty(ref _bankIfsc, value); }
    public string BankBranch { get => _bankBranch; set => SetProperty(ref _bankBranch, value); }

    public byte[]? Logo
    {
        get => _logo;
        private set
        {
            if (SetProperty(ref _logo, value))
            {
                OnPropertyChanged(nameof(HasLogo));
            }
        }
    }

    public bool HasLogo => Logo is not null;

    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public string? SuccessMessage { get => _successMessage; private set => SetProperty(ref _successMessage, value); }

    public override async Task InitializeAsync()
    {
        var profile = await _service.GetAsync();
        IsFirstSetup = profile is null;

        if (profile is not null)
        {
            Name = profile.Name;
            Gstin = profile.Gstin;
            StateCode = profile.StateCode;
            AddressLine1 = profile.AddressLine1;
            AddressLine2 = profile.AddressLine2 ?? string.Empty;
            City = profile.City;
            Pincode = profile.Pincode ?? string.Empty;
            Phone = profile.Phone ?? string.Empty;
            Email = profile.Email ?? string.Empty;
            DeclarationText = profile.DeclarationText;
            BankName = profile.BankName ?? string.Empty;
            BankAccountNumber = profile.BankAccountNumber ?? string.Empty;
            BankIfsc = profile.BankIfsc ?? string.Empty;
            BankBranch = profile.BankBranch ?? string.Empty;
            Logo = profile.Logo;
        }

        _loadedNextNumber = await _numbering.GetNextNumberAsync();
        NextInvoiceNumber = _loadedNextNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);

        _isLoaded = true;
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task SaveAsync()
    {
        ErrorMessage = null;
        SuccessMessage = null;

        var dto = new CompanyProfileDto
        {
            Name = Name,
            Gstin = Gstin,
            StateCode = StateCode ?? string.Empty,
            AddressLine1 = AddressLine1,
            AddressLine2 = AddressLine2,
            City = City,
            Pincode = Pincode,
            Phone = Phone,
            Email = Email,
            DeclarationText = DeclarationText,
            BankName = BankName,
            BankAccountNumber = BankAccountNumber,
            BankIfsc = BankIfsc,
            BankBranch = BankBranch,
            Logo = Logo
        };

        if (!int.TryParse(NextInvoiceNumber?.Trim(), out var nextNumber))
        {
            ErrorMessage = "Please correct the following:\n• Next invoice number must be a whole number, like 162.";
            return;
        }

        try
        {
            await _service.SaveAsync(dto);
            IsFirstSetup = false;

            if (nextNumber != _loadedNextNumber)
            {
                await _numbering.SetNextNumberAsync(nextNumber);
                _loadedNextNumber = nextNumber;
            }

            SuccessMessage = "Company details saved.";
        }
        catch (ValidationException ex)
        {
            ErrorMessage = "Please correct the following:\n• " + string.Join("\n• ", ex.Errors);
        }
    }

    private void ChooseLogo()
    {
        var path = _dialogs.PickImageFile();
        if (path is null)
        {
            return;
        }

        var size = new FileInfo(path).Length;
        if (size > CompanyProfileService.MaxLogoBytes)
        {
            _dialogs.ShowError(
                $"This image is {size / 1024} KB. Please choose an image smaller than " +
                $"{CompanyProfileService.MaxLogoBytes / 1024} KB.");
            return;
        }

        Logo = File.ReadAllBytes(path);
    }

    private async Task CheckForUpdatesAsync()
    {
        UpdateStatusText = "Checking…";
        UpdateStatusText = await _updates.CheckNowAsync();
    }
}
