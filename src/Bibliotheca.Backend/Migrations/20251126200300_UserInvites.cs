using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bibliotheca.Backend.Migrations;

/// <inheritdoc />
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class UserInvites : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UserInvites",
            columns: table => new
            {
                InviteId = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                InviteCode = table.Column<string>(type: "TEXT", nullable: false),
                Used = table.Column<bool>(type: "INTEGER", nullable: false),
                InviteExpiresOn = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserInvites", x => x.InviteId);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "UserInvites");
    }
}
