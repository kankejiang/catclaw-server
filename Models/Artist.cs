using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CatClawMusicServer.Models;

public class Artist
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    [MaxLength(1000)]
    public string? Cover { get; set; }

    [MaxLength(20)]
    public string? Gender { get; set; }

    [MaxLength(20)]
    public string? Birthday { get; set; }

    [MaxLength(100)]
    public string? Region { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    // 导航属性
    public ICollection<Song>? Songs { get; set; }
    public ICollection<Album>? Albums { get; set; }
}
