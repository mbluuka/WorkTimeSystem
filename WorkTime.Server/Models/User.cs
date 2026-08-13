namespace WorkTime.Server.Models;

public class User
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PasswordHash { get; set; }

    public string Role { get; set; } = "Employee"; // Employee/Manager/Admin

    public string? FullName { get; set; }

    public bool IsBlocked { get; set; }

    public long SyncVersion { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<Device> Devices { get; set; } = new List<Device>();

    public ICollection<WorkEvent> WorkEvents { get; set; } = new List<WorkEvent>();
}