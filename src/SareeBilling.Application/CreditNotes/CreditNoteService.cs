using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Common;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Application.CreditNotes;

public interface ICreditNoteService
{
    Task<CreditableInvoice?> GetInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);

    /// <summary>The invoice with this printed number, or null.</summary>
    Task<CreditableInvoice?> FindInvoiceAsync(string invoiceNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a credit note against one invoice, using that invoice's GST type and rates. Its value
    /// first reduces what is pending on the invoice; the rest becomes the customer's advance.
    /// </summary>
    /// <exception cref="ValidationException">When anything is invalid; nothing is saved.</exception>
    Task<CreatedCreditNote> CreateAsync(CreateCreditNoteRequest request, CancellationToken cancellationToken = default);

    Task CancelAsync(int creditNoteId, string reason, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CreditNoteSummary>> SearchAsync(CreditNoteSearchCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>Credit notes (not cancelled) against an invoice; an invoice with any cannot be cancelled.</summary>
    Task<IReadOnlyList<string>> GetActiveNumbersForInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);
}

public sealed class CreditNoteService(
    ICreditNoteRepository repository,
    ICompanyProfileRepository companyProfiles,
    TimeProvider timeProvider) : ICreditNoteService
{
    public const int MaxSearchResults = 1000;
    private const int MaxLongTextLength = 1000;

    public Task<CreditableInvoice?> GetInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default) =>
        repository.GetCreditableInvoiceAsync(invoiceId, cancellationToken);

    public async Task<CreditableInvoice?> FindInvoiceAsync(string invoiceNumber, CancellationToken cancellationToken = default)
    {
        var number = InputRules.Clean(invoiceNumber);
        if (number is null)
        {
            return null;
        }

        var id = await repository.FindInvoiceIdAsync(number, cancellationToken);
        return id is null ? null : await repository.GetCreditableInvoiceAsync(id.Value, cancellationToken);
    }

    public async Task<CreatedCreditNote> CreateAsync(CreateCreditNoteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var company = await companyProfiles.GetAsync(cancellationToken)
                      ?? throw new ValidationException(["Please set up your company details (Company Settings) first."]);

        var reason = InputRules.CleanRequired(request.Reason);
        var errors = new List<string>();
        InputRules.Required(errors, reason, "Reason for the credit note", MaxLongTextLength);
        if (request.CreditNoteDate == default)
        {
            errors.Add("Enter the credit note date.");
        }
        else if (request.CreditNoteDate > Today)
        {
            errors.Add("Credit note date cannot be in the future.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var (saved, applied) = await repository.AddAsync(request.InvoiceId, invoice =>
        {
            // Checked again inside the write transaction, against the invoice as it is now.
            var problems = new List<string>();
            if (invoice.IsCancelled)
            {
                problems.Add($"Invoice {invoice.InvoiceNumber} is cancelled, so no credit note can be made against it.");
            }

            if (request.CreditNoteDate < invoice.InvoiceDate)
            {
                problems.Add($"The credit note date cannot be before the invoice date ({invoice.InvoiceDate:dd-MM-yyyy}).");
            }

            var (calculation, lineProblems) = CreditNoteRules.Calculate(invoice, request.Kind, request.Lines);
            problems.AddRange(lineProblems);
            if (problems.Count > 0)
            {
                throw new ValidationException(problems);
            }

            return Build(request, reason, invoice, calculation!, company);
        }, cancellationToken);

        return new CreatedCreditNote(saved.Id, saved.CreditNoteNumber, saved.GrandTotal, applied, saved.GrandTotal - applied);
    }

    public async Task CancelAsync(int creditNoteId, string reason, CancellationToken cancellationToken = default)
    {
        var cleanReason = reason?.Trim() ?? string.Empty;
        var errors = new List<string>();
        InputRules.Required(errors, cleanReason, "Reason for cancelling", MaxLongTextLength);

        var note = await repository.GetAsync(creditNoteId, cancellationToken)
                   ?? throw new ValidationException(["This credit note no longer exists."]);
        if (note.Status == VoucherStatus.Cancelled)
        {
            errors.Add($"Credit note {note.CreditNoteNumber} is already cancelled.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        await repository.CancelAsync(creditNoteId, cleanReason, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }

    public Task<IReadOnlyList<CreditNoteSummary>> SearchAsync(CreditNoteSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        return repository.SearchAsync(criteria with { Text = InputRules.Clean(criteria.Text) }, MaxSearchResults, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetActiveNumbersForInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default) =>
        repository.GetActiveNumbersForInvoiceAsync(invoiceId, cancellationToken);

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    private static CreditNote Build(
        CreateCreditNoteRequest request, string reason, CreditableInvoice invoice, CreditNoteCalculation calc, CompanyProfile company)
    {
        var note = new CreditNote
        {
            CreditNoteDate = request.CreditNoteDate,
            Kind = request.Kind,
            InvoiceId = invoice.InvoiceId,
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceDate = invoice.InvoiceDate,
            CustomerId = invoice.CustomerId,
            Reason = reason,

            SellerName = company.Name,
            SellerAddress = PostalAddress.Format(company.AddressLine1, company.AddressLine2, company.City, company.Pincode),
            SellerGstin = company.Gstin,
            SellerState = company.State,
            SellerStateCode = company.StateCode,
            SellerPhone = company.Phone,
            SellerEmail = company.Email,

            // The buyer is printed exactly as on the original invoice.
            BuyerName = invoice.BuyerName,
            BuyerAddress = invoice.BuyerAddress,
            BuyerGstin = invoice.BuyerGstin,
            BuyerState = invoice.BuyerState,
            BuyerStateCode = invoice.BuyerStateCode,
            BuyerPhone = invoice.BuyerPhone,

            TaxType = invoice.TaxType,
            TotalQuantity = calc.TotalQuantity,
            TaxableTotal = calc.TaxableTotal,
            CgstTotal = calc.CgstTotal,
            SgstTotal = calc.SgstTotal,
            IgstTotal = calc.IgstTotal,
            TotalTax = calc.TotalTax,
            RoundOff = calc.RoundOff,
            GrandTotal = calc.GrandTotal,
            AmountInWords = AmountInWords.ToRupees(calc.GrandTotal),
            Status = VoucherStatus.Posted
        };

        var number = 1;
        foreach (var line in calc.Lines)
        {
            note.Items.Add(new CreditNoteItem
            {
                LineNumber = number++,
                InvoiceItemId = line.Source.InvoiceItemId,
                Description = line.Source.Description,
                HsnCode = line.Source.HsnCode,
                Quantity = line.Quantity,
                Unit = line.Source.Unit,
                Rate = line.Rate,
                TaxableAmount = line.TaxableAmount,
                GstRate = line.Source.GstRate,
                CgstRate = line.CgstRate,
                CgstAmount = line.CgstAmount,
                SgstRate = line.SgstRate,
                SgstAmount = line.SgstAmount,
                IgstRate = line.IgstRate,
                IgstAmount = line.IgstAmount,
                LineTotal = line.LineTotal
            });
        }

        return note;
    }
}
