using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMC.WM.Data.Migrations
{
    /// <inheritdoc />
    public partial class LegalDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegalDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Number = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DocType = table.Column<int>(type: "INTEGER", nullable: false),
                    Issuer = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    IssuedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EffectiveDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Effect = table.Column<int>(type: "INTEGER", nullable: false),
                    EffectCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    StoredFile = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    FullText = table.Column<string>(type: "TEXT", nullable: true),
                    SummaryAbout = table.Column<string>(type: "TEXT", nullable: true),
                    SummaryApplies = table.Column<string>(type: "TEXT", nullable: true),
                    SummaryState = table.Column<int>(type: "INTEGER", nullable: false),
                    SummaryAuthor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SummaryUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SummaryApprovedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SummaryApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SearchText = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LegalPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Citation = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Due = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalPoints_LegalDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LegalRelations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetId = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetText = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalRelations_LegalDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LegalRelations_LegalDocuments_TargetId",
                        column: x => x.TargetId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LegalSections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Heading = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalSections_LegalDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LegalTaskLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalTaskLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalTaskLinks_LegalDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocuments_Effect",
                table: "LegalDocuments",
                column: "Effect");

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocuments_Number",
                table: "LegalDocuments",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_LegalPoints_DocumentId_Kind_SortOrder",
                table: "LegalPoints",
                columns: new[] { "DocumentId", "Kind", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalRelations_DocumentId",
                table: "LegalRelations",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalRelations_TargetId",
                table: "LegalRelations",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalSections_DocumentId_SortOrder",
                table: "LegalSections",
                columns: new[] { "DocumentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalTaskLinks_DocumentId_TaskId",
                table: "LegalTaskLinks",
                columns: new[] { "DocumentId", "TaskId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegalTaskLinks_TaskId",
                table: "LegalTaskLinks",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegalPoints");

            migrationBuilder.DropTable(
                name: "LegalRelations");

            migrationBuilder.DropTable(
                name: "LegalSections");

            migrationBuilder.DropTable(
                name: "LegalTaskLinks");

            migrationBuilder.DropTable(
                name: "LegalDocuments");
        }
    }
}
