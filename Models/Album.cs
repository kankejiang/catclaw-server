using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CatClawMusicServer.Models;

public class Album
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = "";

    public long ArtistId { get; set; }

    [ForeignKey(nameof(ArtistId))]
    public Artist? Artist { get; set; }

    [MaxLength(1000)]
    public string? Cover { get; set; }

    // 导航属性
    public ICollection<Song>? Songs { get; set; }
}
