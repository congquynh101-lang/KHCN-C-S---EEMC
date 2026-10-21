using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMC.WM.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsAdmin",
                table: "Users",
                newName: "Role");

            // IsAdmin 1 → Quản trị (3); tài khoản khác trước đây sửa được mọi thứ → Biên tập (2)
            migrationBuilder.Sql("UPDATE Users SET Role = CASE WHEN Role = 1 THEN 3 ELSE 2 END;");

            migrationBuilder.AddColumn<bool>(
                name: "IsDisabled",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDisabled",
                table: "Users");

            migrationBuilder.Sql("UPDATE Users SET Role = CASE WHEN Role = 3 THEN 1 ELSE 0 END;");

            migrationBuilder.RenameColumn(
                name: "Role",
                table: "Users",
                newName: "IsAdmin");
        }
    }
}
