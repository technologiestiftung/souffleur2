using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souffleur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProduktionLesezeichen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Die Linksammlung der Produktion wird zum Lesezeichen wie an jedem
            // anderen Gegenstand (BookmarkTarget.Production = 7). Titel wird
            // Stichwort, eine Notiz haengt sich mit Gedankenstrich an. Gleiche
            // Id, gleiche Reihenfolge -- dann faellt die Tabelle.
            migrationBuilder.Sql("""
                INSERT INTO "Bookmarks" ("Id", "Target", "TargetId", "Url", "Label", "SortOrder", "CreatedAt", "ModifiedAt", "CreatedBy", "ModifiedBy")
                SELECT "Id", 7, "ProductionId", "Url",
                       substr(CASE WHEN "Note" IS NULL OR "Note" = '' THEN "Title" ELSE "Title" || ' – ' || "Note" END, 1, 200),
                       "SortOrder", "CreatedAt", "ModifiedAt", "CreatedBy", "ModifiedBy"
                FROM "ProductionLinks";
                """);

            migrationBuilder.DropTable(
                name: "ProductionLinks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionLinks_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLinks_ProductionId",
                table: "ProductionLinks",
                column: "ProductionId");
        }
    }
}
