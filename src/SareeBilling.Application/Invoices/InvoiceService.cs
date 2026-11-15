using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Common;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Enums;
using SareeBilling.Domain.Gst;

namespace SareeBilling.Application.Invoices;

public interface IInvoiceService
{
    /// <summary>
    /// Validates, calculates GST, numbers and saves the invoice.
    /// The saved invoice is final: it can be cancelled, never edited.
    /// </summary>
    /// <exception cref="ValidationException">When anything entered is invalid.</exception>
    Task<CreatedInvoice> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Marks the invoice as cancelled. It stays in history and its number is never reused.</summary>
    /// <exception cref="ValidationException">When no reason is given or it is already cancelled.</exception>
    Task CancelAsync(int invoiceId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Shows the number the next invoice on this date will probably get, or null before company setup.</summary>
    Task<string?> PreviewNextNumberAsync(DateOnly invoiceDate, CancellationToken cancellationToken = default);

    /// <summary>Invoice History search: newest first, at most <see cref="InvoiceService.MaxSearchResults"/> rows.</summary>
    Task<IReadOnlyList<InvoiceSummary>> SearchAsync(InvoiceSearchCriteria criteria, CancellationToken cancellationToken = default);
}

public sealed class InvoiceService : IInvoiceService
{
    public const int MaxSearchResults = 1000;
    private const int MaxReferenceLength = 100;
    private const int MaxLongTextLength = 1000;

    private readonly IInvoiceRepository _invoices;
    private readonly ICustomerRepository _customers;
    private readonly ICompanyProfileRepository _companyProfiles;
    private readonly TimeProvider _timeProvider;

    public InvoiceService(
        IInvoiceRepository invoices,
        ICustomerRepository customers,
        ICompanyProfileRepository companyProfiles,
        TimeProvider timeProvider)
    {
        _invoices = invoices;
        _customers = customers;
        _companyProfiles = companyProfiles;
        _timeProvider = timeProvider;
    }

    public async Task<CreatedInvoice> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var company = await _companyProfiles.GetAsync(cancellationToken)
                      ?? throw new ValidationException(["Please set up your company details (Company Settings) before creating invoices."]);

        var customer = request.CustomerId > 0
            ? await _customers.GetByIdAsync(request.CustomerId, cancellationToken)
            : null;

        var errors = Validate(request, customer);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var invoice = BuildInvoice(request, company, customer!);
        var financialYear = invoice.FinancialYear;
        var saved = await _invoices.AddWithNextNumberAsync(
            invoice,
            sequence => InvoiceNumber.Format(company.InvoicePrefix, financialYear, sequence),
            cancellationToken);

        return new CreatedInvoice(saved.Id, saved.InvoiceNumber, saved.GrandTotal);
    }

    public async Task CancelAsync(int invoiceId, string reason, CancellationToken cancellationToken = default)
    {
        var cleanReason = reason?.Trim() ?? string.Empty;
        var errors = new List<string>();
        InputRules.Required(errors, cleanReason, "Reason for cancelling", MaxLongTextLength);

        var invoice = await _invoices.GetByIdAsync(invoiceId, includeItems: false, cancellationToken)
                      ?? throw new ValidationException(["This invoice no longer exists."]);

        if (invoice.Status == InvoiceStatus.Cancelled)
        {
            errors.Add($"Invoice {invoice.InvoiceNumber} is already cancelled.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        await _invoices.MarkCancelledAsync(invoiceId, cleanReason, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }

    public async Task<string?> PreviewNextNumberAsync(DateOnly invoiceDate, CancellationToken cancellationToken = default)
    {
        var company = await _companyProfiles.GetAsync(cancellationToken);
        if (company is null)
        {
            return null;
        }

        var financialYear = FinancialYear.For(invoiceDate);
        var next = await _invoices.GetLastSequenceAsync(financialYear, cancellationToken) + 1;
        return InvoiceNumber.Format(company.InvoicePrefix, financialYear, next);
    }

    public Task<IReadOnlyList<InvoiceSummary>> SearchAsync(InvoiceSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        return _invoices.SearchAsync(criteria with { Text = InputRules.Clean(criteria.Text) }, MaxSearchResults, cancellationToken);
    }

    private List<string> Validate(CreateInvoiceRequest request, Customer? customer)
    {
        var errors = new List<string>();

        if (customer is null)
        {
            errors.Add("Please select a customer.");
        }
        else if (!customer.IsActive)
        {
            errors.Add($"{customer.Name} is marked inactive. Make it active in Customers first.");
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        if (request.InvoiceDate == default)
        {
            errors.Add("Please enter the invoice date.");
        }
        else if (request.InvoiceDate > today)
        {
            errors.Add("Invoice date cannot be in the future.");
        }

        InputRules.MaxLength(errors, InputRules.Clean(request.BuyerOrderNumber), "Buyer's order no.", MaxReferenceLength);
        InputRules.MaxLength(errors, InputRules.Clean(request.PaymentTerms), "Payment terms", MaxReferenceLength);
        InputRules.MaxLength(errors, InputRules.Clean(request.DispatchDocumentNumber), "Despatch document no.", MaxReferenceLength);
        InputRules.MaxLength(errors, InputRules.Clean(request.DispatchedThrough), "Despatched through", MaxReferenceLength);
        InputRules.MaxLength(errors, InputRules.Clean(request.Destination), "Destination", MaxReferenceLength);
        InputRules.MaxLength(errors, InputRules.Clean(request.TermsOfDelivery), "Terms of delivery", MaxLongTextLength);

        if (request.Items is null || request.Items.Count == 0)
        {
            errors.Add("Please add at least one item.");
        }
        else if (request.Items.Count > InvoiceItemRules.MaxItemsPerInvoice)
        {
            errors.Add($"An invoice can have at most {InvoiceItemRules.MaxItemsPerInvoice} items.");
        }
        else
        {
            for (var i = 0; i < request.Items.Count; i++)
            {
                errors.AddRange(InvoiceItemRules.Validate(request.Items[i]).Select(e => $"Item {i + 1}: {e}"));
            }
        }

        return errors;
    }

    private static Invoice BuildInvoice(CreateInvoiceRequest request, CompanyProfile company, Customer customer)
    {
        var items = request.Items.Select(i => i with
        {
            Description = i.Description.Trim(),
            HsnCode = i.HsnCode.Trim(),
            Unit = i.Unit.Trim()
        }).ToList();

        var gst = GstCalculator.Calculate(
            items.Select(i => new GstLineInput(i.Quantity, i.Rate, i.GstRate)).ToList(),
            company.StateCode,
            customer.StateCode);

        var invoice = new Invoice
        {
            FinancialYear = FinancialYear.For(request.InvoiceDate),
            InvoiceDate = request.InvoiceDate,
            Status = InvoiceStatus.Final,
            CustomerId = customer.Id,

            SellerName = company.Name,
            SellerAddress = PostalAddress.Format(company.AddressLine1, company.AddressLine2, company.City, company.Pincode),
            SellerGstin = company.Gstin,
            SellerState = company.State,
            SellerStateCode = company.StateCode,
            SellerPhone = company.Phone,
            SellerEmail = company.Email,
            SellerBankName = company.BankName,
            SellerBankAccountNumber = company.BankAccountNumber,
            SellerBankIfsc = company.BankIfsc,
            SellerBankBranch = company.BankBranch,
            DeclarationText = company.DeclarationText,

            BuyerName = customer.Name,
            BuyerAddress = PostalAddress.Format(customer.AddressLine1, customer.AddressLine2, customer.City, customer.Pincode),
            BuyerGstin = customer.Gstin,
            BuyerState = customer.State,
            BuyerStateCode = customer.StateCode,
            BuyerPhone = customer.Phone,

            BuyerOrderNumber = InputRules.Clean(request.BuyerOrderNumber),
            BuyerOrderDate = request.BuyerOrderDate,
            PaymentTerms = InputRules.Clean(request.PaymentTerms),
            DispatchDocumentNumber = InputRules.Clean(request.DispatchDocumentNumber),
            DispatchedThrough = InputRules.Clean(request.DispatchedThrough),
            Destination = InputRules.Clean(request.Destination),
            TermsOfDelivery = InputRules.Clean(request.TermsOfDelivery),

            TaxType = gst.TaxType,
            TotalQuantity = gst.TotalQuantity,
            TaxableTotal = gst.TaxableTotal,
            CgstTotal = gst.CgstTotal,
            SgstTotal = gst.SgstTotal,
            IgstTotal = gst.IgstTotal,
            TotalTax = gst.TotalTax,
            RoundOff = gst.RoundOff,
            GrandTotal = gst.GrandTotal,
            AmountInWords = AmountInWords.ToRupees(gst.GrandTotal)
        };

        for (var i = 0; i < items.Count; i++)
        {
            var input = items[i];
            var line = gst.Lines[i];
            invoice.Items.Add(new InvoiceItem
            {
                LineNumber = i + 1,
                Description = input.Description,
                HsnCode = input.HsnCode,
                Quantity = input.Quantity,
                Unit = input.Unit,
                Rate = input.Rate,
                TaxableAmount = line.TaxableAmount,
                GstRate = line.GstRate,
                CgstRate = line.CgstRate,
                CgstAmount = line.CgstAmount,
                SgstRate = line.SgstRate,
                SgstAmount = line.SgstAmount,
                IgstRate = line.IgstRate,
                IgstAmount = line.IgstAmount,
                LineTotal = line.LineTotal
            });
        }

        return invoice;
    }
}
