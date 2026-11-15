using Microsoft.EntityFrameworkCore;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.Invoices;
using SareeBilling.Domain.Entities;
using SareeBilling.Domain.Enums;
using SareeBilling.Infrastructure.Persistence;

namespace SareeBilling.Infrastructure.Repositories;

internal sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public InvoiceRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<Invoice> AddWithNextNumberAsync(
        Invoice invoice, Func<int, string> formatNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(formatNumber);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Microsoft.Data.Sqlite starts this as BEGIN IMMEDIATE: the database is locked for writing
        // from the moment the number is read until the invoice is committed, so two saves can
        // never receive the same number. If anything fails, nothing is saved.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var last = await GetLastSequenceAsync(db, invoice.FinancialYear, cancellationToken);
        var next = last + 1;

        var sequence = await db.InvoiceSequences.FindAsync([invoice.FinancialYear], cancellationToken);
        if (sequence is null)
        {
            db.InvoiceSequences.Add(new InvoiceSequence { FinancialYear = invoice.FinancialYear, LastNumber = next });
        }
        else
        {
            sequence.LastNumber = next;
        }

        invoice.SequenceNumber = next;
        invoice.InvoiceNumber = formatNumber(next);
        db.Invoices.Add(invoice);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return invoice;
    }

    public async Task<int> GetLastSequenceAsync(string financialYear, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await GetLastSequenceAsync(db, financialYear, cancellationToken);
    }

    public async Task<int> GetHighestUsedSequenceAsync(string financialYear, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await HighestUsedAsync(db, financialYear, cancellationToken);
    }

    public async Task SetLastSequenceAsync(string financialYear, int lastNumber, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var sequence = await db.InvoiceSequences.FindAsync([financialYear], cancellationToken);
        if (sequence is null)
        {
            db.InvoiceSequences.Add(new InvoiceSequence { FinancialYear = financialYear, LastNumber = lastNumber });
        }
        else
        {
            sequence.LastNumber = lastNumber;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Invoice?> GetByIdAsync(int id, bool includeItems, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<Invoice> query = db.Invoices.AsNoTracking();
        if (includeItems)
        {
            query = query.Include(i => i.Items.OrderBy(item => item.LineNumber));
        }

        return await query.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceSummary>> SearchAsync(
        InvoiceSearchCriteria criteria, int maxResults, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<Invoice> query = db.Invoices.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(criteria.Text))
        {
            var pattern = "%" + criteria.Text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(i =>
                EF.Functions.Like(i.InvoiceNumber, pattern, "\\") ||
                EF.Functions.Like(i.BuyerName, pattern, "\\") ||
                EF.Functions.Like(i.BuyerGstin, pattern, "\\"));
        }

        if (criteria.FromDate is { } from)
        {
            query = query.Where(i => i.InvoiceDate >= from);
        }

        if (criteria.ToDate is { } to)
        {
            query = query.Where(i => i.InvoiceDate <= to);
        }

        if (!criteria.IncludeCancelled)
        {
            query = query.Where(i => i.Status != InvoiceStatus.Cancelled);
        }

        // Dates are stored as yyyy-MM-dd text, so they sort correctly in SQLite.
        return await query
            .OrderByDescending(i => i.InvoiceDate)
            .ThenByDescending(i => i.Id)
            .Take(maxResults)
            .Select(i => new InvoiceSummary(
                i.Id, i.InvoiceNumber, i.InvoiceDate, i.BuyerName, i.BuyerGstin,
                i.TaxableTotal, i.TotalTax, i.GrandTotal, i.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task MarkCancelledAsync(int id, string reason, DateTime cancelledAtUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var invoice = await db.Invoices.FindAsync([id], cancellationToken)
                      ?? throw new InvalidOperationException("This invoice no longer exists.");

        invoice.Status = InvoiceStatus.Cancelled;
        invoice.CancellationReason = reason;
        invoice.CancelledAtUtc = cancelledAtUtc;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The larger of the stored counter and the highest saved invoice, so numbering stays
    /// correct even if the counter row were ever missing (e.g. an old backup).
    /// </summary>
    private static async Task<int> GetLastSequenceAsync(AppDbContext db, string financialYear, CancellationToken cancellationToken)
    {
        var stored = await db.InvoiceSequences
            .Where(s => s.FinancialYear == financialYear)
            .Select(s => (int?)s.LastNumber)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        return Math.Max(stored, await HighestUsedAsync(db, financialYear, cancellationToken));
    }

    private static async Task<int> HighestUsedAsync(AppDbContext db, string financialYear, CancellationToken cancellationToken) =>
        await db.Invoices
            .Where(i => i.FinancialYear == financialYear)
            .MaxAsync(i => (int?)i.SequenceNumber, cancellationToken) ?? 0;
}
