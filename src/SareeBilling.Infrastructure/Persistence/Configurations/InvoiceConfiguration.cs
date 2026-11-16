using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SareeBilling.Domain.Entities;

namespace SareeBilling.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices", t =>
        {
            t.HasCheckConstraint("CK_Invoices_Status", "\"Status\" IN (1, 2)");
            t.HasCheckConstraint("CK_Invoices_TaxType", "\"TaxType\" IN (1, 2)");
            t.HasCheckConstraint("CK_Invoices_SequenceNumber", "\"SequenceNumber\" > 0");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.InvoiceNumber).HasMaxLength(FieldLengths.InvoiceNumber).IsRequired();
        builder.Property(i => i.FinancialYear).HasMaxLength(FieldLengths.FinancialYear).IsRequired();
        builder.Property(i => i.CancellationReason).HasMaxLength(FieldLengths.LongText);

        // Seller snapshot
        builder.Property(i => i.SellerName).HasMaxLength(FieldLengths.Name).IsRequired();
        builder.Property(i => i.SellerAddress).HasMaxLength(FieldLengths.FullAddress).IsRequired();
        builder.Property(i => i.SellerGstin).HasMaxLength(FieldLengths.Gstin).IsRequired();
        builder.Property(i => i.SellerState).HasMaxLength(FieldLengths.State).IsRequired();
        builder.Property(i => i.SellerStateCode).HasMaxLength(FieldLengths.StateCode).IsRequired();
        builder.Property(i => i.SellerPhone).HasMaxLength(FieldLengths.Phone);
        builder.Property(i => i.SellerEmail).HasMaxLength(FieldLengths.Email);
        builder.Property(i => i.SellerBankName).HasMaxLength(FieldLengths.Name);
        builder.Property(i => i.SellerBankAccountNumber).HasMaxLength(FieldLengths.BankAccount);
        builder.Property(i => i.SellerBankIfsc).HasMaxLength(FieldLengths.Ifsc);
        builder.Property(i => i.SellerBankBranch).HasMaxLength(FieldLengths.Name);
        builder.Property(i => i.DeclarationText).HasMaxLength(FieldLengths.LongText).IsRequired();

        // Buyer snapshot
        builder.Property(i => i.BuyerName).HasMaxLength(FieldLengths.Name).IsRequired();
        builder.Property(i => i.BuyerAddress).HasMaxLength(FieldLengths.FullAddress).IsRequired();
        builder.Property(i => i.BuyerGstin).HasMaxLength(FieldLengths.Gstin);
        builder.Property(i => i.BuyerState).HasMaxLength(FieldLengths.State).IsRequired();
        builder.Property(i => i.BuyerStateCode).HasMaxLength(FieldLengths.StateCode).IsRequired();
        builder.Property(i => i.BuyerPhone).HasMaxLength(FieldLengths.Phone);

        // Optional header details
        builder.Property(i => i.BuyerOrderNumber).HasMaxLength(FieldLengths.Reference);
        builder.Property(i => i.PaymentTerms).HasMaxLength(FieldLengths.Reference);
        builder.Property(i => i.DispatchDocumentNumber).HasMaxLength(FieldLengths.Reference);
        builder.Property(i => i.DispatchedThrough).HasMaxLength(FieldLengths.Reference);
        builder.Property(i => i.Destination).HasMaxLength(FieldLengths.Reference);
        builder.Property(i => i.TermsOfDelivery).HasMaxLength(FieldLengths.LongText);

        // Money. SQLite stores decimal as exact TEXT; precision documents intent.
        builder.Property(i => i.TotalQuantity).HasPrecision(18, 3);
        builder.Property(i => i.TaxableTotal).HasPrecision(18, 2);
        builder.Property(i => i.CgstTotal).HasPrecision(18, 2);
        builder.Property(i => i.SgstTotal).HasPrecision(18, 2);
        builder.Property(i => i.IgstTotal).HasPrecision(18, 2);
        builder.Property(i => i.TotalTax).HasPrecision(18, 2);
        builder.Property(i => i.RoundOff).HasPrecision(18, 2);
        builder.Property(i => i.GrandTotal).HasPrecision(18, 2);
        builder.Property(i => i.AmountInWords).HasMaxLength(FieldLengths.LongText).IsRequired();

        builder.HasIndex(i => i.InvoiceNumber).IsUnique();
        // Uniqueness is guaranteed by the unique InvoiceNumber index; this index speeds up "highest number" lookups.
        builder.HasIndex(i => i.SequenceNumber);
        builder.HasIndex(i => i.InvoiceDate);
        builder.HasIndex(i => i.BuyerName);

        // A customer with invoices can never be deleted (only deactivated).
        builder.HasOne(i => i.Customer)
            .WithMany(c => c.Invoices)
            .HasForeignKey(i => i.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Items)
            .WithOne(item => item.Invoice)
            .HasForeignKey(item => item.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
