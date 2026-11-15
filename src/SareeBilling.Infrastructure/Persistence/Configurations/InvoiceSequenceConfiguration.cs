using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SareeBilling.Domain.Entities;

namespace SareeBilling.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceSequenceConfiguration : IEntityTypeConfiguration<InvoiceSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceSequence> builder)
    {
        builder.ToTable("InvoiceSequences", t =>
            t.HasCheckConstraint("CK_InvoiceSequences_LastNumber", "\"LastNumber\" >= 0"));

        builder.HasKey(s => s.FinancialYear);
        builder.Property(s => s.FinancialYear).HasMaxLength(FieldLengths.FinancialYear);
    }
}
