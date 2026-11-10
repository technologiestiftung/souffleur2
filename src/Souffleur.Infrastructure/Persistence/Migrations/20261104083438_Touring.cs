using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souffleur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Touring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProductionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tours_CatalogEntries_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "CatalogEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TourMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TourId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FunctionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TourMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TourMembers_CatalogEntries_FunctionId",
                        column: x => x.FunctionId,
                        principalTable: "CatalogEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TourMembers_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TourMembers_Tours_TourId",
                        column: x => x.TourId,
                        principalTable: "Tours",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TourStops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TourId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VenueId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContactPersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ArrivalDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DepartureDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Fee = table.Column<long>(type: "INTEGER", nullable: true),
                    HostCoversTravel = table.Column<bool>(type: "INTEGER", nullable: false),
                    HostCoversAccommodation = table.Column<bool>(type: "INTEGER", nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TourStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TourStops_Contacts_ContactPersonId",
                        column: x => x.ContactPersonId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TourStops_Contacts_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TourStops_Tours_TourId",
                        column: x => x.TourId,
                        principalTable: "Tours",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TourStops_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TourPerformances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StopId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartsAt = table.Column<string>(type: "TEXT", nullable: false),
                    EndsAt = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TourPerformances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TourPerformances_TourStops_StopId",
                        column: x => x.StopId,
                        principalTable: "TourStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TourMembers_ContactId",
                table: "TourMembers",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_TourMembers_FunctionId",
                table: "TourMembers",
                column: "FunctionId");

            migrationBuilder.CreateIndex(
                name: "IX_TourMembers_TourId_ContactId",
                table: "TourMembers",
                columns: new[] { "TourId", "ContactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TourPerformances_StartsAt",
                table: "TourPerformances",
                column: "StartsAt");

            migrationBuilder.CreateIndex(
                name: "IX_TourPerformances_StopId",
                table: "TourPerformances",
                column: "StopId");

            migrationBuilder.CreateIndex(
                name: "IX_Tours_ProductionId",
                table: "Tours",
                column: "ProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_TourStops_ArrivalDate",
                table: "TourStops",
                column: "ArrivalDate");

            migrationBuilder.CreateIndex(
                name: "IX_TourStops_ContactPersonId",
                table: "TourStops",
                column: "ContactPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_TourStops_TourId",
                table: "TourStops",
                column: "TourId");

            migrationBuilder.CreateIndex(
                name: "IX_TourStops_VenueId",
                table: "TourStops",
                column: "VenueId");

            migrationBuilder.CreateIndex(
                name: "IX_TourStops_VoucherId",
                table: "TourStops",
                column: "VoucherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TourMembers");

            migrationBuilder.DropTable(
                name: "TourPerformances");

            migrationBuilder.DropTable(
                name: "TourStops");

            migrationBuilder.DropTable(
                name: "Tours");
        }
    }
}
