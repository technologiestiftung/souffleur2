using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souffleur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Produktionen : Migration
    {
        /// <summary>
        /// Die Produktion wird vom Katalogeintrag zum eigenen Gegenstand
        /// (ADR-0011). Die Eintraege der Liste „Produktionen" wandern mit
        /// derselben Id in die neue Tabelle, damit Tourneen, die auf sie
        /// zeigen, weiter stimmen; danach verschwinden sie aus dem Katalog.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tours_CatalogEntries_ProductionId",
                table: "Tours");

            migrationBuilder.AddColumn<Guid>(
                name: "ProductionId",
                table: "Vouchers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductionId",
                table: "TaskItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductionId",
                table: "EventSeries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductionId",
                table: "CalendarEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Productions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Subtitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PremiereDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ClosingDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Budget = table.Column<long>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Productions", x => x.Id);
                });

            // Was im Katalog stand, wird Produktion: aktiv heisst im Spielplan,
            // abgeschaltet heisst abgespielt. Der Katalog kannte kein Datum
            // und kein Budget -- die Felder bleiben leer.
            migrationBuilder.Sql("""
                INSERT INTO "Productions" ("Id", "Title", "Subtitle", "Status", "PremiereDate", "ClosingDate", "Budget", "Notes", "CreatedAt", "ModifiedAt", "CreatedBy", "ModifiedBy")
                SELECT "Id", "Label", NULL, CASE WHEN "IsActive" = 1 THEN 3 ELSE 4 END, NULL, NULL, NULL, "Description", "CreatedAt", "ModifiedAt", "CreatedBy", "ModifiedBy"
                FROM "CatalogEntries"
                WHERE "Catalog" = 7;
                """);

            migrationBuilder.CreateTable(
                name: "ProductionMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FunctionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
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
                name: "IX_Vouchers_ProductionId",
                table: "Vouchers",
                column: "ProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_ProductionId",
                table: "TaskItems",
                column: "ProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_EventSeries_ProductionId",
                table: "EventSeries",
                column: "ProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvents_ProductionId",
                table: "CalendarEvents",
                column: "ProductionId");

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

            migrationBuilder.CreateIndex(
                name: "IX_Productions_PremiereDate",
                table: "Productions",
                column: "PremiereDate");

            migrationBuilder.CreateIndex(
                name: "IX_Productions_Status",
                table: "Productions",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_CalendarEvents_Productions_ProductionId",
                table: "CalendarEvents",
                column: "ProductionId",
                principalTable: "Productions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EventSeries_Productions_ProductionId",
                table: "EventSeries",
                column: "ProductionId",
                principalTable: "Productions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_Productions_ProductionId",
                table: "TaskItems",
                column: "ProductionId",
                principalTable: "Productions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tours_Productions_ProductionId",
                table: "Tours",
                column: "ProductionId",
                principalTable: "Productions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vouchers_Productions_ProductionId",
                table: "Vouchers",
                column: "ProductionId",
                principalTable: "Productions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Erst jetzt, wenn die Tourneen auf die neue Tabelle zeigen.
            migrationBuilder.Sql("""DELETE FROM "CatalogEntries" WHERE "Catalog" = 7;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CalendarEvents_Productions_ProductionId",
                table: "CalendarEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_EventSeries_Productions_ProductionId",
                table: "EventSeries");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_Productions_ProductionId",
                table: "TaskItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Tours_Productions_ProductionId",
                table: "Tours");

            migrationBuilder.DropForeignKey(
                name: "FK_Vouchers_Productions_ProductionId",
                table: "Vouchers");

            migrationBuilder.DropTable(
                name: "ProductionMembers");

            migrationBuilder.DropTable(
                name: "Productions");

            migrationBuilder.DropIndex(
                name: "IX_Vouchers_ProductionId",
                table: "Vouchers");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_ProductionId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_EventSeries_ProductionId",
                table: "EventSeries");

            migrationBuilder.DropIndex(
                name: "IX_CalendarEvents_ProductionId",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "ProductionId",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "ProductionId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "ProductionId",
                table: "EventSeries");

            migrationBuilder.DropColumn(
                name: "ProductionId",
                table: "CalendarEvents");

            migrationBuilder.AddForeignKey(
                name: "FK_Tours_CatalogEntries_ProductionId",
                table: "Tours",
                column: "ProductionId",
                principalTable: "CatalogEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
