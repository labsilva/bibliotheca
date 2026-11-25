using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bibliotheca.Backend.Migrations
{
    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public partial class UserBooksUserMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Comments",
                table: "UsersBooks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<short>(
                name: "Rating",
                table: "UsersBooks",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Comments",
                table: "UsersBooks");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "UsersBooks");
        }
    }
}
