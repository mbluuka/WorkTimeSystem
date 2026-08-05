using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkTime.Server.Migrations
{
    /// <inheritdoc />
    public partial class ImproveSyncQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SyncQueue_ProcessedAt_NextRetry_Attempts",
                table: "SyncQueue",
                columns: new[] { "ProcessedAt", "NextRetry", "Attempts" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SyncQueue_ProcessedAt_NextRetry_Attempts",
                table: "SyncQueue");
        }
    }
}
