using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CatClawMusicServer.Models;

public class RefreshToken
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [Required, MaxLength(200)]
    public string Token { get; set; } = "";

    [MaxLength(200)]
    public string DeviceName { get; set; } = "";

    [MaxLength(200)]
    public string? DeviceId { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsRevoked { get; set; }
}
