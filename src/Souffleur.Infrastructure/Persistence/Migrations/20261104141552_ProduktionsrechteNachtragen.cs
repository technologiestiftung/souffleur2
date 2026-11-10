using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Souffleur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProduktionsrechteNachtragen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Die Vorgaberechte einer Rolle werden nur beim allerersten Start
            // vergeben. Ein spaeter eingefuehrtes Recht erreicht ein bestehendes
            // Haus deshalb nie -- Touring (800) und Produktionen (900) fehlten
            // ueberall, wo die Datenbank aelter war als das Modul. Einmalig
            // nachtragen, bei den mitgelieferten Rollen, die es laut Vorgabe
            // haben: Administration, Geschaeftsfuehrung, Disposition. Nur wo es
            // fehlt, damit nichts doppelt steht.
            migrationBuilder.Sql("""
                INSERT INTO "RolePermissions" ("Id", "RoleId", "Permission", "CreatedAt")
                SELECT lower(substr(h, 1, 8) || '-' || substr(h, 9, 4) || '-' || substr(h, 13, 4) || '-' || substr(h, 17, 4) || '-' || substr(h, 21, 12)),
                       "RoleId", "Permission", strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
                FROM (
                    SELECT r."Id" AS "RoleId", p."Permission", hex(randomblob(16)) AS h
                    FROM "AspNetRoles" r
                    CROSS JOIN (SELECT 800 AS "Permission" UNION ALL SELECT 900) p
                    WHERE r."Name" IN ('Administrator', 'Management', 'Scheduling')
                      AND NOT EXISTS (
                          SELECT 1 FROM "RolePermissions" e WHERE e."RoleId" = r."Id" AND e."Permission" = p."Permission")
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
