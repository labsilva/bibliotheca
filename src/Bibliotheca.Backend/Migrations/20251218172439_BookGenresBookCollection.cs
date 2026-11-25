using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bibliotheca.Backend.Migrations;

/// <inheritdoc />
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class BookGenresBookCollection : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Genres_Books_BookId",
            table: "Genres");

        migrationBuilder.DropIndex(
            name: "IX_Genres_BookId",
            table: "Genres");

        migrationBuilder.DropColumn(
            name: "BookId",
            table: "Genres");

        migrationBuilder.CreateTable(
            name: "BookBookGenre",
            columns: table => new
            {
                BooksBookId = table.Column<int>(type: "INTEGER", nullable: false),
                GenresBookGenreId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookBookGenre", x => new { x.BooksBookId, x.GenresBookGenreId });
                table.ForeignKey(
                    name: "FK_BookBookGenre_Books_BooksBookId",
                    column: x => x.BooksBookId,
                    principalTable: "Books",
                    principalColumn: "BookId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_BookBookGenre_Genres_GenresBookGenreId",
                    column: x => x.GenresBookGenreId,
                    principalTable: "Genres",
                    principalColumn: "BookGenreId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BookBookGenre_GenresBookGenreId",
            table: "BookBookGenre",
            column: "GenresBookGenreId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BookBookGenre");

        migrationBuilder.AddColumn<int>(
            name: "BookId",
            table: "Genres",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Genres_BookId",
            table: "Genres",
            column: "BookId");

        migrationBuilder.AddForeignKey(
            name: "FK_Genres_Books_BookId",
            table: "Genres",
            column: "BookId",
            principalTable: "Books",
            principalColumn: "BookId");
    }
}
