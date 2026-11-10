using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souffleur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Kuenstlersozialabgabe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArtistsSocialFundRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Percent = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistsSocialFundRates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtistsSocialFundRates_Year",
                table: "ArtistsSocialFundRates",
                column: "Year",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtistsSocialFundRates");
        }
    }
}
