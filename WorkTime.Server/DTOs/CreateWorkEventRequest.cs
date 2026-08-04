using System.ComponentModel.DataAnnotations;
using WorkTime.Server.Models;

namespace WorkTime.Server.DTOs;

public class CreateWorkEventRequest
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public Guid DeviceId { get; set; }

    [Required]
    public WorkEventType Type { get; set; }

    [Required]
    public DateTime Timestamp { get; set; }
}