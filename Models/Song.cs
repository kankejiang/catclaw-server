using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CatClawMusicServer.Models;

public class Song
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(500)]
    public string Title { get; set; } = "";

    public long ArtistId { get; set; }

    [ForeignKey(nameof(ArtistId))]
    public Artist? Artist { get; set; }

    public long AlbumId { get; set; }

    [ForeignKey(nameof(AlbumId))]
    public Album? Album { get; set; }

    public int Duration { get; set; } // seconds

    [MaxLength(1000)]
    public string FilePath { get; set; } = "";

    public long FileSize { get; set; }

    public int Bitrate { get; set; }

    public int TrackNumber { get; set; }

    public int Year { get; set; }

    [MaxLength(100)]
    public string Genre { get; set; } = "";

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    public DateTime DateModified { get; set; } = DateTime.UtcNow;

    [MaxLength(1000)]
    public string? CoverArtPath { get; set; }

    [MaxLength(1000)]
    public string? LyricsPath { get; set; }

    // 导航属性（用于播放列表关联）
    public ICollection<PlaylistSong>? PlaylistSongs { get; set; }
    public ICollection<Favorite>? Favorites { get; set; }
    public ICollection<PlayHistory>? PlayHistories { get; set; }
}
