using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souffleur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DienstbedarfMitGruppe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "DutyRequirements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DutyRequirements_GroupId",
                table: "DutyRequirements",
                column: "GroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_DutyRequirements_Groups_GroupId",
                table: "DutyRequirements",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DutyRequirements_Groups_GroupId",
                table: "DutyRequirements");

            migrationBuilder.DropIndex(
                name: "IX_DutyRequirements_GroupId",
                table: "DutyRequirements");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "DutyRequirements");
        }
    }
}
