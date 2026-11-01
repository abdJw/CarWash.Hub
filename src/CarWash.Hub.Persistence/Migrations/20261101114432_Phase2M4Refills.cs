using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarWash.Hub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2M4Refills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "machine_service_states",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    machine_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    service_code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    current_refill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    refilled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    capacity = table.Column<int>(type: "int", nullable: true),
                    used_since_refill = table.Column<int>(type: "int", nullable: false),
                    level = table.Column<byte>(type: "tinyint", nullable: false),
                    forecast_empty_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    average_daily_uses = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    last_usage_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_service_states", x => x.id);
                    table.ForeignKey(
                        name: "fk_machine_service_states_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_machine_service_states_service_definitions_service_code",
                        column: x => x.service_code,
                        principalTable: "service_definitions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refills",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    machine_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    refilled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    void_reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("pk_refills", x => x.id);
                    table.ForeignKey(
                        name: "fk_refills_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "state_rebuild_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    machine_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reason = table.Column<byte>(type: "tinyint", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_state_rebuild_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refill_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    refill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    service_code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    capacity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refill_items", x => x.id);
                    table.CheckConstraint("ck_refill_items_capacity", "capacity BETWEEN 1 AND 100000");
                    table.ForeignKey(
                        name: "fk_refill_items_refills_refill_id",
                        column: x => x.refill_id,
                        principalTable: "refills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_refill_items_service_definitions_service_code",
                        column: x => x.service_code,
                        principalTable: "service_definitions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_machine_service_states_level",
                table: "machine_service_states",
                column: "level");

            migrationBuilder.CreateIndex(
                name: "ix_machine_service_states_machine_id_service_code",
                table: "machine_service_states",
                columns: new[] { "machine_id", "service_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_machine_service_states_service_code",
                table: "machine_service_states",
                column: "service_code");

            migrationBuilder.CreateIndex(
                name: "ix_refill_items_refill_id_service_code",
                table: "refill_items",
                columns: new[] { "refill_id", "service_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refill_items_service_code",
                table: "refill_items",
                column: "service_code");

            migrationBuilder.CreateIndex(
                name: "ix_refills_machine_id_refilled_at",
                table: "refills",
                columns: new[] { "machine_id", "refilled_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "machine_service_states");

            migrationBuilder.DropTable(
                name: "refill_items");

            migrationBuilder.DropTable(
                name: "state_rebuild_requests");

            migrationBuilder.DropTable(
                name: "refills");
        }
    }
}
