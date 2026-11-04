using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarWash.Hub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MachineDeviceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "machine_device_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    desired_revision = table.Column<long>(type: "bigint", nullable: false),
                    services_json = table.Column<string>(type: "varchar(640)", unicode: false, maxLength: 640, nullable: true),
                    desired_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    issue = table.Column<byte>(type: "tinyint", nullable: false),
                    issue_service = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    applied_revision = table.Column<long>(type: "bigint", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_ack_revision = table.Column<long>(type: "bigint", nullable: false),
                    last_ack_status = table.Column<byte>(type: "tinyint", nullable: false),
                    last_ack_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_ack_message_id = table.Column<long>(type: "bigint", nullable: true),
                    max_reported_revision = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_device_settings", x => x.id);
                    table.CheckConstraint("ck_machine_device_settings_applied_revision", "applied_revision >= 0 AND applied_revision <= desired_revision");
                    table.CheckConstraint("ck_machine_device_settings_desired_revision", "desired_revision BETWEEN 0 AND 4294967295");
                    table.CheckConstraint("ck_machine_device_settings_last_ack_revision", "last_ack_revision BETWEEN 0 AND 4294967295");
                    table.CheckConstraint("ck_machine_device_settings_max_reported_revision", "max_reported_revision BETWEEN 0 AND 4294967295");
                    table.ForeignKey(
                        name: "fk_machine_device_settings_machines_id",
                        column: x => x.id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "machine_device_settings");
        }
    }
}
