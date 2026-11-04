using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarWash.Hub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StationServiceDurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "station_service_durations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    gas_station_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    service_code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    duration1_seconds = table.Column<int>(type: "int", nullable: false),
                    duration2_seconds = table.Column<int>(type: "int", nullable: true),
                    duration3_seconds = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("pk_station_service_durations", x => x.id);
                    table.CheckConstraint("ck_station_service_durations_duration1", "duration1_seconds BETWEEN 1 AND 5999");
                    table.CheckConstraint("ck_station_service_durations_duration2", "duration2_seconds IS NULL OR duration2_seconds BETWEEN 1 AND 5999");
                    table.CheckConstraint("ck_station_service_durations_duration3", "duration3_seconds IS NULL OR duration3_seconds BETWEEN 1 AND 5999");
                    table.CheckConstraint("ck_station_service_durations_positions", "(duration2_seconds IS NULL AND duration3_seconds IS NULL) OR (duration2_seconds IS NOT NULL AND duration3_seconds IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_station_service_durations_gas_stations_gas_station_id",
                        column: x => x.gas_station_id,
                        principalTable: "gas_stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_station_service_durations_service_definitions_service_code",
                        column: x => x.service_code,
                        principalTable: "service_definitions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_station_service_durations_service_code",
                table: "station_service_durations",
                column: "service_code");

            migrationBuilder.CreateIndex(
                name: "ix_station_service_durations_station_service",
                table: "station_service_durations",
                columns: new[] { "gas_station_id", "service_code" },
                unique: true,
                filter: "is_deleted = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "station_service_durations");
        }
    }
}
