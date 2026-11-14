using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Remotely.Server.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Add_AuditLogsAndMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceMetricSamples",
                columns: table => new
                {
                    ID = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceID = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<string>(type: "TEXT", nullable: false),
                    CpuUtilization = table.Column<double>(type: "REAL", nullable: false),
                    UsedMemoryPercent = table.Column<double>(type: "REAL", nullable: false),
                    UsedStoragePercent = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceMetricSamples", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "RemoteControlSessionLogs",
                columns: table => new
                {
                    ID = table.Column<string>(type: "TEXT", nullable: false),
                    StartedOn = table.Column<string>(type: "TEXT", nullable: false),
                    EndedOn = table.Column<string>(type: "TEXT", nullable: true),
                    DeviceID = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", nullable: false),
                    OrganizationID = table.Column<string>(type: "TEXT", nullable: false),
                    ViewerUserName = table.Column<string>(type: "TEXT", nullable: true),
                    RequesterDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    ViewerIPAddress = table.Column<string>(type: "TEXT", nullable: true),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    ViewOnly = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteControlSessionLogs", x => x.ID);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceMetricSamples");

            migrationBuilder.DropTable(
                name: "RemoteControlSessionLogs");
        }
    }
}
