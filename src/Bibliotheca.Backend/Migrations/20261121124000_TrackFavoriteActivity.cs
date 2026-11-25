using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bibliotheca.Backend.Migrations;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class _20261121124000_TrackFavoriteActivity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "AddedToFavoritesAt",
            table: "UsersBooks",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AddedToFavoritesAt", table: "UsersBooks");
    }
}
