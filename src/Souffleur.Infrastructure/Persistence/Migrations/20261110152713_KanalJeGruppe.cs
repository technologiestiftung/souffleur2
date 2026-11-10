using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souffleur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KanalJeGruppe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "Conversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_GroupId",
                table: "Conversations",
                column: "GroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Conversations_GroupId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Conversations");
        }
    }
}
