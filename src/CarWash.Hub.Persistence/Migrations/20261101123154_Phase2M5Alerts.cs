using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarWash.Hub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2M5Alerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "no_activity_after_hours",
                table: "machines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "quiet_hours_end",
                table: "gas_stations",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "quiet_hours_start",
                table: "gas_stations",
                type: "time",
                nullable: true);

            // Existing stations get the default quiet hours a new station starts with
            // (GasStation.DefaultQuietHoursStart/End). There is no column default: a null pair means
            // "open 24 hours" and must stay settable.
            migrationBuilder.Sql("UPDATE gas_stations SET quiet_hours_start = '23:00', quiet_hours_end = '07:00';");

            migrationBuilder.CreateTable(
                name: "alerts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    machine_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    service_code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    type = table.Column<byte>(type: "tinyint", nullable: false),
                    severity = table.Column<byte>(type: "tinyint", nullable: false),
                    notify = table.Column<bool>(type: "bit", nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    resolution = table.Column<byte>(type: "tinyint", nullable: true),
                    baseline_refilled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    remaining = table.Column<int>(type: "int", nullable: true),
                    capacity = table.Column<int>(type: "int", nullable: true),
                    forecast_empty_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    acknowledged_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerts", x => x.id);
                    table.ForeignKey(
                        name: "fk_alerts_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_machines_no_activity_after_hours",
                table: "machines",
                sql: "no_activity_after_hours BETWEEN 1 AND 168");

            migrationBuilder.AddCheckConstraint(
                name: "ck_gas_stations_quiet_hours",
                table: "gas_stations",
                sql: "(quiet_hours_start IS NULL AND quiet_hours_end IS NULL) OR (quiet_hours_start IS NOT NULL AND quiet_hours_end IS NOT NULL AND quiet_hours_start <> quiet_hours_end)");

            migrationBuilder.CreateIndex(
                name: "ix_alerts_machine_id_id",
                table: "alerts",
                columns: new[] { "machine_id", "id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_alerts_open",
                table: "alerts",
                columns: new[] { "machine_id", "service_code", "type" },
                unique: true,
                filter: "[resolved_at] IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_alerts_resolved_at_id",
                table: "alerts",
                columns: new[] { "resolved_at", "id" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_machines_no_activity_after_hours",
                table: "machines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_gas_stations_quiet_hours",
                table: "gas_stations");

            migrationBuilder.DropColumn(
                name: "no_activity_after_hours",
                table: "machines");

            migrationBuilder.DropColumn(
                name: "quiet_hours_end",
                table: "gas_stations");

            migrationBuilder.DropColumn(
                name: "quiet_hours_start",
                table: "gas_stations");
        }
    }
}
