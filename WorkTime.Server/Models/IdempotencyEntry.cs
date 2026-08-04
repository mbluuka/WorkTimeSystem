using System.ComponentModel.DataAnnotations;

namespace WorkTime.Server.Models;

public class IdempotencyEntry
{
    [Key]
    public string Key { get; set; } = null!;

    public int StatusCode { get; set; }

    public string? ContentType { get; set; }

    public byte[]? Body { get; set; }

    public DateTime CreatedAt { get; set; }
}
