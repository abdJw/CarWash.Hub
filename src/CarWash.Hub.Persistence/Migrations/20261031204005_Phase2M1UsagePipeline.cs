using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarWash.Hub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2M1UsagePipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "processed_at",
                table: "machine_messages",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "processing_error",
                table: "machine_messages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "processing_status",
                table: "machine_messages",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateTable(
                name: "machine_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_message_id = table.Column<long>(type: "bigint", nullable: false),
                    last_usage_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_service_code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    total_uses = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_states", x => x.id);
                    table.ForeignKey(
                        name: "fk_machine_states_machines_id",
                        column: x => x.id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    icon = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    color = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                    tracks_refill = table.Column<bool>(type: "bit", nullable: false),
                    default_capacity = table.Column<int>(type: "int", nullable: true),
                    low_threshold_percent = table.Column<int>(type: "int", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    is_configured = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("pk_service_definitions", x => x.id);
                    table.UniqueConstraint("ak_service_definitions_code", x => x.code);
                    table.CheckConstraint("ck_service_definitions_default_capacity", "default_capacity IS NULL OR default_capacity > 0");
                    table.CheckConstraint("ck_service_definitions_low_threshold_percent", "low_threshold_percent BETWEEN 1 AND 99");
                });

            migrationBuilder.CreateTable(
                name: "service_usage_hourly",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    machine_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hour_start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    service_code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    usage_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_usage_hourly", x => x.id)
                        .Annotation("SqlServer:Clustered", false);
                    table.CheckConstraint("ck_service_usage_hourly_usage_count", "usage_count >= 0");
                    table.ForeignKey(
                        name: "fk_service_usage_hourly_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_usage_hourly_service_definitions_service_code",
                        column: x => x.service_code,
                        principalTable: "service_definitions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_usages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    machine_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    service_code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    occurred_at_source = table.Column<byte>(type: "tinyint", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    machine_message_id = table.Column<long>(type: "bigint", nullable: false),
                    item_index = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_usages", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_usages_machine_messages_machine_message_id",
                        column: x => x.machine_message_id,
                        principalTable: "machine_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_usages_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_usages_service_definitions_service_code",
                        column: x => x.service_code,
                        principalTable: "service_definitions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_machine_messages_failed",
                table: "machine_messages",
                column: "id",
                filter: "[processing_status] = 3");

            migrationBuilder.CreateIndex(
                name: "ix_machine_messages_pending",
                table: "machine_messages",
                column: "id",
                filter: "[processing_status] = 0");

            migrationBuilder.CreateIndex(
                name: "ix_service_usage_hourly_hour_start",
                table: "service_usage_hourly",
                column: "hour_start")
                .Annotation("SqlServer:Include", new[] { "machine_id", "service_code", "usage_count" });

            migrationBuilder.CreateIndex(
                name: "ix_service_usage_hourly_machine_id_hour_start_service_code",
                table: "service_usage_hourly",
                columns: new[] { "machine_id", "hour_start", "service_code" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "ix_service_usage_hourly_service_code",
                table: "service_usage_hourly",
                column: "service_code");

            migrationBuilder.CreateIndex(
                name: "ix_service_usages_machine_id_service_code_occurred_at",
                table: "service_usages",
                columns: new[] { "machine_id", "service_code", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_service_usages_machine_message_id_item_index",
                table: "service_usages",
                columns: new[] { "machine_message_id", "item_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_usages_service_code",
                table: "service_usages",
                column: "service_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "machine_states");

            migrationBuilder.DropTable(
                name: "service_usage_hourly");

            migrationBuilder.DropTable(
                name: "service_usages");

            migrationBuilder.DropTable(
                name: "service_definitions");

            migrationBuilder.DropIndex(
                name: "ix_machine_messages_failed",
                table: "machine_messages");

            migrationBuilder.DropIndex(
                name: "ix_machine_messages_pending",
                table: "machine_messages");

            migrationBuilder.DropColumn(
                name: "processed_at",
                table: "machine_messages");

            migrationBuilder.DropColumn(
                name: "processing_error",
                table: "machine_messages");

            migrationBuilder.DropColumn(
                name: "processing_status",
                table: "machine_messages");
        }
    }
}
