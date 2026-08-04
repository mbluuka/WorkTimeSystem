using Microsoft.EntityFrameworkCore;
using Npgsql;
using WorkTime.Server.Data;
using WorkTime.Server.Models;

namespace WorkTime.Server.Services;

public class BulkUpsertService : IBulkUpsertService
{
    private readonly WorkTimeDbContext _dbContext;

    public BulkUpsertService(WorkTimeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task BulkUpsertAsync(IEnumerable<WorkEvent> events, CancellationToken cancellationToken = default)
    {
        var list = events.ToList();
        if (!list.Any()) return;

        var provider = _dbContext.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Npgsql"))
        {
            // Use temporary table + COPY for performance
            var conn = _dbContext.Database.GetDbConnection();
            await using (conn)
            {
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync(cancellationToken);

                // Create temp table
                await using (var createCmd = conn.CreateCommand())
                {
                    createCmd.CommandText = "CREATE TEMP TABLE tmp_workevents (\"Id\" uuid PRIMARY KEY, \"UserId\" uuid, \"DeviceId\" uuid, \"Type\" int, \"Timestamp\" timestamp, \"CreatedAt\" timestamp, \"SyncedAt\" timestamp) ON COMMIT DROP;";
                    await createCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // COPY into temp table using NpgsqlBinaryImporter
                var npgConnection = (NpgsqlConnection)conn;
                await using (var writer = npgConnection.BeginBinaryImport("COPY tmp_workevents (\"Id\", \"UserId\", \"DeviceId\", \"Type\", \"Timestamp\", \"CreatedAt\", \"SyncedAt\") FROM STDIN (FORMAT BINARY)"))
                {
                    foreach (var ev in list)
                    {
                        writer.StartRow();
                        writer.Write(ev.Id, NpgsqlTypes.NpgsqlDbType.Uuid);
                        writer.Write(ev.UserId, NpgsqlTypes.NpgsqlDbType.Uuid);
                        writer.Write(ev.DeviceId, NpgsqlTypes.NpgsqlDbType.Uuid);
                        writer.Write((int)ev.Type, NpgsqlTypes.NpgsqlDbType.Integer);
                        writer.Write(ev.Timestamp, NpgsqlTypes.NpgsqlDbType.Timestamp);
                        writer.Write(ev.CreatedAt, NpgsqlTypes.NpgsqlDbType.Timestamp);
                        if (ev.SyncedAt.HasValue)
                            writer.Write(ev.SyncedAt.Value, NpgsqlTypes.NpgsqlDbType.Timestamp);
                        else
                            writer.Write(DBNull.Value, NpgsqlTypes.NpgsqlDbType.Timestamp);
                    }

                    await writer.CompleteAsync(cancellationToken);
                }

                // Upsert from temp to main table
                var upsertSql = "INSERT INTO \"WorkEvents\" (\"Id\", \"UserId\", \"DeviceId\", \"Type\", \"Timestamp\", \"CreatedAt\", \"SyncedAt\") " +
                                "SELECT \"Id\", \"UserId\", \"DeviceId\", \"Type\", \"Timestamp\", \"CreatedAt\", \"SyncedAt\" FROM tmp_workevents " +
                                "ON CONFLICT (\"Id\") DO UPDATE SET " +
                                "\"UserId\" = EXCLUDED.\"UserId\", " +
                                "\"DeviceId\" = EXCLUDED.\"DeviceId\", " +
                                "\"Type\" = EXCLUDED.\"Type\", " +
                                "\"Timestamp\" = EXCLUDED.\"Timestamp\", " +
                                "\"CreatedAt\" = EXCLUDED.\"CreatedAt\", " +
                                "\"SyncedAt\" = EXCLUDED.\"SyncedAt\";";

                await using (var upsertCmd = conn.CreateCommand())
                {
                    upsertCmd.CommandText = upsertSql;
                    await upsertCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }
        else
        {
            // Fallback for non-Postgres providers: simple AddRange
            _dbContext.WorkEvents.AddRange(list);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
