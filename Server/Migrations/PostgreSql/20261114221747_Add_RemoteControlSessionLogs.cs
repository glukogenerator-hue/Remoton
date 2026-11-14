using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Remotely.Server.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class Add_RemoteControlSessionLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RemoteControlSessionLogs",
                columns: table => new
                {
                    ID = table.Column<string>(type: "text", nullable: false),
                    StartedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeviceID = table.Column<string>(type: "text", nullable: false),
                    DeviceName = table.Column<string>(type: "text", nullable: false),
                    OrganizationID = table.Column<string>(type: "text", nullable: false),
                    ViewerUserName = table.Column<string>(type: "text", nullable: true),
                    RequesterDisplayName = table.Column<string>(type: "text", nullable: true),
                    ViewerIPAddress = table.Column<string>(type: "text", nullable: true),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    ViewOnly = table.Column<bool>(type: "boolean", nullable: false)
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
                name: "RemoteControlSessionLogs");
        }
    }
}
