using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CatClawMusicServer.Models;

public class Scrobble
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public long SongId { get; set; }

    [ForeignKey(nameof(SongId))]
    public Song? Song { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public int DurationPlayedMs { get; set; }

    [MaxLength(50)]
    public string Source { get; set; } = "library"; // "library" | "playlist" | "search" | "recommend" | "p2p"
}
