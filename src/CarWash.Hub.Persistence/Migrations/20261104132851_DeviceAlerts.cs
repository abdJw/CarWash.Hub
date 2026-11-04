using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarWash.Hub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_sale_at",
                table: "alerts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "water_unavailable_since",
                table: "alerts",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_sale_at",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "water_unavailable_since",
                table: "alerts");
        }
    }
}
