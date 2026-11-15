using SareeBilling.Application.Customers;
using SareeBilling.Desktop.Mvvm;
using SareeBilling.Domain.Gst;

namespace SareeBilling.Desktop.ViewModels;

/// <summary>
/// Form state for adding or editing one customer. Saving is done by the owning page,
/// so the same form can be reused later (e.g. quick-add from the invoice screen).
/// </summary>
public sealed class CustomerEditorViewModel : ObservableObject
{
    private int _id;
    private string _name = string.Empty;
    private string _gstin = string.Empty;
    private GstinHint _gstinHint = GstinHint.For(null);
    private string? _stateCode;
    private string _addressLine1 = string.Empty;
    private string _addressLine2 = string.Empty;
    private string _city = string.Empty;
    private string _pincode = string.Empty;
    private string _phone = string.Empty;
    private bool _isActive = true;

    public IReadOnlyList<IndianState> States => IndianStates.All;

    public int Id
    {
        get => _id;
        private set
        {
            if (SetProperty(ref _id, value))
            {
                OnPropertyChanged(nameof(IsNew));
                OnPropertyChanged(nameof(IsExisting));
                OnPropertyChanged(nameof(Heading));
            }
        }
    }

    public bool IsNew => Id == 0;

    public bool IsExisting => !IsNew;

    public string Heading => IsNew ? "New customer" : "Edit customer";

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
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public void Load(CustomerDto customer)
    {
        Id = customer.Id;
        Name = customer.Name;
        Gstin = customer.Gstin ?? string.Empty;
        StateCode = customer.StateCode;
        AddressLine1 = customer.AddressLine1;
        AddressLine2 = customer.AddressLine2 ?? string.Empty;
        City = customer.City;
        Pincode = customer.Pincode ?? string.Empty;
        Phone = customer.Phone ?? string.Empty;
        IsActive = customer.IsActive;
    }

    public void Clear() => Load(new CustomerDto());

    public CustomerDto ToDto() => new()
    {
        Id = Id,
        Name = Name,
        Gstin = Gstin,
        StateCode = StateCode ?? string.Empty,
        AddressLine1 = AddressLine1,
        AddressLine2 = AddressLine2,
        City = City,
        Pincode = Pincode,
        Phone = Phone,
        IsActive = IsActive
    };
}
