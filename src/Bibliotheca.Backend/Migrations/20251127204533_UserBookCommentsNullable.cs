using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bibliotheca.Backend.Migrations;

/// <inheritdoc />
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class UserBookCommentsNullable : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Comments",
            table: "UsersBooks",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "TEXT");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Comments",
            table: "UsersBooks",
            type: "TEXT",
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldNullable: true);
    }
}
