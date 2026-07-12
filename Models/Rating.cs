using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CatClawMusicServer.Models;

public class Rating
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public long SongId { get; set; }

    [ForeignKey(nameof(SongId))]
    public Song? Song { get; set; }

    public int Score { get; set; } // 1-5

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
