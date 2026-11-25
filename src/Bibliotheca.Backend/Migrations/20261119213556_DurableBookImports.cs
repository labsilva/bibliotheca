using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bibliotheca.Backend.Migrations;

/// <inheritdoc />
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class _20261119213556_DurableBookImports : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BookImports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Owner = table.Column<string>(type: "TEXT", nullable: false),
                Isbns = table.Column<string>(type: "TEXT", nullable: false),
                Total = table.Column<int>(type: "INTEGER", nullable: false),
                Succeeded = table.Column<int>(type: "INTEGER", nullable: false),
                Failed = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookImports", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BookImports_CompletedAtUtc_CreatedAtUtc",
            table: "BookImports",
            columns: new[] { "CompletedAtUtc", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BookImports");
    }
}
