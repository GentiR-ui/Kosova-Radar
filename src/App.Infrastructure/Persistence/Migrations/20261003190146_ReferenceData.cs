using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ReferenceData : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Categories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Categories", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Cities",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Aliases = table.Column<List<string>>(type: "text[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Cities", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Categories_Slug_Kind",
            table: "Categories",
            columns: new[] { "Slug", "Kind" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Cities_Slug",
            table: "Cities",
            column: "Slug",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Categories");

        migrationBuilder.DropTable(
            name: "Cities");
    }
}
