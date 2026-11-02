using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarWash.Hub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2FinancePrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "gas_station_id",
                table: "service_usages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "service_usage_daily",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    gas_station_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    machine_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    service_code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    usage_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_usage_daily", x => x.id)
                        .Annotation("SqlServer:Clustered", false);
                    table.CheckConstraint("ck_service_usage_daily_usage_count", "usage_count >= 0");
                    table.ForeignKey(
                        name: "fk_service_usage_daily_gas_stations_gas_station_id",
                        column: x => x.gas_station_id,
                        principalTable: "gas_stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_usage_daily_machines_machine_id",
                        column: x => x.machine_id,
                        principalTable: "machines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_usage_daily_service_definitions_service_code",
                        column: x => x.service_code,
                        principalTable: "service_definitions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "station_service_prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    gas_station_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    service_code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("pk_station_service_prices", x => x.id);
                    table.CheckConstraint("ck_station_service_prices_unit_price", "unit_price BETWEEN 0 AND 999.999");
                    table.ForeignKey(
                        name: "fk_station_service_prices_gas_stations_gas_station_id",
                        column: x => x.gas_station_id,
                        principalTable: "gas_stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_station_service_prices_service_definitions_service_code",
                        column: x => x.service_code,
                        principalTable: "service_definitions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_usages_gas_station_id",
                table: "service_usages",
                column: "gas_station_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_usage_daily_business_date_station_machine_service",
                table: "service_usage_daily",
                columns: new[] { "business_date", "gas_station_id", "machine_id", "service_code" },
                unique: true)
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "ix_service_usage_daily_gas_station_id",
                table: "service_usage_daily",
                column: "gas_station_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_usage_daily_machine_id",
                table: "service_usage_daily",
                column: "machine_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_usage_daily_service_code",
                table: "service_usage_daily",
                column: "service_code");

            migrationBuilder.CreateIndex(
                name: "ix_station_service_prices_service_code",
                table: "station_service_prices",
                column: "service_code");

            migrationBuilder.CreateIndex(
                name: "ix_station_service_prices_station_service_effective_from",
                table: "station_service_prices",
                columns: new[] { "gas_station_id", "service_code", "effective_from" },
                unique: true,
                filter: "is_deleted = 0");

            migrationBuilder.AddForeignKey(
                name: "fk_service_usages_gas_stations_gas_station_id",
                table: "service_usages",
                column: "gas_station_id",
                principalTable: "gas_stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Earlier uses belong to the station each machine stands at now: the best record there is.
            migrationBuilder.Sql("""
                UPDATE u SET gas_station_id = m.gas_station_id
                FROM service_usages u
                JOIN machines m ON m.id = u.machine_id
                WHERE m.gas_station_id IS NOT NULL;
                """);

            // The daily rollup of every use so far. 'Jordan Standard Time' is Asia/Amman, the agreed
            // business time zone (Dashboard:TimeZone); the worker keeps the rollup from here on.
            migrationBuilder.Sql("""
                INSERT INTO service_usage_daily (business_date, gas_station_id, machine_id, service_code, usage_count)
                SELECT CAST(occurred_at AT TIME ZONE 'Jordan Standard Time' AS date), gas_station_id, machine_id, service_code, COUNT(*)
                FROM service_usages
                GROUP BY CAST(occurred_at AT TIME ZONE 'Jordan Standard Time' AS date), gas_station_id, machine_id, service_code;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_service_usages_gas_stations_gas_station_id",
                table: "service_usages");

            migrationBuilder.DropTable(
                name: "service_usage_daily");

            migrationBuilder.DropTable(
                name: "station_service_prices");

            migrationBuilder.DropIndex(
                name: "ix_service_usages_gas_station_id",
                table: "service_usages");

            migrationBuilder.DropColumn(
                name: "gas_station_id",
                table: "service_usages");
        }
    }
}
