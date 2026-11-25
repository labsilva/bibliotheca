using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bibliotheca.Backend.Migrations;

/// <inheritdoc />
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class WIP : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Search",
            table: "Books",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "SeriesId",
            table: "Books",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "BookSeries",
            columns: table => new
            {
                SeriesId = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookSeries", x => x.SeriesId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Books_Search",
            table: "Books",
            column: "Search");

        migrationBuilder.CreateIndex(
            name: "IX_Books_SeriesId",
            table: "Books",
            column: "SeriesId");

        migrationBuilder.CreateIndex(
            name: "IX_BookSeries_Name",
            table: "BookSeries",
            column: "Name",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_Books_BookSeries_SeriesId",
            table: "Books",
            column: "SeriesId",
            principalTable: "BookSeries",
            principalColumn: "SeriesId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Books_BookSeries_SeriesId",
            table: "Books");

        migrationBuilder.DropTable(
            name: "BookSeries");

        migrationBuilder.DropIndex(
            name: "IX_Books_Search",
            table: "Books");

        migrationBuilder.DropIndex(
            name: "IX_Books_SeriesId",
            table: "Books");

        migrationBuilder.DropColumn(
            name: "Search",
            table: "Books");

        migrationBuilder.DropColumn(
            name: "SeriesId",
            table: "Books");
    }
}
