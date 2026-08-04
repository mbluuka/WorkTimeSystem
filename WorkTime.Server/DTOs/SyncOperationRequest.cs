using System.Text.Json;

namespace WorkTime.Server.DTOs;

public class SyncOperationRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public JsonElement Data { get; set; }
    public long Version { get; set; }
}
