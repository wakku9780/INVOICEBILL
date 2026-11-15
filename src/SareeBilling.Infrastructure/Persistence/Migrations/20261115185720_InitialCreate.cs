using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SareeBilling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyProfile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AddressLine1 = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    AddressLine2 = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Pincode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    StateCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Gstin = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    LogoPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    InvoicePrefix = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BankAccountNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    BankIfsc = table.Column<string>(type: "TEXT", maxLength: 11, nullable: true),
                    BankBranch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DeclarationText = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProfile", x => x.Id);
                    table.CheckConstraint("CK_CompanyProfile_SingleRow", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AddressLine1 = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    AddressLine2 = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Pincode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    StateCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Gstin = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceSequences",
                columns: table => new
                {
                    FinancialYear = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    LastNumber = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSequences", x => x.FinancialYear);
                    table.CheckConstraint("CK_InvoiceSequences_LastNumber", "\"LastNumber\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FinancialYear = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false),
                    SellerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SellerAddress = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    SellerGstin = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    SellerState = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SellerStateCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    SellerPhone = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    SellerEmail = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    SellerBankName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SellerBankAccountNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    SellerBankIfsc = table.Column<string>(type: "TEXT", maxLength: 11, nullable: true),
                    SellerBankBranch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DeclarationText = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    BuyerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BuyerAddress = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    BuyerGstin = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    BuyerState = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    BuyerStateCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    BuyerPhone = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    BuyerOrderNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BuyerOrderDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PaymentTerms = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DispatchDocumentNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DispatchedThrough = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Destination = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TermsOfDelivery = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    TaxType = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    TaxableTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CgstTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SgstTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IgstTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TotalTax = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RoundOff = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AmountInWords = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.CheckConstraint("CK_Invoices_SequenceNumber", "\"SequenceNumber\" > 0");
                    table.CheckConstraint("CK_Invoices_Status", "\"Status\" IN (1, 2)");
                    table.CheckConstraint("CK_Invoices_TaxType", "\"TaxType\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_Invoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    HsnCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    GstRate = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    CgstRate = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    CgstAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SgstRate = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    SgstAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IgstRate = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    IgstAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceItems", x => x.Id);
                    table.CheckConstraint("CK_InvoiceItems_GstRate", "CAST(\"GstRate\" AS REAL) BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_InvoiceItems_LineNumber", "\"LineNumber\" > 0");
                    table.CheckConstraint("CK_InvoiceItems_Quantity", "CAST(\"Quantity\" AS REAL) > 0");
                    table.CheckConstraint("CK_InvoiceItems_Rate", "CAST(\"Rate\" AS REAL) >= 0");
                    table.ForeignKey(
                        name: "FK_InvoiceItems_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Gstin",
                table: "Customers",
                column: "Gstin");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Name",
                table: "Customers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_InvoiceId_LineNumber",
                table: "InvoiceItems",
                columns: new[] { "InvoiceId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_BuyerName",
                table: "Invoices",
                column: "BuyerName");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CustomerId",
                table: "Invoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_FinancialYear_SequenceNumber",
                table: "Invoices",
                columns: new[] { "FinancialYear", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceDate",
                table: "Invoices",
                column: "InvoiceDate");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceNumber",
                table: "Invoices",
                column: "InvoiceNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyProfile");

            migrationBuilder.DropTable(
                name: "InvoiceItems");

            migrationBuilder.DropTable(
                name: "InvoiceSequences");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
