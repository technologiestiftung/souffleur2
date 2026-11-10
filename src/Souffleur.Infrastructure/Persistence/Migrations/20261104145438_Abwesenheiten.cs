using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souffleur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Abwesenheiten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FederalState",
                table: "Organizations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3); // Berlin -- dort sitzen die Haeuser, die es schon gibt.

            migrationBuilder.CreateTable(
                name: "Absences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    IsHalfDay = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeputyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CertificateReceived = table.Column<bool>(type: "INTEGER", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedAt = table.Column<string>(type: "TEXT", nullable: true),
                    DecisionNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PayrollReportedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Absences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Absences_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Absences_Contacts_DeputyId",
                        column: x => x.DeputyId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Employments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    WeeklyHours = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    WorkDays = table.Column<int>(type: "INTEGER", nullable: false),
                    AnnualLeaveDays = table.Column<decimal>(type: "TEXT", precision: 5, scale: 1, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Employments_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HouseHolidays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    Day = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseHolidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaveAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Days = table.Column<decimal>(type: "TEXT", precision: 5, scale: 1, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveAdjustments_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Absences_ContactId_StartDate",
                table: "Absences",
                columns: new[] { "ContactId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Absences_DeputyId",
                table: "Absences",
                column: "DeputyId");

            migrationBuilder.CreateIndex(
                name: "IX_Absences_StartDate_EndDate",
                table: "Absences",
                columns: new[] { "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Absences_Status",
                table: "Absences",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Employments_ContactId_ValidFrom",
                table: "Employments",
                columns: new[] { "ContactId", "ValidFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HouseHolidays_Month_Day",
                table: "HouseHolidays",
                columns: new[] { "Month", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveAdjustments_ContactId_Year",
                table: "LeaveAdjustments",
                columns: new[] { "ContactId", "Year" });

            // Bestehende Haeuser bekommen die beiden neuen Rechte einmal an
            // Administration und Geschaeftsfuehrung -- so, wie ein neues Haus
            // sie beim ersten Start bekaeme. Der Start ergaenzt nur den
            // Administrator; die Geschaeftsfuehrung braucht diesen Nachtrag
            // (wie bei ProduktionsrechteNachtragen). Den Administrator traegt
            // er mit, damit das Recht schon vor dem naechsten Neustart da ist.
            //
            // Ueber eine Hilfstabelle, damit jede Zeile genau einen Zufallswert
            // hat; die Kennung in der Form, in der EF Guids ablegt.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE NeueRechte AS
                SELECT r.Id AS RoleId, p.Permission AS Permission, hex(randomblob(16)) AS h
                FROM AspNetRoles r
                CROSS JOIN (SELECT 420 AS Permission UNION ALL SELECT 430) p
                WHERE r.Name IN ('Administrator', 'Management')
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp
                                  WHERE rp.RoleId = r.Id AND rp.Permission = p.Permission);

                INSERT INTO RolePermissions (Id, RoleId, Permission, CreatedAt)
                SELECT substr(h, 1, 8) || '-' || substr(h, 9, 4) || '-' || substr(h, 13, 4) || '-'
                       || substr(h, 17, 4) || '-' || substr(h, 21, 12),
                       RoleId, Permission, strftime('%Y-%m-%dT%H:%M:%S.0000000Z', 'now')
                FROM NeueRechte;

                DROP TABLE NeueRechte;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Absences");

            migrationBuilder.DropTable(
                name: "Employments");

            migrationBuilder.DropTable(
                name: "HouseHolidays");

            migrationBuilder.DropTable(
                name: "LeaveAdjustments");

            migrationBuilder.Sql("DELETE FROM RolePermissions WHERE Permission IN (420, 430);");

            migrationBuilder.DropColumn(
                name: "FederalState",
                table: "Organizations");
        }
    }
}
