using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SareeBilling.Domain.Entities;

namespace SareeBilling.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        // SQLite stores decimals as TEXT, and TEXT always compares greater than a number,
        // so the checks CAST to REAL to make the database itself reject bad values.
        builder.ToTable("InvoiceItems", t =>
        {
            t.HasCheckConstraint("CK_InvoiceItems_Quantity", "CAST(\"Quantity\" AS REAL) > 0");
            t.HasCheckConstraint("CK_InvoiceItems_Rate", "CAST(\"Rate\" AS REAL) >= 0");
            t.HasCheckConstraint("CK_InvoiceItems_GstRate", "CAST(\"GstRate\" AS REAL) BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_InvoiceItems_LineNumber", "\"LineNumber\" > 0");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Description).HasMaxLength(FieldLengths.Description).IsRequired();
        builder.Property(i => i.HsnCode).HasMaxLength(FieldLengths.HsnCode).IsRequired();
        builder.Property(i => i.Unit).HasMaxLength(FieldLengths.Unit).IsRequired();

        builder.Property(i => i.Quantity).HasPrecision(18, 3);
        builder.Property(i => i.Rate).HasPrecision(18, 2);
        builder.Property(i => i.TaxableAmount).HasPrecision(18, 2);
        builder.Property(i => i.GstRate).HasPrecision(5, 2);
        builder.Property(i => i.CgstRate).HasPrecision(5, 2);
        builder.Property(i => i.CgstAmount).HasPrecision(18, 2);
        builder.Property(i => i.SgstRate).HasPrecision(5, 2);
        builder.Property(i => i.SgstAmount).HasPrecision(18, 2);
        builder.Property(i => i.IgstRate).HasPrecision(5, 2);
        builder.Property(i => i.IgstAmount).HasPrecision(18, 2);
        builder.Property(i => i.LineTotal).HasPrecision(18, 2);

        builder.HasIndex(i => new { i.InvoiceId, i.LineNumber }).IsUnique();
    }
}
