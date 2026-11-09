using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkTime.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectIdToWorkEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "WorkEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkEvents_ProjectId",
                table: "WorkEvents",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkEvents_Projects_ProjectId",
                table: "WorkEvents",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkEvents_Projects_ProjectId",
                table: "WorkEvents");

            migrationBuilder.DropIndex(
                name: "IX_WorkEvents_ProjectId",
                table: "WorkEvents");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "WorkEvents");
        }
    }
}
