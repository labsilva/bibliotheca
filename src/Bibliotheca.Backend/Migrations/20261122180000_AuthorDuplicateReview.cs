using Bibliotheca.Backend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Diagnostics.CodeAnalysis;

namespace Bibliotheca.Backend.Migrations;

[DbContext(typeof(BibliothecaDbContext))]
[Migration("20261122180000_AuthorDuplicateReview")]
[ExcludeFromCodeCoverage]
public sealed class AuthorDuplicateReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("AuthorDuplicateScans", columns: table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
            CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
            ActiveSlot = table.Column<int>(type: "INTEGER", nullable: true),
            LastAuthorId = table.Column<int>(type: "INTEGER", nullable: false),
            MaxAuthorId = table.Column<int>(type: "INTEGER", nullable: false),
            Total = table.Column<int>(type: "INTEGER", nullable: false),
            Processed = table.Column<int>(type: "INTEGER", nullable: false),
            NewSuggestions = table.Column<int>(type: "INTEGER", nullable: false),
            RequestedBy = table.Column<string>(type: "TEXT", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_AuthorDuplicateScans", row => row.Id));
        migrationBuilder.CreateTable("AuthorDuplicateSuggestions", columns: table => new
        {
            Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
            FirstAuthorId = table.Column<int>(type: "INTEGER", nullable: false),
            SecondAuthorId = table.Column<int>(type: "INTEGER", nullable: false),
            Status = table.Column<string>(type: "TEXT", nullable: false),
            ReviewedBy = table.Column<string>(type: "TEXT", nullable: true),
            ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
            KeptAuthorId = table.Column<int>(type: "INTEGER", nullable: true)
        }, constraints: table => table.PrimaryKey("PK_AuthorDuplicateSuggestions", row => row.Id));
        migrationBuilder.CreateIndex("IX_AuthorDuplicateScans_ActiveSlot", "AuthorDuplicateScans", "ActiveSlot", unique: true);
        migrationBuilder.CreateIndex("IX_AuthorDuplicateSuggestions_FirstAuthorId_SecondAuthorId", "AuthorDuplicateSuggestions",
            new[] { "FirstAuthorId", "SecondAuthorId" }, unique: true);
        migrationBuilder.CreateIndex("IX_AuthorDuplicateSuggestions_SecondAuthorId", "AuthorDuplicateSuggestions", "SecondAuthorId");
        migrationBuilder.CreateIndex("IX_AuthorDuplicateSuggestions_Status_Id", "AuthorDuplicateSuggestions", new[] { "Status", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AuthorDuplicateSuggestions");
        migrationBuilder.DropTable("AuthorDuplicateScans");
    }
}
