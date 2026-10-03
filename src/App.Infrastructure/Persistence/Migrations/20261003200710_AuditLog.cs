using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AuditLog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AuditLogs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                UserEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                EntityType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                Changes = table.Column<string>(type: "jsonb", nullable: false),
                At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditLogs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_EntityId_At",
            table: "AuditLogs",
            columns: new[] { "EntityId", "At" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AuditLogs");
    }
}
