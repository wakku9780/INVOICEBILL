using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SareeBilling.Domain.Entities;

namespace SareeBilling.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceCounterConfiguration : IEntityTypeConfiguration<InvoiceCounter>
{
    public void Configure(EntityTypeBuilder<InvoiceCounter> builder)
    {
        builder.ToTable("InvoiceCounter", t =>
        {
            t.HasCheckConstraint("CK_InvoiceCounter_SingleRow", $"\"Id\" = {InvoiceCounter.SingletonId}");
            t.HasCheckConstraint("CK_InvoiceCounter_LastNumber", "\"LastNumber\" >= 0");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
    }
}
