using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMC.WM.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_Code",
                table: "WorkTasks");

            migrationBuilder.AddColumn<string>(
                name: "OriginDevice",
                table: "WorkTasks",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SyncDirty",
                table: "WorkTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SyncId",
                table: "WorkTasks",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "SyncStamp",
                table: "WorkTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "SyncDirty",
                table: "TaskHistories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SyncId",
                table: "TaskHistories",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "SyncStamp",
                table: "TaskHistories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "SyncDirty",
                table: "Categories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SyncId",
                table: "Categories",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "SyncStamp",
                table: "Categories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "SyncSourceStates",
                columns: table => new
                {
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastFile = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", nullable: true),
                    LastImportUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncSourceStates", x => x.DeviceId);
                });

            migrationBuilder.CreateTable(
                name: "SyncTombstones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SyncId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SyncStamp = table.Column<long>(type: "INTEGER", nullable: false),
                    SyncDirty = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncTombstones", x => x.Id);
                });

            // Gán định danh đồng bộ cho dữ liệu đã có; đánh dấu cần gửi đi ở lần đồng bộ đầu tiên.
            // Danh mục mặc định được đổi sang định danh cố định trong DbInitializer.
            foreach (var table in new[] { "WorkTasks", "TaskHistories", "Categories" })
                migrationBuilder.Sql($"UPDATE \"{table}\" SET \"SyncId\" = lower(hex(randomblob(16))), \"SyncStamp\" = 1, \"SyncDirty\" = 1 WHERE \"SyncId\" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_Code",
                table: "WorkTasks",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_SyncDirty",
                table: "WorkTasks",
                column: "SyncDirty");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_SyncId",
                table: "WorkTasks",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskHistories_SyncDirty",
                table: "TaskHistories",
                column: "SyncDirty");

            migrationBuilder.CreateIndex(
                name: "IX_TaskHistories_SyncId",
                table: "TaskHistories",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_SyncId",
                table: "Categories",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncTombstones_Kind_SyncId",
                table: "SyncTombstones",
                columns: new[] { "Kind", "SyncId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncSourceStates");

            migrationBuilder.DropTable(
                name: "SyncTombstones");

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_Code",
                table: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_SyncDirty",
                table: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_SyncId",
                table: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_TaskHistories_SyncDirty",
                table: "TaskHistories");

            migrationBuilder.DropIndex(
                name: "IX_TaskHistories_SyncId",
                table: "TaskHistories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_SyncId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "OriginDevice",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "SyncDirty",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "SyncStamp",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "SyncDirty",
                table: "TaskHistories");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "TaskHistories");

            migrationBuilder.DropColumn(
                name: "SyncStamp",
                table: "TaskHistories");

            migrationBuilder.DropColumn(
                name: "SyncDirty",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "SyncStamp",
                table: "Categories");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_Code",
                table: "WorkTasks",
                column: "Code",
                unique: true);
        }
    }
}
