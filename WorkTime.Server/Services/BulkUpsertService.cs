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
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(cancellationToken);

            // Temp table lives only for the transaction (ON COMMIT DROP),
            // so CREATE / COPY / upsert must share one explicit transaction.
            await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
            try
            {
                await using (var createCmd = conn.CreateCommand())
                {
                    createCmd.Transaction = transaction;
                    createCmd.CommandText = "CREATE TEMP TABLE tmp_workevents (\"Id\" uuid PRIMARY KEY, \"UserId\" uuid, \"DeviceId\" uuid, \"Type\" int, \"Timestamp\" timestamp, \"CreatedAt\" timestamp, \"SyncedAt\" timestamp, \"DeviceSeq\" bigint, \"ServerReceivedAt\" timestamp, \"ProjectId\" uuid) ON COMMIT DROP;";
                    await createCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // COPY into temp table using NpgsqlBinaryImporter
                var npgConnection = (NpgsqlConnection)conn;
                await using (var writer = await npgConnection.BeginBinaryImportAsync("COPY tmp_workevents (\"Id\", \"UserId\", \"DeviceId\", \"Type\", \"Timestamp\", \"CreatedAt\", \"SyncedAt\", \"DeviceSeq\", \"ServerReceivedAt\", \"ProjectId\") FROM STDIN (FORMAT BINARY)", cancellationToken))
                {
                    foreach (var ev in list)
                    {
                        // PostgreSQL 'timestamp without time zone' не принимает DateTime с Kind=UTC,
                        // поэтому приводим к Unspecified — время уже в UTC.
                        var ts = DateTime.SpecifyKind(ev.Timestamp, DateTimeKind.Unspecified);
                        var ca = DateTime.SpecifyKind(ev.CreatedAt, DateTimeKind.Unspecified);
                        var sa = ev.SyncedAt.HasValue
                            ? DateTime.SpecifyKind(ev.SyncedAt.Value, DateTimeKind.Unspecified)
                            : (DateTime?)null;
                        var sra = ev.ServerReceivedAt.HasValue
                            ? DateTime.SpecifyKind(ev.ServerReceivedAt.Value, DateTimeKind.Unspecified)
                            : (DateTime?)null;

                        writer.StartRow();
                        writer.Write(ev.Id, NpgsqlTypes.NpgsqlDbType.Uuid);
                        writer.Write(ev.UserId, NpgsqlTypes.NpgsqlDbType.Uuid);
                        writer.Write(ev.DeviceId, NpgsqlTypes.NpgsqlDbType.Uuid);
                        writer.Write((int)ev.Type, NpgsqlTypes.NpgsqlDbType.Integer);
                        writer.Write(ts, NpgsqlTypes.NpgsqlDbType.Timestamp);
                        writer.Write(ca, NpgsqlTypes.NpgsqlDbType.Timestamp);
                        writer.Write(sa, NpgsqlTypes.NpgsqlDbType.Timestamp);
                        writer.Write(ev.DeviceSeq, NpgsqlTypes.NpgsqlDbType.Bigint);
                        writer.Write(sra, NpgsqlTypes.NpgsqlDbType.Timestamp);
                        writer.Write(ev.ProjectId, NpgsqlTypes.NpgsqlDbType.Uuid);
                    }

                    await writer.CompleteAsync(cancellationToken);
                }

                // ServerOrder is assigned by the sequence default; already-synced rows keep their order.
                var upsertSql = "INSERT INTO \"WorkEvents\" (\"Id\", \"UserId\", \"DeviceId\", \"Type\", \"Timestamp\", \"CreatedAt\", \"SyncedAt\", \"DeviceSeq\", \"ServerReceivedAt\", \"ProjectId\") " +
                                "SELECT \"Id\", \"UserId\", \"DeviceId\", \"Type\", \"Timestamp\", \"CreatedAt\", \"SyncedAt\", \"DeviceSeq\", \"ServerReceivedAt\", \"ProjectId\" FROM tmp_workevents " +
                                "ON CONFLICT (\"Id\") DO UPDATE SET " +
                                "\"UserId\" = EXCLUDED.\"UserId\", " +
                                "\"DeviceId\" = EXCLUDED.\"DeviceId\", " +
                                "\"Type\" = EXCLUDED.\"Type\", " +
                                "\"Timestamp\" = EXCLUDED.\"Timestamp\", " +
                                "\"CreatedAt\" = EXCLUDED.\"CreatedAt\", " +
                                "\"SyncedAt\" = EXCLUDED.\"SyncedAt\", " +
                                "\"DeviceSeq\" = EXCLUDED.\"DeviceSeq\", " +
                                "\"ServerReceivedAt\" = EXCLUDED.\"ServerReceivedAt\", " +
                                "\"ProjectId\" = EXCLUDED.\"ProjectId\";";

                await using (var upsertCmd = conn.CreateCommand())
                {
                    upsertCmd.Transaction = transaction;
                    upsertCmd.CommandText = upsertSql;
                    await upsertCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
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
