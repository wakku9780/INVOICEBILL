using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SareeBilling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreLogoInDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoPath",
                table: "CompanyProfile");

            migrationBuilder.AddColumn<byte[]>(
                name: "Logo",
                table: "CompanyProfile",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Logo",
                table: "CompanyProfile");

            migrationBuilder.AddColumn<string>(
                name: "LogoPath",
                table: "CompanyProfile",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }
    }
}
