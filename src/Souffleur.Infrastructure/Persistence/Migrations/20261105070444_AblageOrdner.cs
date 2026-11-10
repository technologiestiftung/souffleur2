using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souffleur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AblageOrdner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileModifiedAt",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FolderPath",
                table: "Documents",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MissingSince",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Module = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRestricted = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastScanAt = table.Column<string>(type: "TEXT", nullable: true),
                    LastScanResult = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentFolderGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FolderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentFolderGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentFolderGroups_DocumentFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "DocumentFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentFolderGroups_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_FolderId_FolderPath",
                table: "Documents",
                columns: new[] { "FolderId", "FolderPath" },
                unique: true,
                filter: "\"FolderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFolderGroups_FolderId_GroupId",
                table: "DocumentFolderGroups",
                columns: new[] { "FolderId", "GroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFolderGroups_GroupId",
                table: "DocumentFolderGroups",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFolders_Path",
                table: "DocumentFolders",
                column: "Path",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_DocumentFolders_FolderId",
                table: "Documents",
                column: "FolderId",
                principalTable: "DocumentFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_DocumentFolders_FolderId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "DocumentFolderGroups");

            migrationBuilder.DropTable(
                name: "DocumentFolders");

            migrationBuilder.DropIndex(
                name: "IX_Documents_FolderId_FolderPath",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "FileModifiedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "FolderPath",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "MissingSince",
                table: "Documents");
        }
    }
}
