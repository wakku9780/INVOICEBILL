using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SareeBilling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptsAndCustomerBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "CreditDays",
                table: "Customers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30);

            // Existing invoices: due 30 days after the invoice date (the default credit period).
            migrationBuilder.Sql("UPDATE \"Invoices\" SET \"DueDate\" = date(\"InvoiceDate\", '+30 days');");

            migrationBuilder.CreateTable(
                name: "OpeningBills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false),
                    BillNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    BillDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Side = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBills", x => x.Id);
                    table.CheckConstraint("CK_OpeningBills_Amount", "CAST(\"Amount\" AS REAL) > 0");
                    table.CheckConstraint("CK_OpeningBills_Side", "\"Side\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_OpeningBills_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Receipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReceiptNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ReceiptDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipts", x => x.Id);
                    table.CheckConstraint("CK_Receipts_Amount", "CAST(\"Amount\" AS REAL) > 0");
                    table.CheckConstraint("CK_Receipts_Mode", "\"Mode\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_Receipts_SequenceNumber", "\"SequenceNumber\" > 0");
                    table.CheckConstraint("CK_Receipts_Status", "\"Status\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_Receipts_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VoucherCounters",
                columns: table => new
                {
                    VoucherType = table.Column<int>(type: "INTEGER", nullable: false),
                    LastNumber = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherCounters", x => x.VoucherType);
                    table.CheckConstraint("CK_VoucherCounters_LastNumber", "\"LastNumber\" >= 0");
                });

            // Receipt numbering starts at R-1.
            migrationBuilder.Sql("INSERT INTO \"VoucherCounters\" (\"VoucherType\", \"LastNumber\") VALUES (1, 0);");

            migrationBuilder.CreateTable(
                name: "BillAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false),
                    AllocatedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ReceiptId = table.Column<int>(type: "INTEGER", nullable: true),
                    OpeningCreditId = table.Column<int>(type: "INTEGER", nullable: true),
                    InvoiceId = table.Column<int>(type: "INTEGER", nullable: true),
                    OpeningBillId = table.Column<int>(type: "INTEGER", nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillAllocations", x => x.Id);
                    table.CheckConstraint("CK_BillAllocations_Amount", "CAST(\"Amount\" AS REAL) > 0");
                    table.CheckConstraint("CK_BillAllocations_OneSource", "(\"ReceiptId\" IS NOT NULL) + (\"OpeningCreditId\" IS NOT NULL) = 1");
                    table.CheckConstraint("CK_BillAllocations_OneTarget", "(\"InvoiceId\" IS NOT NULL) + (\"OpeningBillId\" IS NOT NULL) = 1");
                    table.ForeignKey(
                        name: "FK_BillAllocations_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillAllocations_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillAllocations_OpeningBills_OpeningBillId",
                        column: x => x.OpeningBillId,
                        principalTable: "OpeningBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillAllocations_OpeningBills_OpeningCreditId",
                        column: x => x.OpeningCreditId,
                        principalTable: "OpeningBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillAllocations_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillAllocations_CustomerId",
                table: "BillAllocations",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_BillAllocations_InvoiceId",
                table: "BillAllocations",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_BillAllocations_OpeningBillId",
                table: "BillAllocations",
                column: "OpeningBillId");

            migrationBuilder.CreateIndex(
                name: "IX_BillAllocations_OpeningCreditId",
                table: "BillAllocations",
                column: "OpeningCreditId");

            migrationBuilder.CreateIndex(
                name: "IX_BillAllocations_ReceiptId",
                table: "BillAllocations",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBills_CustomerId",
                table: "OpeningBills",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_CustomerId",
                table: "Receipts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_ReceiptDate",
                table: "Receipts",
                column: "ReceiptDate");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_ReceiptNumber",
                table: "Receipts",
                column: "ReceiptNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillAllocations");

            migrationBuilder.DropTable(
                name: "VoucherCounters");

            migrationBuilder.DropTable(
                name: "OpeningBills");

            migrationBuilder.DropTable(
                name: "Receipts");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CreditDays",
                table: "Customers");
        }
    }
}
