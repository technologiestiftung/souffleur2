using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souffleur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProduktionPlanung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Productions",
                type: "TEXT",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Research",
                table: "Productions",
                type: "TEXT",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductionBudgetLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionBudgetLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionBudgetLines_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductionLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "ProductionNeeds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantity = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsDone = table.Column<bool>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionNeeds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionNeeds_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductionRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FunctionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionRoles_CatalogEntries_FunctionId",
                        column: x => x.FunctionId,
                        principalTable: "CatalogEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRoles_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRoles_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Aus Beteiligten werden Rollen: Wer bisher als „Ensemble" mitwirkte,
            // steht jetzt auf einer Rolle, die so heisst wie seine Funktion --
            // besser als nichts, umbenennen kann das Haus. Erst danach faellt
            // die alte Tabelle.
            migrationBuilder.Sql("""
                INSERT INTO "ProductionRoles" ("Id", "ProductionId", "Name", "FunctionId", "ContactId", "Note", "SortOrder", "CreatedAt", "ModifiedAt", "CreatedBy", "ModifiedBy")
                SELECT m."Id", m."ProductionId",
                       COALESCE((SELECT e."Label" FROM "CatalogEntries" e WHERE e."Id" = m."FunctionId"), 'Mitwirkung'),
                       m."FunctionId", m."ContactId", m."Note",
                       (ROW_NUMBER() OVER (PARTITION BY m."ProductionId" ORDER BY m."CreatedAt")) * 10,
                       m."CreatedAt", m."ModifiedAt", m."CreatedBy", m."ModifiedBy"
                FROM "ProductionMembers" m;
                """);

            migrationBuilder.DropTable(
                name: "ProductionMembers");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBudgetLines_ProductionId",
                table: "ProductionBudgetLines",
                column: "ProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLinks_ProductionId",
                table: "ProductionLinks",
                column: "ProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionNeeds_ProductionId",
                table: "ProductionNeeds",
                column: "ProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRoles_ContactId",
                table: "ProductionRoles",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRoles_FunctionId",
                table: "ProductionRoles",
                column: "FunctionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRoles_ProductionId",
                table: "ProductionRoles",
                column: "ProductionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionBudgetLines");

            migrationBuilder.DropTable(
                name: "ProductionLinks");

            migrationBuilder.DropTable(
                name: "ProductionNeeds");

            migrationBuilder.DropTable(
                name: "ProductionRoles");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Productions");

            migrationBuilder.DropColumn(
                name: "Research",
                table: "Productions");

            migrationBuilder.CreateTable(
                name: "ProductionMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FunctionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProductionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionMembers_CatalogEntries_FunctionId",
                        column: x => x.FunctionId,
                        principalTable: "CatalogEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionMembers_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionMembers_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionMembers_ContactId",
                table: "ProductionMembers",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionMembers_FunctionId",
                table: "ProductionMembers",
                column: "FunctionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionMembers_ProductionId_ContactId",
                table: "ProductionMembers",
                columns: new[] { "ProductionId", "ContactId" },
                unique: true);
        }
    }
}
