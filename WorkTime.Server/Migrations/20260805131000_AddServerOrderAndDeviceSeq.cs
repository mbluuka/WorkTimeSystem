using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkTime.Server.Migrations
{
    public partial class AddServerOrderAndDeviceSeq : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // create sequence for server order
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS workevents_serverorder_seq START 1;");

            migrationBuilder.AddColumn<long>(
                name: "DeviceSeq",
                table: "WorkEvents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "ServerReceivedAt",
                table: "WorkEvents",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ServerOrder",
                table: "WorkEvents",
                type: "bigint",
                nullable: true,
                defaultValueSql: "nextval('workevents_serverorder_seq')");

            migrationBuilder.CreateIndex(
                name: "IX_WorkEvents_ServerOrder",
                table: "WorkEvents",
                column: "ServerOrder");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_WorkEvents_ServerOrder", table: "WorkEvents");
            migrationBuilder.DropColumn(name: "ServerOrder", table: "WorkEvents");
            migrationBuilder.DropColumn(name: "ServerReceivedAt", table: "WorkEvents");
            migrationBuilder.DropColumn(name: "DeviceSeq", table: "WorkEvents");
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS workevents_serverorder_seq;");
        }
    }
}
