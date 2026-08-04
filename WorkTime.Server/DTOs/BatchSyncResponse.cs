using System.Collections.Generic;

namespace WorkTime.Server.DTOs;

public class BatchSyncResponse
{
    public IEnumerable<SyncResult> Results { get; set; } = new List<SyncResult>();

    public IEnumerable<WorkEventResponse> Timeline { get; set; } = new List<WorkEventResponse>();

    public long? LastServerOrder { get; set; }
}
