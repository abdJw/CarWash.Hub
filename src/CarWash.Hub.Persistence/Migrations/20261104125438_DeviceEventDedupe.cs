using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarWash.Hub.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceEventDedupe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "device_event_id",
                table: "machine_messages",
                type: "varchar(128)",
                unicode: false,
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_machine_messages_device_event_id",
                table: "machine_messages",
                columns: new[] { "machine_id", "device_event_id" },
                unique: true,
                filter: "[device_event_id] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_machine_messages_device_event_id",
                table: "machine_messages");

            migrationBuilder.DropColumn(
                name: "device_event_id",
                table: "machine_messages");
        }
    }
}
