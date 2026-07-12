using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CatClawMusicServer.Models;

public class PlayQueue
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>JSON array of song IDs, e.g. [1,5,23,7]</summary>
    [Required]
    public string SongIds { get; set; } = "[]";

    public int CurrentIndex { get; set; }

    public long PositionMs { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
