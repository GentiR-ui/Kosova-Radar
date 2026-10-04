using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AiCalls : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AiCalls",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                RequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Success = table.Column<bool>(type: "boolean", nullable: false),
                ErrorCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Confidence = table.Column<double>(type: "double precision", nullable: true),
                InputTokens = table.Column<int>(type: "integer", nullable: true),
                OutputTokens = table.Column<int>(type: "integer", nullable: true),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DurationMs = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiCalls", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AiCalls_CandidateId",
            table: "AiCalls",
            column: "CandidateId");

        migrationBuilder.CreateIndex(
            name: "IX_AiCalls_StartedAt",
            table: "AiCalls",
            column: "StartedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AiCalls");
    }
}
