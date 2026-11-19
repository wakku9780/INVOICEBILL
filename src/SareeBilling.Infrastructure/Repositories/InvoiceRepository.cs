using Microsoft.EntityFrameworkCore;
using SareeBilling.Application.Abstractions;
using SareeBilling.Application.CreditNotes;
using SareeBilling.Application.Dashboard;
using SareeBilling.Application.Invoices;
using SareeBilling.Domain.Common;
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

    public async Task<Invoice> AddWithNextNumberAsync(Invoice invoice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Microsoft.Data.Sqlite starts this as BEGIN IMMEDIATE: the database is locked for writing
        // from the moment the number is read until the invoice is committed, so two saves can
        // never receive the same number. If anything fails, nothing is saved.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var next = await GetLastNumberAsync(db, cancellationToken) + 1;
        await SetCounterAsync(db, next, cancellationToken);

        invoice.SequenceNumber = next;
        invoice.InvoiceNumber = InvoiceNumber.Format(next);
        db.Invoices.Add(invoice);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return invoice;
    }

    public async Task<int> GetLastNumberAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await GetLastNumberAsync(db, cancellationToken);
    }

    public async Task<int> GetHighestUsedNumberAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await HighestUsedAsync(db, cancellationToken);
    }

    public async Task SetLastNumberAsync(int lastNumber, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await SetCounterAsync(db, lastNumber, cancellationToken);
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

        if (!string.IsNullOrWhiteSpace(criteria.Text) && criteria.Text.Trim().All(char.IsAsciiDigit))
        {
            // Only digits: the user is looking for an invoice number, e.g. "161" — match it exactly
            // (also older numbers such as "JAS/2026-27/161"), not every GSTIN containing those digits.
            var number = criteria.Text.Trim();
            var oldStyleSuffix = "/" + number;
            query = query.Where(i => i.InvoiceNumber == number || i.InvoiceNumber.EndsWith(oldStyleSuffix));
        }
        else if (!string.IsNullOrWhiteSpace(criteria.Text))
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
        var found = await query
            .OrderByDescending(i => i.InvoiceDate)
            .ThenByDescending(i => i.Id)
            .Take(maxResults)
            .Select(i => new InvoiceSummary(
                i.Id, i.InvoiceNumber, i.InvoiceDate, i.BuyerName, i.BuyerGstin,
                i.TaxableTotal, i.TotalTax, i.GrandTotal, i.Status))
            .ToListAsync(cancellationToken);

        var ids = found.Select(i => i.Id).ToList();
        var credited = (await db.CreditNotes.AsNoTracking()
                .Where(c => c.Status == VoucherStatus.Posted && ids.Contains(c.InvoiceId))
                .Select(c => new { c.InvoiceId, c.GrandTotal })
                .ToListAsync(cancellationToken))
            .GroupBy(c => c.InvoiceId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.GrandTotal));

        return found.Select(i => credited.TryGetValue(i.Id, out var amount) ? i with { Credited = amount } : i).ToList();
    }

    public async Task<IReadOnlyList<SalesRow>> ListSalesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Amounts are stored as text, so they are added up in memory by the dashboard service.
        return await db.Invoices.AsNoTracking()
            .Where(i => i.InvoiceDate >= from && i.InvoiceDate <= to)
            .Select(i => new SalesRow(
                i.Id, i.InvoiceNumber, i.SequenceNumber, i.InvoiceDate, i.CustomerId, i.BuyerName,
                i.TaxableTotal, i.TotalTax, i.GrandTotal, i.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task MarkCancelledAsync(int id, string reason, DateTime cancelledAtUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var invoice = await db.Invoices.FindAsync([id], cancellationToken)
                      ?? throw new InvalidOperationException("This invoice no longer exists.");

        // A credit note must always point to a valid invoice.
        var creditNotes = await CreditNoteRepository.ActiveNumbersAsync(db, id, cancellationToken);
        if (creditNotes.Count > 0)
        {
            throw new Application.Common.ValidationException([CreditNoteMessages.BlockInvoiceCancel(invoice.InvoiceNumber, creditNotes)]);
        }

        // Money already received against it stays with the customer as advance.
        await db.BillAllocations.Where(a => a.InvoiceId == id).ExecuteDeleteAsync(cancellationToken);

        invoice.Status = InvoiceStatus.Cancelled;
        invoice.CancellationReason = reason;
        invoice.CancelledAtUtc = cancelledAtUtc;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The larger of the stored counter and the highest saved invoice, so numbering stays
    /// correct even if the counter row were ever missing (e.g. an old backup).
    /// </summary>
    private static async Task<int> GetLastNumberAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var stored = await db.InvoiceCounters
            .Select(c => (int?)c.LastNumber)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        return Math.Max(stored, await HighestUsedAsync(db, cancellationToken));
    }

    private static async Task<int> HighestUsedAsync(AppDbContext db, CancellationToken cancellationToken) =>
        await db.Invoices.MaxAsync(i => (int?)i.SequenceNumber, cancellationToken) ?? 0;

    private static async Task SetCounterAsync(AppDbContext db, int lastNumber, CancellationToken cancellationToken)
    {
        var counter = await db.InvoiceCounters.FindAsync([InvoiceCounter.SingletonId], cancellationToken);
        if (counter is null)
        {
            db.InvoiceCounters.Add(new InvoiceCounter { LastNumber = lastNumber });
        }
        else
        {
            counter.LastNumber = lastNumber;
        }
    }
}
