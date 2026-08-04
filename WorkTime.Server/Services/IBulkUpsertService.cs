using WorkTime.Server.Models;

namespace WorkTime.Server.Services;

public interface IBulkUpsertService
{
    Task BulkUpsertAsync(IEnumerable<WorkEvent> events, CancellationToken cancellationToken = default);
}
