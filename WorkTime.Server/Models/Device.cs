namespace WorkTime.Server.Models;

public class Device
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }

    public ICollection<WorkEvent> WorkEvents { get; set; } = new List<WorkEvent>();
}