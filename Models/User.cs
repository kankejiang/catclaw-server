using System;
using System.ComponentModel.DataAnnotations;

namespace CatClawMusicServer.Models;

public class User
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(50)]
    public string Username { get; set; } = "";

    [Required]
    public string PasswordHash { get; set; } = "";

    [MaxLength(100)]
    public string DisplayName { get; set; } = "";

    [MaxLength(20)]
    public string Role { get; set; } = "user"; // "admin" | "user"

    [MaxLength(500)]
    public string? AvatarPath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;

    // 导航属性
    public ICollection<RefreshToken>? RefreshTokens { get; set; }
    public ICollection<Device>? Devices { get; set; }
    public ICollection<Favorite>? Favorites { get; set; }
    public ICollection<Scrobble>? Scrobbles { get; set; }
    public ICollection<Rating>? Ratings { get; set; }
    public ICollection<PlayQueue>? PlayQueues { get; set; }
}
