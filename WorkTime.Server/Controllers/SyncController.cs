using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkTime.Server.Data;
using WorkTime.Server.DTOs;
using WorkTime.Server.Models;

namespace WorkTime.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SyncController : ControllerBase
{
    private readonly WorkTimeDbContext _dbContext;

    public SyncController(WorkTimeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpPost("operations")]
    public async Task<ActionResult<IEnumerable<SyncOperationResult>>> PostOperations(
        [FromBody] IEnumerable<SyncOperationRequest> ops)
    {
        var list = ops?.ToList() ?? new List<SyncOperationRequest>();

        if (list.Count == 0)
        {
            return BadRequest(new
            {
                message = "No operations provided"
            });
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdClaim, out var currentUserId))
        {
            return Unauthorized(new
            {
                message = "Invalid user identity"
            });
        }

        var currentRole =
            User.FindFirstValue(ClaimTypes.Role)
            ?? "Employee";

        var results = new List<SyncOperationResult>();

        foreach (var op in list)
        {
            /*
             * Если клиент не передал Id операции,
             * сервер генерирует новый.
             */
            if (op.Id == Guid.Empty)
            {
                op.Id = Guid.NewGuid();
            }

            /*
             * Проверяем существование операции.
             *
             * Это делает endpoint идемпотентным:
             * повторная отправка той же операции
             * не создаёт вторую запись.
             */
            var existing = await _dbContext.SyncQueue
                .FirstOrDefaultAsync(x => x.Id == op.Id);

            if (existing != null)
            {
                /*
                 * Если операция принадлежит другому пользователю,
                 * нельзя раскрывать её содержимое.
                 */
                if (existing.UserId != currentUserId &&
                    currentRole != "Admin" &&
                    currentRole != "Manager")
                {
                    results.Add(new SyncOperationResult
                    {
                        Id = op.Id,
                        Status = "Forbidden",
                        Message = "Operation belongs to another user"
                    });

                    continue;
                }

                /*
                 * Проверяем, что повторная операция действительно
                 * соответствует существующей.
                 */
                var sameOperation =
                    existing.UserId == op.UserId &&
                    existing.EntityType.Equals(
                        op.EntityType,
                        StringComparison.OrdinalIgnoreCase) &&
                    existing.EntityId == op.EntityId &&
                    existing.Operation.Equals(
                        op.Operation,
                        StringComparison.OrdinalIgnoreCase) &&
                    existing.Version == op.Version &&
                    existing.Data == op.Data.GetRawText();

                if (!sameOperation)
                {
                    results.Add(new SyncOperationResult
                    {
                        Id = op.Id,
                        Status = "Conflict",
                        Message = "Operation with this id already exists with different data"
                    });

                    continue;
                }

                /*
                 * Операция уже была успешно обработана.
                 */
                if (existing.ProcessedAt.HasValue)
                {
                    results.Add(new SyncOperationResult
                    {
                        Id = op.Id,
                        Status = "AlreadyProcessed",
                        Message = existing.Result ?? "Operation already processed"
                    });

                    continue;
                }

                /*
                 * Операция уже стоит в очереди,
                 * но ещё не обработана.
                 */
                results.Add(new SyncOperationResult
                {
                    Id = op.Id,
                    Status = "AlreadyQueued",
                    Message = existing.Result ?? "Operation already queued"
                });

                continue;
            }

            /*
             * Проверяем права пользователя.
             *
             * Employee может отправлять операции только
             * от своего имени.
             *
             * Manager/Admin могут работать с другими пользователями.
             */
            if (currentRole != "Admin" &&
                currentRole != "Manager" &&
                op.UserId != currentUserId)
            {
                results.Add(new SyncOperationResult
                {
                    Id = op.Id,
                    Status = "Forbidden",
                    Message = "User can synchronize only own data"
                });

                continue;
            }

            /*
             * Проверка типа сущности.
             */
            if (string.IsNullOrWhiteSpace(op.EntityType))
            {
                results.Add(new SyncOperationResult
                {
                    Id = op.Id,
                    Status = "Invalid",
                    Message = "EntityType is required"
                });

                continue;
            }

            /*
             * Пока сервер поддерживает следующие операции.
             */
            var operation = op.Operation?.Trim().ToLowerInvariant();

            if (operation != "create" &&
                operation != "update" &&
                operation != "delete")
            {
                results.Add(new SyncOperationResult
                {
                    Id = op.Id,
                    Status = "Invalid",
                    Message = "Unsupported operation. Allowed: create, update, delete"
                });

                continue;
            }

            /*
             * Для create/update данные обязательны.
             */
            if ((operation == "create" || operation == "update") &&
                (op.Data.ValueKind == JsonValueKind.Undefined ||
                 op.Data.ValueKind == JsonValueKind.Null))
            {
                results.Add(new SyncOperationResult
                {
                    Id = op.Id,
                    Status = "Invalid",
                    Message = "Data is required for create/update operation"
                });

                continue;
            }

            /*
             * Создаём запись очереди.
             */
            var entry = new SyncQueueEntry
            {
                Id = op.Id,
                UserId = op.UserId,
                EntityType = op.EntityType.Trim(),
                EntityId = op.EntityId,
                Operation = operation,
                Data = op.Data.ValueKind == JsonValueKind.Undefined
                    ? null
                    : op.Data.GetRawText(),
                Version = op.Version,
                Attempts = 0,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.SyncQueue.Add(entry);

            results.Add(new SyncOperationResult
            {
                Id = entry.Id,
                Status = "Accepted",
                Message = "Operation accepted for processing"
            });
        }

        await _dbContext.SaveChangesAsync();

        return Ok(results);
    }
}