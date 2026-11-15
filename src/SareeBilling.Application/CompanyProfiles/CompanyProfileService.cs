using System.Text.RegularExpressions;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Common;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Gst;

namespace SareeBilling.Application.CompanyProfiles;

public interface ICompanyProfileService
{
    /// <returns>The saved profile, or null if the company has not been set up yet.</returns>
    Task<CompanyProfileDto?> GetAsync(CancellationToken cancellationToken = default);

    /// <exception cref="ValidationException">When any field is invalid.</exception>
    Task SaveAsync(CompanyProfileDto profile, CancellationToken cancellationToken = default);
}

public sealed partial class CompanyProfileService : ICompanyProfileService
{
    public const int MaxLogoBytes = 512 * 1024;
    public const int MaxPrefixLength = 10;

    private readonly ICompanyProfileRepository _repository;

    public CompanyProfileService(ICompanyProfileRepository repository)
    {
        _repository = repository;
    }

    [GeneratedRegex("^[A-Z0-9-]+$")]
    private static partial Regex PrefixPattern();

    public async Task<CompanyProfileDto?> GetAsync(CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetAsync(cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    public async Task SaveAsync(CompanyProfileDto profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var entity = Normalize(profile);
        var errors = Validate(entity);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        await _repository.SaveAsync(entity, cancellationToken);
    }

    private static CompanyProfile Normalize(CompanyProfileDto dto)
    {
        var state = IndianStates.FindByCode(dto.StateCode?.Trim());

        return new CompanyProfile
        {
            Name = InputRules.CleanRequired(dto.Name),
            AddressLine1 = InputRules.CleanRequired(dto.AddressLine1),
            AddressLine2 = InputRules.Clean(dto.AddressLine2),
            City = InputRules.CleanRequired(dto.City),
            Pincode = InputRules.Clean(dto.Pincode),
            StateCode = state?.Code ?? string.Empty,
            State = state?.Name ?? string.Empty,
            Gstin = GstinValidator.Normalize(dto.Gstin),
            Phone = InputRules.Clean(dto.Phone),
            Email = InputRules.Clean(dto.Email),
            Logo = dto.Logo is { Length: > 0 } ? dto.Logo : null,
            InvoicePrefix = InputRules.CleanRequired(dto.InvoicePrefix).ToUpperInvariant(),
            BankName = InputRules.Clean(dto.BankName),
            BankAccountNumber = InputRules.Clean(dto.BankAccountNumber),
            BankIfsc = InputRules.Clean(dto.BankIfsc)?.ToUpperInvariant(),
            BankBranch = InputRules.Clean(dto.BankBranch),
            DeclarationText = InputRules.CleanRequired(dto.DeclarationText)
        };
    }

    private static List<string> Validate(CompanyProfile p)
    {
        var errors = new List<string>();

        InputRules.Required(errors, p.Name, "Company name", 200);
        InputRules.Required(errors, p.AddressLine1, "Address line 1", 250);
        InputRules.MaxLength(errors, p.AddressLine2, "Address line 2", 250);
        InputRules.Required(errors, p.City, "City", 100);
        InputRules.Pincode(errors, p.Pincode);
        var state = InputRules.State(errors, p.StateCode);

        if (p.Gstin.Length == 0)
        {
            errors.Add("GSTIN is required for your company.");
        }
        else
        {
            InputRules.Gstin(errors, p.Gstin, state);
        }

        InputRules.MaxLength(errors, p.Phone, "Phone", 60);
        InputRules.Email(errors, p.Email);

        if (p.InvoicePrefix.Length == 0)
        {
            errors.Add("Invoice prefix is required (for example JAS).");
        }
        else if (p.InvoicePrefix.Length > MaxPrefixLength || !PrefixPattern().IsMatch(p.InvoicePrefix))
        {
            errors.Add($"Invoice prefix can have up to {MaxPrefixLength} letters, digits or '-' (for example JAS).");
        }

        InputRules.MaxLength(errors, p.BankName, "Bank name", 200);
        InputRules.MaxLength(errors, p.BankAccountNumber, "Account number", 40);
        InputRules.Ifsc(errors, p.BankIfsc);
        InputRules.MaxLength(errors, p.BankBranch, "Branch", 200);
        InputRules.Required(errors, p.DeclarationText, "Declaration text", 1000);

        if (p.Logo is not null)
        {
            if (p.Logo.Length > MaxLogoBytes)
            {
                errors.Add($"Logo image is too large. Please use an image under {MaxLogoBytes / 1024} KB.");
            }
            else if (!IsPngOrJpeg(p.Logo))
            {
                errors.Add("Logo must be a PNG or JPG image.");
            }
        }

        return errors;
    }

    private static bool IsPngOrJpeg(byte[] bytes) =>
        bytes is [0x89, 0x50, 0x4E, 0x47, ..] || bytes is [0xFF, 0xD8, 0xFF, ..];

    private static CompanyProfileDto ToDto(CompanyProfile e) => new()
    {
        Name = e.Name,
        AddressLine1 = e.AddressLine1,
        AddressLine2 = e.AddressLine2,
        City = e.City,
        Pincode = e.Pincode,
        State = e.State,
        StateCode = e.StateCode,
        Gstin = e.Gstin,
        Phone = e.Phone,
        Email = e.Email,
        Logo = e.Logo,
        InvoicePrefix = e.InvoicePrefix,
        BankName = e.BankName,
        BankAccountNumber = e.BankAccountNumber,
        BankIfsc = e.BankIfsc,
        BankBranch = e.BankBranch,
        DeclarationText = e.DeclarationText
    };
}
