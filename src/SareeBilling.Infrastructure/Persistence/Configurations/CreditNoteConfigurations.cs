using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SareeBilling.Domain.Entities;

namespace SareeBilling.Infrastructure.Persistence.Configurations;

internal sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("CreditNotes", t =>
        {
            t.HasCheckConstraint("CK_CreditNotes_Status", "\"Status\" IN (1, 2)");
            t.HasCheckConstraint("CK_CreditNotes_Kind", "\"Kind\" IN (1, 2)");
            t.HasCheckConstraint("CK_CreditNotes_TaxType", "\"TaxType\" IN (1, 2)");
            t.HasCheckConstraint("CK_CreditNotes_SequenceNumber", "\"SequenceNumber\" > 0");
            t.HasCheckConstraint("CK_CreditNotes_GrandTotal", "CAST(\"GrandTotal\" AS REAL) > 0");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.CreditNoteNumber).HasMaxLength(FieldLengths.InvoiceNumber).IsRequired();
        builder.Property(c => c.InvoiceNumber).HasMaxLength(FieldLengths.InvoiceNumber).IsRequired();
        builder.Property(c => c.Reason).HasMaxLength(FieldLengths.LongText).IsRequired();

        builder.Property(c => c.SellerName).HasMaxLength(FieldLengths.Name).IsRequired();
        builder.Property(c => c.SellerAddress).HasMaxLength(FieldLengths.FullAddress).IsRequired();
        builder.Property(c => c.SellerGstin).HasMaxLength(FieldLengths.Gstin).IsRequired();
        builder.Property(c => c.SellerState).HasMaxLength(FieldLengths.State).IsRequired();
        builder.Property(c => c.SellerStateCode).HasMaxLength(FieldLengths.StateCode).IsRequired();
        builder.Property(c => c.SellerPhone).HasMaxLength(FieldLengths.Phone);
        builder.Property(c => c.SellerEmail).HasMaxLength(FieldLengths.Email);

        builder.Property(c => c.BuyerName).HasMaxLength(FieldLengths.Name).IsRequired();
        builder.Property(c => c.BuyerAddress).HasMaxLength(FieldLengths.FullAddress).IsRequired();
        builder.Property(c => c.BuyerGstin).HasMaxLength(FieldLengths.Gstin);
        builder.Property(c => c.BuyerState).HasMaxLength(FieldLengths.State).IsRequired();
        builder.Property(c => c.BuyerStateCode).HasMaxLength(FieldLengths.StateCode).IsRequired();
        builder.Property(c => c.BuyerPhone).HasMaxLength(FieldLengths.Phone);

        builder.Property(c => c.TotalQuantity).HasPrecision(18, 3);
        builder.Property(c => c.TaxableTotal).HasPrecision(18, 2);
        builder.Property(c => c.CgstTotal).HasPrecision(18, 2);
        builder.Property(c => c.SgstTotal).HasPrecision(18, 2);
        builder.Property(c => c.IgstTotal).HasPrecision(18, 2);
        builder.Property(c => c.TotalTax).HasPrecision(18, 2);
        builder.Property(c => c.RoundOff).HasPrecision(18, 2);
        builder.Property(c => c.GrandTotal).HasPrecision(18, 2);
        builder.Property(c => c.AmountInWords).HasMaxLength(FieldLengths.LongText).IsRequired();
        builder.Property(c => c.CancellationReason).HasMaxLength(FieldLengths.LongText);

        builder.HasIndex(c => c.CreditNoteNumber).IsUnique();
        builder.HasIndex(c => c.CreditNoteDate);
        builder.HasIndex(c => c.InvoiceId);
        builder.HasIndex(c => c.CustomerId);

        builder.HasOne(c => c.Invoice).WithMany().HasForeignKey(c => c.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Customer).WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Items)
            .WithOne(i => i.CreditNote)
            .HasForeignKey(i => i.CreditNoteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CreditNoteItemConfiguration : IEntityTypeConfiguration<CreditNoteItem>
{
    public void Configure(EntityTypeBuilder<CreditNoteItem> builder)
    {
        builder.ToTable("CreditNoteItems", t =>
        {
            t.HasCheckConstraint("CK_CreditNoteItems_Quantity", "CAST(\"Quantity\" AS REAL) >= 0");
            t.HasCheckConstraint("CK_CreditNoteItems_TaxableAmount", "CAST(\"TaxableAmount\" AS REAL) > 0");
            t.HasCheckConstraint("CK_CreditNoteItems_GstRate", "CAST(\"GstRate\" AS REAL) BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_CreditNoteItems_LineNumber", "\"LineNumber\" > 0");
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

        builder.HasIndex(i => new { i.CreditNoteId, i.LineNumber }).IsUnique();
        builder.HasIndex(i => i.InvoiceItemId);

        builder.HasOne(i => i.InvoiceItem).WithMany().HasForeignKey(i => i.InvoiceItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
