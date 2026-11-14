using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Remotely.Server.Migrations.SqlServer
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
                    ID = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StartedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeviceID = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrganizationID = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ViewerUserName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequesterDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ViewerIPAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Mode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ViewOnly = table.Column<bool>(type: "bit", nullable: false)
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
