using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SareeBilling.Domain.Entities;

namespace SareeBilling.Infrastructure.Persistence.Configurations;

internal sealed class OpeningBillConfiguration : IEntityTypeConfiguration<OpeningBill>
{
    public void Configure(EntityTypeBuilder<OpeningBill> builder)
    {
        builder.ToTable("OpeningBills", t =>
        {
            t.HasCheckConstraint("CK_OpeningBills_Side", "\"Side\" IN (1, 2)");
            t.HasCheckConstraint("CK_OpeningBills_Amount", "CAST(\"Amount\" AS REAL) > 0");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.BillNumber).HasMaxLength(FieldLengths.InvoiceNumber).IsRequired();
        builder.Property(b => b.Amount).HasPrecision(18, 2);

        builder.HasOne(b => b.Customer)
            .WithMany(c => c.OpeningBills)
            .HasForeignKey(b => b.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => b.CustomerId);
    }
}

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("Receipts", t =>
        {
            t.HasCheckConstraint("CK_Receipts_Status", "\"Status\" IN (1, 2)");
            t.HasCheckConstraint("CK_Receipts_Mode", "\"Mode\" IN (1, 2, 3)");
            t.HasCheckConstraint("CK_Receipts_SequenceNumber", "\"SequenceNumber\" > 0");
            t.HasCheckConstraint("CK_Receipts_Amount", "CAST(\"Amount\" AS REAL) > 0");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.ReceiptNumber).HasMaxLength(FieldLengths.InvoiceNumber).IsRequired();
        builder.Property(r => r.CustomerName).HasMaxLength(FieldLengths.Name).IsRequired();
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.Reference).HasMaxLength(FieldLengths.Reference);
        builder.Property(r => r.Narration).HasMaxLength(FieldLengths.LongText);
        builder.Property(r => r.CancellationReason).HasMaxLength(FieldLengths.LongText);

        builder.HasIndex(r => r.ReceiptNumber).IsUnique();
        builder.HasIndex(r => r.ReceiptDate);
        builder.HasIndex(r => r.CustomerId);

        builder.HasOne(r => r.Customer)
            .WithMany()
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BillAllocationConfiguration : IEntityTypeConfiguration<BillAllocation>
{
    public void Configure(EntityTypeBuilder<BillAllocation> builder)
    {
        builder.ToTable("BillAllocations", t =>
        {
            // Exactly one source (receipt or opening advance) and one target (invoice or opening bill).
            t.HasCheckConstraint("CK_BillAllocations_OneSource",
                "(\"ReceiptId\" IS NOT NULL) + (\"OpeningCreditId\" IS NOT NULL) = 1");
            t.HasCheckConstraint("CK_BillAllocations_OneTarget",
                "(\"InvoiceId\" IS NOT NULL) + (\"OpeningBillId\" IS NOT NULL) = 1");
            t.HasCheckConstraint("CK_BillAllocations_Amount", "CAST(\"Amount\" AS REAL) > 0");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Amount).HasPrecision(18, 2);

        builder.HasOne(a => a.Receipt).WithMany().HasForeignKey(a => a.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.OpeningCredit).WithMany().HasForeignKey(a => a.OpeningCreditId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Invoice).WithMany().HasForeignKey(a => a.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.OpeningBill).WithMany().HasForeignKey(a => a.OpeningBillId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.CustomerId);
    }
}

internal sealed class VoucherCounterConfiguration : IEntityTypeConfiguration<VoucherCounter>
{
    public void Configure(EntityTypeBuilder<VoucherCounter> builder)
    {
        builder.ToTable("VoucherCounters", t => t.HasCheckConstraint("CK_VoucherCounters_LastNumber", "\"LastNumber\" >= 0"));
        builder.HasKey(c => c.VoucherType);
        builder.Property(c => c.VoucherType).ValueGeneratedNever();
    }
}
