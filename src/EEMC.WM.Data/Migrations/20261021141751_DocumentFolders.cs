using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMC.WM.Data.Migrations
{
    /// <inheritdoc />
    public partial class DocumentFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cột cũ Attachments.TaskId/ProjectId được GIỮ trong CSDL (không xóa cột trên SQLite cũ của NAS);
            // vị trí tài liệu chuyển sang bảng AttachmentLinks ở cuối migration này.

            migrationBuilder.AddColumn<int>(
                name: "CurrentVersion",
                table: "Attachments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Attachments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedText",
                table: "Attachments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Attachments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "Attachments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                table: "Attachments",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Attachments",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "UploadedBy",
                table: "Attachments",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttachmentVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AttachmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    VersionNo = table.Column<int>(type: "INTEGER", nullable: false),
                    StoredFile = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    UploadedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttachmentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttachmentVersions_Attachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "Attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: true),
                    LegalDocumentId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocFolders_DocFolders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "DocFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttachmentLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AttachmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    FolderId = table.Column<int>(type: "INTEGER", nullable: true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    LegalDocumentId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttachmentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttachmentLinks_Attachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "Attachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttachmentLinks_DocFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "DocFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_IsDeleted",
                table: "Attachments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_Sha256",
                table: "Attachments",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_AttachmentId",
                table: "AttachmentLinks",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_FolderId",
                table: "AttachmentLinks",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_LegalDocumentId",
                table: "AttachmentLinks",
                column: "LegalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_ProjectId",
                table: "AttachmentLinks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentLinks_TaskId",
                table: "AttachmentLinks",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentVersions_AttachmentId_VersionNo",
                table: "AttachmentVersions",
                columns: new[] { "AttachmentId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocFolders_LegalDocumentId",
                table: "DocFolders",
                column: "LegalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocFolders_ParentId",
                table: "DocFolders",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocFolders_ProjectId",
                table: "DocFolders",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DocFolders_TaskId",
                table: "DocFolders",
                column: "TaskId");

            // Chuyển dữ liệu cũ: vị trí theo công việc/dự án → AttachmentLinks; tệp hiện có → phiên bản 1.
            // Nội dung để tìm kiếm (ExtractedText = NULL) được đọc dần sau khi khởi động.
            migrationBuilder.Sql("INSERT INTO AttachmentLinks (AttachmentId, TaskId, CreatedAt) SELECT Id, TaskId, CreatedAt FROM Attachments WHERE TaskId IS NOT NULL;");
            migrationBuilder.Sql("INSERT INTO AttachmentLinks (AttachmentId, ProjectId, CreatedAt) SELECT Id, ProjectId, CreatedAt FROM Attachments WHERE ProjectId IS NOT NULL;");
            migrationBuilder.Sql("INSERT INTO AttachmentVersions (AttachmentId, VersionNo, StoredFile, FileName, SizeBytes, UploadedAt) SELECT Id, 1, StoredFile, Name, SizeBytes, CreatedAt FROM Attachments WHERE StoredFile IS NOT NULL;");
            migrationBuilder.Sql("UPDATE Attachments SET UpdatedAt = CreatedAt, SearchText = lower(Name);");
            // Tài liệu không còn thuộc công việc/dự án nào (công việc đã xóa vĩnh viễn trước đây): vào thùng rác, không mất tệp
            migrationBuilder.Sql("UPDATE Attachments SET IsDeleted = 1, DeletedAt = CreatedAt WHERE TaskId IS NULL AND ProjectId IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttachmentLinks");

            migrationBuilder.DropTable(
                name: "AttachmentVersions");

            migrationBuilder.DropTable(
                name: "DocFolders");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_IsDeleted",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_Sha256",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "CurrentVersion",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "ExtractedText",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "Sha256",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "UploadedBy",
                table: "Attachments");
            // Cột TaskId/ProjectId vẫn còn trong CSDL (không bị xóa ở Up)
        }
    }
}
