namespace SareeBilling.Infrastructure.Persistence.Configurations;

/// <summary>Maximum text lengths shared by all entity configurations.</summary>
internal static class FieldLengths
{
    public const int Name = 200;
    public const int Address = 250;
    public const int FullAddress = 600;
    public const int City = 100;
    public const int Pincode = 10;
    public const int State = 60;
    public const int StateCode = 2;
    public const int Gstin = 15;
    public const int Phone = 60;
    public const int Email = 150;
    public const int InvoicePrefix = 20;
    public const int InvoiceNumber = 40;
    public const int FinancialYear = 7;
    public const int BankAccount = 40;
    public const int Ifsc = 11;
    public const int Reference = 100;
    public const int Description = 300;
    public const int HsnCode = 8;
    public const int Unit = 10;
    public const int LongText = 1000;
}
