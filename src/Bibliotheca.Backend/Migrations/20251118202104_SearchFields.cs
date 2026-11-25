using Bibliotheca.Backend.Data;
using Diacritics.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bibliotheca.Backend.Migrations;

/// <inheritdoc />
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class SearchFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ShelveSearch",
            table: "Shelves",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Search",
            table: "BookSeries",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "AuthorSearch",
            table: "Authors",
            type: "TEXT",
            nullable: false,
            defaultValue: "");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ShelveSearch",
            table: "Shelves");

        migrationBuilder.DropColumn(
            name: "Search",
            table: "BookSeries");

        migrationBuilder.DropColumn(
            name: "AuthorSearch",
            table: "Authors");
    }
}
