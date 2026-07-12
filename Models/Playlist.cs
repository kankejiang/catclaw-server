using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CatClawMusicServer.Models;

public class Playlist
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    [MaxLength(1000)]
    public string? Description { get; set; }

    public bool IsPublic { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // 导航属性
    public ICollection<PlaylistSong>? Songs { get; set; }
}
