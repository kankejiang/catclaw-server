namespace CatClawMusicServer;

public record ScannerOptions
{
    public string MusicDirectory { get; set; } = "";
    public string CoverOutputDir { get; set; } = "";
}
