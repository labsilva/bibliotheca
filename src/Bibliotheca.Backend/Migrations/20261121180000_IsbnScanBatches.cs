using Bibliotheca.Backend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Diagnostics.CodeAnalysis;

namespace Bibliotheca.Backend.Migrations;

[DbContext(typeof(BibliothecaDbContext))]
[Migration("20261121180000_IsbnScanBatches")]
[ExcludeFromCodeCoverage]
public sealed class IsbnScanBatches : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("BatchId", "BookImports", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("IsbnFailurePositions", "BookImports", type: "TEXT", defaultValue: "[]");
        migrationBuilder.AddColumn<string>("IsbnFailureReasons", "BookImports", type: "TEXT", defaultValue: "[]");
        migrationBuilder.CreateIndex("IX_BookImports_Owner_BatchId", "BookImports", new[] { "Owner", "BatchId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_BookImports_Owner_BatchId", "BookImports");
        migrationBuilder.DropColumn("BatchId", "BookImports");
        migrationBuilder.DropColumn("IsbnFailurePositions", "BookImports");
        migrationBuilder.DropColumn("IsbnFailureReasons", "BookImports");
    }
}
