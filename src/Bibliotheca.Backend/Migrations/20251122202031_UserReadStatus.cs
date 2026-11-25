using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bibliotheca.Backend.Migrations;

/// <inheritdoc />
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class UserReadStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReadingStatus",
            table: "Books");

        migrationBuilder.DropColumn(
            name: "ReadingTimestamp",
            table: "Books");

        migrationBuilder.AddColumn<DateTime>(
            name: "ModifiedOn",
            table: "UsersBooks",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ModifiedOn",
            table: "UsersBooks");

        migrationBuilder.AddColumn<string>(
            name: "ReadingStatus",
            table: "Books",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<DateTime>(
            name: "ReadingTimestamp",
            table: "Books",
            type: "TEXT",
            nullable: true);
    }
}
