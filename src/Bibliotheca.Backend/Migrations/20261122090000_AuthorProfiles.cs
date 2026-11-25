using Bibliotheca.Backend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Diagnostics.CodeAnalysis;

namespace Bibliotheca.Backend.Migrations;

[DbContext(typeof(BibliothecaDbContext))]
[Migration("20261122090000_AuthorProfiles")]
[ExcludeFromCodeCoverage]
public sealed class AuthorProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("SourceAuthorKey", "Authors", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("Biography", "Authors", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("BiographySourceUrl", "Authors", type: "TEXT", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("SourceAuthorKey", "Authors");
        migrationBuilder.DropColumn("Biography", "Authors");
        migrationBuilder.DropColumn("BiographySourceUrl", "Authors");
    }
}
