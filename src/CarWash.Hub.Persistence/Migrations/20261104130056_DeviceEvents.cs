using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarWash.Hub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "duration_seconds",
                table: "service_usages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "paid_amount",
                table: "service_usages",
                type: "decimal(9,3)",
                precision: 9,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "sale_amount",
                table: "service_usages",
                type: "decimal(9,3)",
                precision: 9,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cash_collected_amount",
                table: "service_usage_daily",
                type: "decimal(12,3)",
                precision: 12,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "device_priced_uses",
                table: "service_usage_daily",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "device_sales_amount",
                table: "service_usage_daily",
                type: "decimal(12,3)",
                precision: 12,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_boot_at",
                table: "machine_states",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_boot_id",
                table: "machine_states",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_heartbeat_at",
                table: "machine_states",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "water_available",
                table: "machine_states",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "water_changed_at",
                table: "machine_states",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "water_message_id",
                table: "machine_states",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "water_reported_at",
                table: "machine_states",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_usages_duration_seconds",
                table: "service_usages",
                sql: "duration_seconds IS NULL OR duration_seconds > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_usages_paid_amount",
                table: "service_usages",
                sql: "paid_amount IS NULL OR paid_amount >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_usages_sale_amount",
                table: "service_usages",
                sql: "sale_amount IS NULL OR sale_amount >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_usage_daily_cash_collected_amount",
                table: "service_usage_daily",
                sql: "cash_collected_amount >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_usage_daily_device_priced_uses",
                table: "service_usage_daily",
                sql: "device_priced_uses >= 0 AND device_priced_uses <= usage_count");

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_usage_daily_device_sales_amount",
                table: "service_usage_daily",
                sql: "device_sales_amount >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_service_usages_duration_seconds",
                table: "service_usages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_usages_paid_amount",
                table: "service_usages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_usages_sale_amount",
                table: "service_usages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_usage_daily_cash_collected_amount",
                table: "service_usage_daily");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_usage_daily_device_priced_uses",
                table: "service_usage_daily");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_usage_daily_device_sales_amount",
                table: "service_usage_daily");

            migrationBuilder.DropColumn(
                name: "duration_seconds",
                table: "service_usages");

            migrationBuilder.DropColumn(
                name: "paid_amount",
                table: "service_usages");

            migrationBuilder.DropColumn(
                name: "sale_amount",
                table: "service_usages");

            migrationBuilder.DropColumn(
                name: "cash_collected_amount",
                table: "service_usage_daily");

            migrationBuilder.DropColumn(
                name: "device_priced_uses",
                table: "service_usage_daily");

            migrationBuilder.DropColumn(
                name: "device_sales_amount",
                table: "service_usage_daily");

            migrationBuilder.DropColumn(
                name: "last_boot_at",
                table: "machine_states");

            migrationBuilder.DropColumn(
                name: "last_boot_id",
                table: "machine_states");

            migrationBuilder.DropColumn(
                name: "last_heartbeat_at",
                table: "machine_states");

            migrationBuilder.DropColumn(
                name: "water_available",
                table: "machine_states");

            migrationBuilder.DropColumn(
                name: "water_changed_at",
                table: "machine_states");

            migrationBuilder.DropColumn(
                name: "water_message_id",
                table: "machine_states");

            migrationBuilder.DropColumn(
                name: "water_reported_at",
                table: "machine_states");
        }
    }
}
