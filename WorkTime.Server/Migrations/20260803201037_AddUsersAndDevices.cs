using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkTime.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false),
                    Email = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false),
                    Platform = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);

                    table.ForeignKey(
                        name: "FK_Devices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO "Users" ("Id", "Name", "Email", "CreatedAt")
                VALUES
                (
                    '11111111-1111-1111-1111-111111111111',
                    'Test User',
                    'test@example.com',
                    NOW()
                ),
                (
                    '3fa85f64-5717-4562-b3fc-2c963f66afa6',
                    'Swagger Test User',
                    'swagger@example.com',
                    NOW()
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO "Devices" ("Id", "UserId", "Name", "Platform", "CreatedAt")
                VALUES
                (
                    '22222222-2222-2222-2222-222222222222',
                    '11111111-1111-1111-1111-111111111111',
                    'PC-01',
                    'Windows',
                    NOW()
                ),
                (
                    '33333333-3333-3333-3333-333333333333',
                    '3fa85f64-5717-4562-b3fc-2c963f66afa6',
                    'Swagger-Test-Device',
                    'Unknown',
                    NOW()
                );
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "WorkEvents"
                ALTER COLUMN "DeviceId" TYPE uuid
                USING CASE
                    WHEN "DeviceId" = 'PC-01'
                        THEN '22222222-2222-2222-2222-222222222222'::uuid

                    WHEN "DeviceId" = 'string'
                        THEN '33333333-3333-3333-3333-333333333333'::uuid

                    ELSE "DeviceId"::uuid
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_WorkEvents_DeviceId_Timestamp",
                table: "WorkEvents",
                columns: new[] { "DeviceId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkEvents_UserId_Timestamp",
                table: "WorkEvents",
                columns: new[] { "UserId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_UserId",
                table: "Devices",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkEvents_Devices_DeviceId",
                table: "WorkEvents",
                column: "DeviceId",
                principalTable: "Devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkEvents_Users_UserId",
                table: "WorkEvents",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkEvents_Devices_DeviceId",
                table: "WorkEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkEvents_Users_UserId",
                table: "WorkEvents");

            migrationBuilder.DropIndex(
                name: "IX_WorkEvents_DeviceId_Timestamp",
                table: "WorkEvents");

            migrationBuilder.DropIndex(
                name: "IX_WorkEvents_UserId_Timestamp",
                table: "WorkEvents");

            migrationBuilder.DropIndex(
                name: "IX_Devices_UserId",
                table: "Devices");

            migrationBuilder.Sql("""
        ALTER TABLE "WorkEvents"
        ALTER COLUMN "DeviceId" TYPE text
        USING CASE
            WHEN "DeviceId" = '22222222-2222-2222-2222-222222222222'::uuid
                THEN 'PC-01'
            ELSE "DeviceId"::text
        END;
        """);

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
