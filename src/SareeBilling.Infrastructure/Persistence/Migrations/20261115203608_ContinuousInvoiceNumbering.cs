using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SareeBilling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContinuousInvoiceNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Plain running invoice numbers (1, 2, 3 ...) that never restart replace the
            // per-financial-year counters. Existing invoices keep their numbers unchanged.
            migrationBuilder.CreateTable(
                name: "InvoiceCounter",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    LastNumber = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceCounter", x => x.Id);
                    table.CheckConstraint("CK_InvoiceCounter_LastNumber", "\"LastNumber\" >= 0");
                    table.CheckConstraint("CK_InvoiceCounter_SingleRow", "\"Id\" = 1");
                });

            // Carry over the highest number already used or reserved (e.g. a "continue from
            // Tally" starting number), so no number is ever issued twice.
            migrationBuilder.Sql(
                """
                INSERT INTO "InvoiceCounter" ("Id", "LastNumber")
                SELECT 1, MAX(
                    COALESCE((SELECT MAX("LastNumber") FROM "InvoiceSequences"), 0),
                    COALESCE((SELECT MAX("SequenceNumber") FROM "Invoices"), 0));
                """);

            migrationBuilder.DropTable(
                name: "InvoiceSequences");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_FinancialYear_SequenceNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "InvoicePrefix",
                table: "CompanyProfile");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SequenceNumber",
                table: "Invoices",
                column: "SequenceNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceCounter");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_SequenceNumber",
                table: "Invoices");

            migrationBuilder.AddColumn<string>(
                name: "InvoicePrefix",
                table: "CompanyProfile",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

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

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_FinancialYear_SequenceNumber",
                table: "Invoices",
                columns: new[] { "FinancialYear", "SequenceNumber" },
                unique: true);
        }
    }
}
