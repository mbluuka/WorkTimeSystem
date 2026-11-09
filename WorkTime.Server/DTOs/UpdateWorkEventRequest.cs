using WorkTime.Server.Models;

namespace WorkTime.Server.DTOs;

// Тело запроса ручной корректировки события (FR7).
// Тип и метку времени может менять только владелец записи или руководитель/админ.
public class UpdateWorkEventRequest
{
    public WorkEventType Type { get; set; }

    public DateTime Timestamp { get; set; }

    public Guid? ProjectId { get; set; }
}
