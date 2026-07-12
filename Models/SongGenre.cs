namespace CatClawMusicServer.Models;

public class SongGenre
{
    public long SongId { get; set; }
    public Song? Song { get; set; }

    public long GenreId { get; set; }
    public Genre? Genre { get; set; }
}
