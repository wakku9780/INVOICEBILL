namespace SareeBilling.Domain.Entities;

/// <summary>
/// One line of an invoice. All amounts are calculated once and stored.
/// Only the tax columns matching the invoice's TaxType are non-zero.
/// </summary>
public class InvoiceItem
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    /// <summary>1-based position of the line on the invoice.</summary>
    public int LineNumber { get; set; }

    public string Description { get; set; } = string.Empty;
    public string HsnCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }

    /// <summary>Unit of measure, e.g. "Qty", "Pcs", "Mtr".</summary>
    public string Unit { get; set; } = string.Empty;

    public decimal Rate { get; set; }

    /// <summary>Quantity x Rate, rounded to 2 decimals.</summary>
    public decimal TaxableAmount { get; set; }

    /// <summary>Total GST percentage for this line, e.g. 5.</summary>
    public decimal GstRate { get; set; }

    public decimal CgstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstRate { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstRate { get; set; }
    public decimal IgstAmount { get; set; }

    /// <summary>TaxableAmount + all tax on this line.</summary>
    public decimal LineTotal { get; set; }
}
