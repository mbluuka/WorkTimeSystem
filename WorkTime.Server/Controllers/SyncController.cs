using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkTime.Server.Data;
using WorkTime.Server.DTOs;
using WorkTime.Server.Models;

namespace WorkTime.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Microsoft.AspNetCore.Authorization.Authorize]
public class SyncController : ControllerBase
{
    private readonly WorkTimeDbContext _dbContext;
    private readonly WorkTime.Server.Services.IBulkUpsertService _bulkUpsert;

    public SyncController(WorkTimeDbContext dbContext, WorkTime.Server.Services.IBulkUpsertService bulkUpsert)
    {
        _dbContext = dbContext;
        _bulkUpsert = bulkUpsert;
    }

    [HttpPost("operations")]
    public async Task<ActionResult<IEnumerable<SyncOperationResult>>> PostOperations([FromBody] IEnumerable<SyncOperationRequest> ops)
    {
        var list = ops?.ToList() ?? new List<SyncOperationRequest>();
        if (!list.Any()) return BadRequest(new { message = "No operations provided" });

        var currentUserId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier).Value);
        var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "Employee";

        var results = new List<SyncOperationResult>();
        var toUpsertWorkEvents = new List<WorkEvent>();

        foreach (var op in list)
        {
            if (currentRole != "Admin" && currentRole != "Manager" && op.UserId != currentUserId)
            {
                results.Add(new SyncOperationResult { Id = op.Id, Status = "Failed", Message = "Forbidden" });
                continue;
            }

            var entry = new SyncQueueEntry
            {
                Id = op.Id == Guid.Empty ? Guid.NewGuid() : op.Id,
                UserId = op.UserId,
                EntityType = op.EntityType,
                EntityId = op.EntityId,
                Operation = op.Operation,
                Data = op.Data.GetRawText(),
                Version = op.Version,
                Attempts = 0,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.SyncQueue.Add(entry);
            results.Add(new SyncOperationResult { Id = entry.Id, Status = "Accepted" });

            // For now, process WorkEvent create/update immediately into WorkEvents via bulk upsert
            if (string.Equals(op.EntityType, "WorkEvent", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(op.Operation, "create", StringComparison.OrdinalIgnoreCase) || string.Equals(op.Operation, "update", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var we = JsonSerializer.Deserialize<WorkEvent>(op.Data.GetRawText());
                    if (we != null)
                    {
                        // ensure server fields
                        we.ServerReceivedAt = DateTime.UtcNow;
                        toUpsertWorkEvents.Add(we);
                        entry.ProcessedAt = DateTime.UtcNow;
                        entry.Result = "Applied";
                    }
                }
                catch (Exception ex)
                {
                    entry.Result = "Failed: " + ex.Message;
                }
            }
        }

        await _dbContext.SaveChangesAsync();

        if (toUpsertWorkEvents.Any())
        {
            try
            {
                await _bulkUpsert.BulkUpsertAsync(toUpsertWorkEvents);
            }
            catch (Exception ex)
            {
                // mark failed entries
                foreach (var we in toUpsertWorkEvents)
                {
                    var q = await _dbContext.SyncQueue.FirstOrDefaultAsync(x => x.EntityId == we.Id && x.EntityType == "WorkEvent");
                    if (q != null)
                    {
                        q.Result = "Failed apply: " + ex.Message;
                        q.Attempts += 1;
                        q.NextRetry = DateTime.UtcNow.AddMinutes(5);
                    }
                }

                await _dbContext.SaveChangesAsync();
            }
        }

        return Ok(results);
    }
}
