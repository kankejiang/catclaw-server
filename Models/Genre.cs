using System;
using System.ComponentModel.DataAnnotations;

namespace CatClawMusicServer.Models;

public class Genre
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = "";

    public int SongCount { get; set; }

    // 导航属性
    public ICollection<SongGenre>? SongGenres { get; set; }
}
