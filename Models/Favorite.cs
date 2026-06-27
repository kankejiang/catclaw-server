using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CatClawMusicServer.Models;

public class Favorite
{
    [Key]
    public long Id { get; set; }

    public long SongId { get; set; }

    [ForeignKey(nameof(SongId))]
    public Song? Song { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
