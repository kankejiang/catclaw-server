namespace CatClawMusicServer;

public class StreamingOptions
{
    public bool HlsEnabled { get; set; } = true;
    public int TranscodeCacheSizeGB { get; set; } = 2;
    public int[] DefaultBitrates { get; set; } = [96, 160, 256];
    public string FFmpegPath { get; set; } = "ffmpeg";
    public int SegmentDurationSeconds { get; set; } = 6;
    public int MaxConcurrentTranscodes { get; set; } = 4;
    public int SegmentWaitTimeoutMs { get; set; } = 30_000;
    public int IdleKillSeconds { get; set; } = 60;
    public string TranscodeDir { get; set; } = "Data/transcode";
}
