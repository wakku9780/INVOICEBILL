using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SareeBilling.Domain.Entities;

namespace SareeBilling.Infrastructure.Persistence.Configurations;

internal sealed class CompanyProfileConfiguration : IEntityTypeConfiguration<CompanyProfile>
{
    public const int SingletonId = 1;

    public void Configure(EntityTypeBuilder<CompanyProfile> builder)
    {
        builder.ToTable("CompanyProfile", t =>
            t.HasCheckConstraint("CK_CompanyProfile_SingleRow", $"\"Id\" = {SingletonId}"));

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).HasMaxLength(FieldLengths.Name).IsRequired();
        builder.Property(c => c.AddressLine1).HasMaxLength(FieldLengths.Address).IsRequired();
        builder.Property(c => c.AddressLine2).HasMaxLength(FieldLengths.Address);
        builder.Property(c => c.City).HasMaxLength(FieldLengths.City).IsRequired();
        builder.Property(c => c.Pincode).HasMaxLength(FieldLengths.Pincode);
        builder.Property(c => c.State).HasMaxLength(FieldLengths.State).IsRequired();
        builder.Property(c => c.StateCode).HasMaxLength(FieldLengths.StateCode).IsRequired();
        builder.Property(c => c.Gstin).HasMaxLength(FieldLengths.Gstin).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(FieldLengths.Phone);
        builder.Property(c => c.Email).HasMaxLength(FieldLengths.Email);
        builder.Property(c => c.Logo);
        builder.Property(c => c.BankName).HasMaxLength(FieldLengths.Name);
        builder.Property(c => c.BankAccountNumber).HasMaxLength(FieldLengths.BankAccount);
        builder.Property(c => c.BankIfsc).HasMaxLength(FieldLengths.Ifsc);
        builder.Property(c => c.BankBranch).HasMaxLength(FieldLengths.Name);
        builder.Property(c => c.DeclarationText).HasMaxLength(FieldLengths.LongText).IsRequired();
    }
}
