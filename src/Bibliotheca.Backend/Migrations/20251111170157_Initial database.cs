using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bibliotheca.Backend.Migrations;

/// <inheritdoc />
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class Initialdatabase : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Authors",
            columns: table => new
            {
                AuthorId = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                AuthorName = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Authors", x => x.AuthorId);
            });

        migrationBuilder.CreateTable(
            name: "Collections",
            columns: table => new
            {
                CollectionId = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Collections", x => x.CollectionId);
            });

        migrationBuilder.CreateTable(
            name: "OpenIddictApplications",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                ApplicationType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                ClientId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                ClientSecret = table.Column<string>(type: "TEXT", nullable: true),
                ClientType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                ConcurrencyToken = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                ConsentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                DisplayName = table.Column<string>(type: "TEXT", nullable: true),
                DisplayNames = table.Column<string>(type: "TEXT", nullable: true),
                JsonWebKeySet = table.Column<string>(type: "TEXT", nullable: true),
                Permissions = table.Column<string>(type: "TEXT", nullable: true),
                PostLogoutRedirectUris = table.Column<string>(type: "TEXT", nullable: true),
                Properties = table.Column<string>(type: "TEXT", nullable: true),
                RedirectUris = table.Column<string>(type: "TEXT", nullable: true),
                Requirements = table.Column<string>(type: "TEXT", nullable: true),
                Settings = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OpenIddictApplications", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "OpenIddictScopes",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                ConcurrencyToken = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                Descriptions = table.Column<string>(type: "TEXT", nullable: true),
                DisplayName = table.Column<string>(type: "TEXT", nullable: true),
                DisplayNames = table.Column<string>(type: "TEXT", nullable: true),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                Properties = table.Column<string>(type: "TEXT", nullable: true),
                Resources = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OpenIddictScopes", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Publishers",
            columns: table => new
            {
                PublisherId = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Publishers", x => x.PublisherId);
            });

        migrationBuilder.CreateTable(
            name: "Tags",
            columns: table => new
            {
                TagId = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Tag = table.Column<string>(type: "TEXT", nullable: false),
                TagSearch = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tags", x => x.TagId);
            });

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Email = table.Column<string>(type: "TEXT", nullable: false),
                FullName = table.Column<string>(type: "TEXT", nullable: false),
                Password = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "OpenIddictAuthorizations",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                ApplicationId = table.Column<string>(type: "TEXT", nullable: true),
                ConcurrencyToken = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                CreationDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                Properties = table.Column<string>(type: "TEXT", nullable: true),
                Scopes = table.Column<string>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                Subject = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                Type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OpenIddictAuthorizations", x => x.Id);
                table.ForeignKey(
                    name: "FK_OpenIddictAuthorizations_OpenIddictApplications_ApplicationId",
                    column: x => x.ApplicationId,
                    principalTable: "OpenIddictApplications",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "Books",
            columns: table => new
            {
                BookId = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Isbn = table.Column<string>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                Subtitle = table.Column<string>(type: "TEXT", nullable: true),
                AdditionalData = table.Column<string>(type: "TEXT", nullable: true),
                CollectionId = table.Column<int>(type: "INTEGER", nullable: true),
                PublisherId = table.Column<int>(type: "INTEGER", nullable: true),
                PublishedOn = table.Column<DateTime>(type: "TEXT", nullable: true),
                ReadingStatus = table.Column<string>(type: "TEXT", nullable: false),
                ReadingTimestamp = table.Column<DateTime>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                ModifiedOn = table.Column<DateTime>(type: "TEXT", nullable: false),
                ModifiedBy = table.Column<string>(type: "TEXT", nullable: false),
                LastMetadataRefreshOn = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Books", x => x.BookId);
                table.ForeignKey(
                    name: "FK_Books_Collections_CollectionId",
                    column: x => x.CollectionId,
                    principalTable: "Collections",
                    principalColumn: "CollectionId");
                table.ForeignKey(
                    name: "FK_Books_Publishers_PublisherId",
                    column: x => x.PublisherId,
                    principalTable: "Publishers",
                    principalColumn: "PublisherId");
            });

        migrationBuilder.CreateTable(
            name: "Shelves",
            columns: table => new
            {
                ShelveId = table.Column<int>(type: "INTEGER", nullable: false),
                UserId = table.Column<int>(type: "INTEGER", nullable: false),
                ShelveName = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Shelves", x => new { x.ShelveId, x.UserId });
                table.ForeignKey(
                    name: "FK_Shelves_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "OpenIddictTokens",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                ApplicationId = table.Column<string>(type: "TEXT", nullable: true),
                AuthorizationId = table.Column<string>(type: "TEXT", nullable: true),
                ConcurrencyToken = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                CreationDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                ExpirationDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                Payload = table.Column<string>(type: "TEXT", nullable: true),
                Properties = table.Column<string>(type: "TEXT", nullable: true),
                RedemptionDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                ReferenceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                Subject = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                Type = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OpenIddictTokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_OpenIddictTokens_OpenIddictApplications_ApplicationId",
                    column: x => x.ApplicationId,
                    principalTable: "OpenIddictApplications",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_OpenIddictTokens_OpenIddictAuthorizations_AuthorizationId",
                    column: x => x.AuthorizationId,
                    principalTable: "OpenIddictAuthorizations",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "AuthorBook",
            columns: table => new
            {
                AuthorsAuthorId = table.Column<int>(type: "INTEGER", nullable: false),
                BooksBookId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuthorBook", x => new { x.AuthorsAuthorId, x.BooksBookId });
                table.ForeignKey(
                    name: "FK_AuthorBook_Authors_AuthorsAuthorId",
                    column: x => x.AuthorsAuthorId,
                    principalTable: "Authors",
                    principalColumn: "AuthorId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AuthorBook_Books_BooksBookId",
                    column: x => x.BooksBookId,
                    principalTable: "Books",
                    principalColumn: "BookId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BookBookTag",
            columns: table => new
            {
                BooksBookId = table.Column<int>(type: "INTEGER", nullable: false),
                TagsTagId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookBookTag", x => new { x.BooksBookId, x.TagsTagId });
                table.ForeignKey(
                    name: "FK_BookBookTag_Books_BooksBookId",
                    column: x => x.BooksBookId,
                    principalTable: "Books",
                    principalColumn: "BookId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_BookBookTag_Tags_TagsTagId",
                    column: x => x.TagsTagId,
                    principalTable: "Tags",
                    principalColumn: "TagId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BookCover",
            columns: table => new
            {
                BookId = table.Column<int>(type: "INTEGER", nullable: false),
                MimeType = table.Column<string>(type: "TEXT", nullable: false),
                Cover = table.Column<byte[]>(type: "BLOB", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookCover", x => x.BookId);
                table.ForeignKey(
                    name: "FK_BookCover_Books_BookId",
                    column: x => x.BookId,
                    principalTable: "Books",
                    principalColumn: "BookId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BookExternalLinks",
            columns: table => new
            {
                ExternalLinkId = table.Column<string>(type: "TEXT", nullable: false),
                BookId = table.Column<int>(type: "INTEGER", nullable: false),
                Url = table.Column<string>(type: "TEXT", nullable: false),
                Notes = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookExternalLinks", x => x.ExternalLinkId);
                table.ForeignKey(
                    name: "FK_BookExternalLinks_Books_BookId",
                    column: x => x.BookId,
                    principalTable: "Books",
                    principalColumn: "BookId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Genres",
            columns: table => new
            {
                BookGenreId = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Genre = table.Column<string>(type: "TEXT", nullable: false),
                BookId = table.Column<int>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Genres", x => x.BookGenreId);
                table.ForeignKey(
                    name: "FK_Genres_Books_BookId",
                    column: x => x.BookId,
                    principalTable: "Books",
                    principalColumn: "BookId");
            });

        migrationBuilder.CreateTable(
            name: "UsersBooks",
            columns: table => new
            {
                UserId = table.Column<int>(type: "INTEGER", nullable: false),
                BookId = table.Column<int>(type: "INTEGER", nullable: false),
                ShelveId = table.Column<int>(type: "INTEGER", nullable: false),
                ReadingStatus = table.Column<string>(type: "TEXT", nullable: false),
                ReadingTimestamp = table.Column<DateTime>(type: "TEXT", nullable: true),
                Favorite = table.Column<bool>(type: "INTEGER", nullable: false),
                ShelveId1 = table.Column<int>(type: "INTEGER", nullable: false),
                ShelveUserId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UsersBooks", x => new { x.UserId, x.BookId });
                table.ForeignKey(
                    name: "FK_UsersBooks_Books_BookId",
                    column: x => x.BookId,
                    principalTable: "Books",
                    principalColumn: "BookId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_UsersBooks_Shelves_ShelveId1_ShelveUserId",
                    columns: x => new { x.ShelveId1, x.ShelveUserId },
                    principalTable: "Shelves",
                    principalColumns: new[] { "ShelveId", "UserId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_UsersBooks_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AuthorBook_BooksBookId",
            table: "AuthorBook",
            column: "BooksBookId");

        migrationBuilder.CreateIndex(
            name: "IX_BookBookTag_TagsTagId",
            table: "BookBookTag",
            column: "TagsTagId");

        migrationBuilder.CreateIndex(
            name: "IX_BookExternalLinks_BookId",
            table: "BookExternalLinks",
            column: "BookId");

        migrationBuilder.CreateIndex(
            name: "IX_Books_CollectionId",
            table: "Books",
            column: "CollectionId");

        migrationBuilder.CreateIndex(
            name: "IX_Books_Isbn",
            table: "Books",
            column: "Isbn",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Books_PublisherId",
            table: "Books",
            column: "PublisherId");

        migrationBuilder.CreateIndex(
            name: "IX_Genres_BookId",
            table: "Genres",
            column: "BookId");

        migrationBuilder.CreateIndex(
            name: "IX_OpenIddictApplications_ClientId",
            table: "OpenIddictApplications",
            column: "ClientId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OpenIddictAuthorizations_ApplicationId_Status_Subject_Type",
            table: "OpenIddictAuthorizations",
            columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

        migrationBuilder.CreateIndex(
            name: "IX_OpenIddictScopes_Name",
            table: "OpenIddictScopes",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OpenIddictTokens_ApplicationId_Status_Subject_Type",
            table: "OpenIddictTokens",
            columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

        migrationBuilder.CreateIndex(
            name: "IX_OpenIddictTokens_AuthorizationId",
            table: "OpenIddictTokens",
            column: "AuthorizationId");

        migrationBuilder.CreateIndex(
            name: "IX_OpenIddictTokens_ReferenceId",
            table: "OpenIddictTokens",
            column: "ReferenceId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Shelves_UserId",
            table: "Shelves",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_Tags_Tag",
            table: "Tags",
            column: "Tag");

        migrationBuilder.CreateIndex(
            name: "IX_Tags_TagSearch",
            table: "Tags",
            column: "TagSearch");

        migrationBuilder.CreateIndex(
            name: "IX_UsersBooks_BookId",
            table: "UsersBooks",
            column: "BookId");

        migrationBuilder.CreateIndex(
            name: "IX_UsersBooks_ShelveId1_ShelveUserId",
            table: "UsersBooks",
            columns: new[] { "ShelveId1", "ShelveUserId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AuthorBook");

        migrationBuilder.DropTable(
            name: "BookBookTag");

        migrationBuilder.DropTable(
            name: "BookCover");

        migrationBuilder.DropTable(
            name: "BookExternalLinks");

        migrationBuilder.DropTable(
            name: "Genres");

        migrationBuilder.DropTable(
            name: "OpenIddictScopes");

        migrationBuilder.DropTable(
            name: "OpenIddictTokens");

        migrationBuilder.DropTable(
            name: "UsersBooks");

        migrationBuilder.DropTable(
            name: "Authors");

        migrationBuilder.DropTable(
            name: "Tags");

        migrationBuilder.DropTable(
            name: "OpenIddictAuthorizations");

        migrationBuilder.DropTable(
            name: "Books");

        migrationBuilder.DropTable(
            name: "Shelves");

        migrationBuilder.DropTable(
            name: "OpenIddictApplications");

        migrationBuilder.DropTable(
            name: "Collections");

        migrationBuilder.DropTable(
            name: "Publishers");

        migrationBuilder.DropTable(
            name: "Users");
    }
}
