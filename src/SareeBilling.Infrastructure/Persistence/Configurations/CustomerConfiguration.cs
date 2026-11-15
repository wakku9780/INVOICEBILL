using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SareeBilling.Domain.Entities;

namespace SareeBilling.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(FieldLengths.Name).IsRequired();
        builder.Property(c => c.AddressLine1).HasMaxLength(FieldLengths.Address).IsRequired();
        builder.Property(c => c.AddressLine2).HasMaxLength(FieldLengths.Address);
        builder.Property(c => c.City).HasMaxLength(FieldLengths.City).IsRequired();
        builder.Property(c => c.Pincode).HasMaxLength(FieldLengths.Pincode);
        builder.Property(c => c.State).HasMaxLength(FieldLengths.State).IsRequired();
        builder.Property(c => c.StateCode).HasMaxLength(FieldLengths.StateCode).IsRequired();
        builder.Property(c => c.Gstin).HasMaxLength(FieldLengths.Gstin);
        builder.Property(c => c.Phone).HasMaxLength(FieldLengths.Phone);
        builder.Property(c => c.IsActive).HasDefaultValue(true);

        builder.HasIndex(c => c.Name);
        builder.HasIndex(c => c.Gstin);
    }
}
