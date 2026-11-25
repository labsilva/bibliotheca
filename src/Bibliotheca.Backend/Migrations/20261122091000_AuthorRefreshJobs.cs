using Bibliotheca.Backend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Diagnostics.CodeAnalysis;

namespace Bibliotheca.Backend.Migrations;

[DbContext(typeof(BibliothecaDbContext))]
[Migration("20261122091000_AuthorRefreshJobs")]
[ExcludeFromCodeCoverage]
public sealed class AuthorRefreshJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("AuthorRefreshes", columns: table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            RequestedBy = table.Column<string>(type: "TEXT", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
            CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
            ActiveSlot = table.Column<int>(type: "INTEGER", nullable: true),
            LeaseOwner = table.Column<Guid>(type: "TEXT", nullable: true),
            LeaseUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
        }, constraints: table => table.PrimaryKey("PK_AuthorRefreshes", row => row.Id));
        migrationBuilder.CreateTable("AuthorRefreshItems", columns: table => new
        {
            Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
            AuthorRefreshId = table.Column<Guid>(type: "TEXT", nullable: false),
            AuthorId = table.Column<int>(type: "INTEGER", nullable: false),
            AuthorName = table.Column<string>(type: "TEXT", nullable: false),
            Status = table.Column<string>(type: "TEXT", nullable: false),
            Error = table.Column<string>(type: "TEXT", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AuthorRefreshItems", row => row.Id);
            table.ForeignKey("FK_AuthorRefreshItems_AuthorRefreshes_AuthorRefreshId", row => row.AuthorRefreshId,
                "AuthorRefreshes", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_AuthorRefreshes_ActiveSlot", "AuthorRefreshes", "ActiveSlot", unique: true);
        migrationBuilder.CreateIndex("IX_AuthorRefreshItems_AuthorRefreshId_Status_Id", "AuthorRefreshItems",
            new[] { "AuthorRefreshId", "Status", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AuthorRefreshItems");
        migrationBuilder.DropTable("AuthorRefreshes");
    }
}
