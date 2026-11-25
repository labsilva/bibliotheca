using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bibliotheca.Backend.Migrations;

/// <inheritdoc />
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class _20261121143755_ExternalEditionImports : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Books_Isbn",
            table: "Books");

        migrationBuilder.AddColumn<string>(
            name: "EditionExternalId",
            table: "BookImports",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EditionProviderId",
            table: "BookImports",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ResultBookId",
            table: "BookImports",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SelectedEdition",
            table: "BookImports",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ShelfId",
            table: "BookImports",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "BookEditionReferences",
            columns: table => new
            {
                ProviderId = table.Column<string>(type: "TEXT", nullable: false),
                ExternalId = table.Column<string>(type: "TEXT", nullable: false),
                BookId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookEditionReferences", x => new { x.ProviderId, x.ExternalId });
                table.ForeignKey(
                    name: "FK_BookEditionReferences_Books_BookId",
                    column: x => x.BookId,
                    principalTable: "Books",
                    principalColumn: "BookId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Books_Isbn",
            table: "Books",
            column: "Isbn",
            unique: true,
            filter: "Isbn <> ''");

        migrationBuilder.CreateIndex(
            name: "IX_BookEditionReferences_BookId",
            table: "BookEditionReferences",
            column: "BookId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BookEditionReferences");

        migrationBuilder.DropIndex(
            name: "IX_Books_Isbn",
            table: "Books");

        migrationBuilder.DropColumn(
            name: "EditionExternalId",
            table: "BookImports");

        migrationBuilder.DropColumn(
            name: "EditionProviderId",
            table: "BookImports");

        migrationBuilder.DropColumn(
            name: "ResultBookId",
            table: "BookImports");

        migrationBuilder.DropColumn(
            name: "SelectedEdition",
            table: "BookImports");

        migrationBuilder.DropColumn(
            name: "ShelfId",
            table: "BookImports");

        migrationBuilder.CreateIndex(
            name: "IX_Books_Isbn",
            table: "Books",
            column: "Isbn",
            unique: true);
    }
}
