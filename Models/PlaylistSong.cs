using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CatClawMusicServer.Models;

public class PlaylistSong
{
    [Key]
    public long Id { get; set; }

    public long PlaylistId { get; set; }

    [ForeignKey(nameof(PlaylistId))]
    public Playlist? Playlist { get; set; }

    public long SongId { get; set; }

    [ForeignKey(nameof(SongId))]
    public Song? Song { get; set; }

    public int SortOrder { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
