using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarWash.Hub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2M2Users : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    friendly_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    xml = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    password_hash = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false),
                    role = table.Column<byte>(type: "tinyint", nullable: false),
                    gas_station_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    security_stamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    must_change_password = table.Column<bool>(type: "bit", nullable: false),
                    failed_sign_in_count = table.Column<int>(type: "int", nullable: false),
                    lockout_end_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_sign_in_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modified_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_role", "role IN (1, 2)");
                    table.CheckConstraint("ck_users_station_matches_role", "(role = 2 AND gas_station_id IS NOT NULL) OR (role = 1 AND gas_station_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_users_gas_stations_gas_station_id",
                        column: x => x.gas_station_id,
                        principalTable: "gas_stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_gas_station_id",
                table: "users",
                column: "gas_station_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_user_name",
                table: "users",
                column: "user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
