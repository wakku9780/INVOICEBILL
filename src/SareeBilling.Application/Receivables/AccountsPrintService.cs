using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Common;
using SareeBilling.Domain.Common;
using SareeBilling.Domain.Enums;

namespace SareeBilling.Application.Receivables;

/// <summary>A printable document: the PDF plus one PNG per A4 page for preview and printing.</summary>
public sealed record DocumentPrintout(
    string Title,
    bool IsCancelled,
    string SuggestedFileName,
    byte[] Pdf,
    IReadOnlyList<byte[]> PageImages);

/// <summary>The seller's details printed at the top of receipts and statements.</summary>
public sealed record CompanyHeader(string Name, string Address, string? Gstin, string? Phone);

public interface IAccountsPdfRenderer
{
    (byte[] Pdf, IReadOnlyList<byte[]> PageImages) RenderReceipt(CompanyHeader company, ReceiptDetail receipt, string amountInWords, int dpi);

    (byte[] Pdf, IReadOnlyList<byte[]> PageImages) RenderStatement(CompanyHeader company, CustomerLedger ledger, int dpi);

    (byte[] Pdf, IReadOnlyList<byte[]> PageImages) RenderOutstanding(CompanyHeader company, OutstandingReport report, int dpi);
}

public interface IAccountsPrintService
{
    Task<DocumentPrintout> ReceiptAsync(int receiptId, CancellationToken cancellationToken = default);

    Task<DocumentPrintout> StatementAsync(CustomerLedger ledger, CancellationToken cancellationToken = default);

    Task<DocumentPrintout> OutstandingAsync(OutstandingReport report, CancellationToken cancellationToken = default);
}

public sealed class AccountsPrintService(
    IReceivablesRepository receivables,
    ICompanyProfileRepository companyProfiles,
    IAccountsPdfRenderer renderer) : IAccountsPrintService
{
    public const int PrintDpi = 300;

    public async Task<DocumentPrintout> ReceiptAsync(int receiptId, CancellationToken cancellationToken = default)
    {
        var receipt = await receivables.GetReceiptAsync(receiptId, cancellationToken)
                      ?? throw new ValidationException(["This receipt no longer exists."]);
        var company = await HeaderAsync(cancellationToken);

        var (pdf, pages) = await Task.Run(
            () => renderer.RenderReceipt(company, receipt, AmountInWords.ToRupees(receipt.Amount), PrintDpi), cancellationToken);
        return new DocumentPrintout(
            $"Receipt {receipt.ReceiptNumber} - {receipt.CustomerName}",
            receipt.Status == VoucherStatus.Cancelled,
            SafeFileName($"Receipt-{receipt.ReceiptNumber}.pdf"),
            pdf,
            pages);
    }

    public async Task<DocumentPrintout> StatementAsync(CustomerLedger ledger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var company = await HeaderAsync(cancellationToken);

        var (pdf, pages) = await Task.Run(() => renderer.RenderStatement(company, ledger, PrintDpi), cancellationToken);
        return new DocumentPrintout(
            $"Statement - {ledger.CustomerName}",
            false,
            SafeFileName($"Statement-{ledger.CustomerName}-{ledger.To:yyyy-MM-dd}.pdf"),
            pdf,
            pages);
    }

    public async Task<DocumentPrintout> OutstandingAsync(OutstandingReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        var company = await HeaderAsync(cancellationToken);

        var (pdf, pages) = await Task.Run(() => renderer.RenderOutstanding(company, report, PrintDpi), cancellationToken);
        return new DocumentPrintout(
            "Outstanding receivables",
            false,
            SafeFileName($"Outstanding-{report.AsOf:yyyy-MM-dd}.pdf"),
            pdf,
            pages);
    }

    private async Task<CompanyHeader> HeaderAsync(CancellationToken cancellationToken)
    {
        var company = await companyProfiles.GetAsync(cancellationToken)
                      ?? throw new ValidationException(["Please set up your company details (Company Settings) first."]);
        return new CompanyHeader(
            company.Name,
            PostalAddress.Format(company.AddressLine1, company.AddressLine2, company.City, company.Pincode) + ", " + company.State,
            company.Gstin,
            company.Phone);
    }

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
    }
}
