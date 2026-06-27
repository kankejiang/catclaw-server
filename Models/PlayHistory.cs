using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CatClawMusicServer.Models;

public class PlayHistory
{
    [Key]
    public long Id { get; set; }

    public long SongId { get; set; }

    [ForeignKey(nameof(SongId))]
    public Song? Song { get; set; }

    public DateTime PlayedAt { get; set; } = DateTime.UtcNow;

    public int PlayDuration { get; set; } // seconds played
}
