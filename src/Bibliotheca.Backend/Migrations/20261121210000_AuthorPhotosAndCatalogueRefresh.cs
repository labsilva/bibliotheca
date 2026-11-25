using Bibliotheca.Backend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Diagnostics.CodeAnalysis;

namespace Bibliotheca.Backend.Migrations;

[DbContext(typeof(BibliothecaDbContext))]
[Migration("20261121210000_AuthorPhotosAndCatalogueRefresh")]
[ExcludeFromCodeCoverage]
public sealed class AuthorPhotosAndCatalogueRefresh : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("PhotoUrl", "Authors", type: "TEXT", nullable: true);
        migrationBuilder.CreateTable("CatalogueRefreshes", columns: table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            RequestedBy = table.Column<string>(type: "TEXT", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
            CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
            ActiveSlot = table.Column<int>(type: "INTEGER", nullable: true),
            LeaseOwner = table.Column<Guid>(type: "TEXT", nullable: true),
            LeaseUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
        }, constraints: table => table.PrimaryKey("PK_CatalogueRefreshes", row => row.Id));
        migrationBuilder.CreateTable("CatalogueRefreshItems", columns: table => new
        {
            Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
            CatalogueRefreshId = table.Column<Guid>(type: "TEXT", nullable: false),
            BookId = table.Column<int>(type: "INTEGER", nullable: false),
            Title = table.Column<string>(type: "TEXT", nullable: false),
            Status = table.Column<string>(type: "TEXT", nullable: false),
            Error = table.Column<string>(type: "TEXT", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CatalogueRefreshItems", row => row.Id);
            table.ForeignKey("FK_CatalogueRefreshItems_CatalogueRefreshes_CatalogueRefreshId", row => row.CatalogueRefreshId,
                "CatalogueRefreshes", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_CatalogueRefreshes_ActiveSlot", "CatalogueRefreshes", "ActiveSlot", unique: true);
        migrationBuilder.CreateIndex("IX_CatalogueRefreshItems_CatalogueRefreshId_Status_Id", "CatalogueRefreshItems",
            new[] { "CatalogueRefreshId", "Status", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("CatalogueRefreshItems");
        migrationBuilder.DropTable("CatalogueRefreshes");
        migrationBuilder.DropColumn("PhotoUrl", "Authors");
    }
}
