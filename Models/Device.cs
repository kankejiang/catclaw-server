using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CatClawMusicServer.Models;

public class Device
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [Required, MaxLength(200)]
    public string DeviceId { get; set; } = "";

    [MaxLength(200)]
    public string DeviceName { get; set; } = "";

    [MaxLength(50)]
    public string Platform { get; set; } = ""; // "android" | "ios" | "web" | "windows"

    [MaxLength(50)]
    public string? AppVersion { get; set; }

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
