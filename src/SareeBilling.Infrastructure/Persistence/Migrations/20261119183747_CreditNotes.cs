using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SareeBilling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreditNotes : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Only adds: two new tables, one nullable column on BillAllocations (SQLite rebuilds that
        /// table to change its check constraint, copying every row) and a counter row.
        /// No existing invoice, customer, receipt or allocation is changed.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BillAllocations_OneSource",
                table: "BillAllocations");

            migrationBuilder.AddColumn<int>(
                name: "CreditNoteId",
                table: "BillAllocations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CreditNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreditNoteNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CreditNoteDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    InvoiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SellerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SellerAddress = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    SellerGstin = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    SellerState = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SellerStateCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    SellerPhone = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    SellerEmail = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    BuyerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BuyerAddress = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    BuyerGstin = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    BuyerState = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    BuyerStateCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    BuyerPhone = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
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
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotes", x => x.Id);
                    table.CheckConstraint("CK_CreditNotes_GrandTotal", "CAST(\"GrandTotal\" AS REAL) > 0");
                    table.CheckConstraint("CK_CreditNotes_Kind", "\"Kind\" IN (1, 2)");
                    table.CheckConstraint("CK_CreditNotes_SequenceNumber", "\"SequenceNumber\" > 0");
                    table.CheckConstraint("CK_CreditNotes_Status", "\"Status\" IN (1, 2)");
                    table.CheckConstraint("CK_CreditNotes_TaxType", "\"TaxType\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_CreditNotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditNoteItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreditNoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    InvoiceItemId = table.Column<int>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_CreditNoteItems", x => x.Id);
                    table.CheckConstraint("CK_CreditNoteItems_GstRate", "CAST(\"GstRate\" AS REAL) BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_CreditNoteItems_LineNumber", "\"LineNumber\" > 0");
                    table.CheckConstraint("CK_CreditNoteItems_Quantity", "CAST(\"Quantity\" AS REAL) >= 0");
                    table.CheckConstraint("CK_CreditNoteItems_TaxableAmount", "CAST(\"TaxableAmount\" AS REAL) > 0");
                    table.ForeignKey(
                        name: "FK_CreditNoteItems_CreditNotes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "CreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CreditNoteItems_InvoiceItems_InvoiceItemId",
                        column: x => x.InvoiceItemId,
                        principalTable: "InvoiceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillAllocations_CreditNoteId",
                table: "BillAllocations",
                column: "CreditNoteId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillAllocations_OneSource",
                table: "BillAllocations",
                sql: "(\"ReceiptId\" IS NOT NULL) + (\"OpeningCreditId\" IS NOT NULL) + (\"CreditNoteId\" IS NOT NULL) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteItems_CreditNoteId_LineNumber",
                table: "CreditNoteItems",
                columns: new[] { "CreditNoteId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteItems_InvoiceItemId",
                table: "CreditNoteItems",
                column: "InvoiceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CreditNoteDate",
                table: "CreditNotes",
                column: "CreditNoteDate");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CreditNoteNumber",
                table: "CreditNotes",
                column: "CreditNoteNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CustomerId",
                table: "CreditNotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_InvoiceId",
                table: "CreditNotes",
                column: "InvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_BillAllocations_CreditNotes_CreditNoteId",
                table: "BillAllocations",
                column: "CreditNoteId",
                principalTable: "CreditNotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Credit notes are numbered CN-1, CN-2 ... and never restart.
            migrationBuilder.Sql("INSERT OR IGNORE INTO \"VoucherCounters\" (\"VoucherType\", \"LastNumber\") VALUES (2, 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BillAllocations_CreditNotes_CreditNoteId",
                table: "BillAllocations");

            migrationBuilder.DropTable(
                name: "CreditNoteItems");

            migrationBuilder.DropTable(
                name: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_BillAllocations_CreditNoteId",
                table: "BillAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillAllocations_OneSource",
                table: "BillAllocations");

            migrationBuilder.DropColumn(
                name: "CreditNoteId",
                table: "BillAllocations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillAllocations_OneSource",
                table: "BillAllocations",
                sql: "(\"ReceiptId\" IS NOT NULL) + (\"OpeningCreditId\" IS NOT NULL) = 1");
        }
    }
}
