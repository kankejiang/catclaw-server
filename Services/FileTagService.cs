using TagLib;
using File = TagLib.File;

namespace CatClawMusicServer.Services;

public record AudioTags(
    string Title,
    string Artist,
    string Album,
    int Duration,      // seconds
    int Bitrate,
    int TrackNumber,
    int Year,
    string Genre,
    string? CoverPath); // 提取的封面保存路径

public static class FileTagService
{
    /// <summary>
    /// 读取音频文件标签，支持 MP3/FLAC/WAV/WMA/OGG/AIFF/M4A/APE/WV/MP4/MP2/MPC/TTA/OPUS
    /// </summary>
    public static AudioTags? ReadTags(string filePath, string coverOutputDir)
    {
        try
        {
            using var tagFile = File.Create(filePath);
            var tag = tagFile.Tag;
            var properties = tagFile.Properties;

            // 提取封面
            string? coverPath = null;
            if (tag.Pictures.Length > 0)
            {
                var pic = tag.Pictures[0];
                var ext = pic.MimeType switch
                {
                    "image/jpeg" => ".jpg",
                    "image/png" => ".png",
                    "image/webp" => ".webp",
                    _ => ".jpg"
                };
                var fileName = $"{Path.GetFileNameWithoutExtension(filePath)}{ext}";
                coverPath = Path.Combine(coverOutputDir, fileName);
                Directory.CreateDirectory(coverOutputDir);
                System.IO.File.WriteAllBytes(coverPath, pic.Data.Data);
            }

            return new AudioTags(
                Title: !string.IsNullOrWhiteSpace(tag.Title)
                    ? tag.Title.Trim()
                    : Path.GetFileNameWithoutExtension(filePath),
                Artist: !string.IsNullOrWhiteSpace(tag.FirstPerformer)
                    ? tag.FirstPerformer.Trim()
                    : "未知艺术家",
                Album: !string.IsNullOrWhiteSpace(tag.Album)
                    ? tag.Album.Trim()
                    : "未知专辑",
                Duration: (int)(properties?.Duration.TotalSeconds ?? 0),
                Bitrate: properties?.AudioBitrate ?? 0,
                TrackNumber: (int)tag.Track,
                Year: (int)tag.Year,
                Genre: tag.Genres.Length > 0 ? string.Join(", ", tag.Genres) : "",
                CoverPath: coverPath
            );
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 查找同目录下的歌词文件 (.lrc / .ttml)
    /// </summary>
    public static string? FindLyricsFile(string audioFilePath)
    {
        var dir = Path.GetDirectoryName(audioFilePath)!;
        var name = Path.GetFileNameWithoutExtension(audioFilePath);

        var lrc = Path.Combine(dir, name + ".lrc");
        if (System.IO.File.Exists(lrc)) return lrc;

        var ttml = Path.Combine(dir, name + ".ttml");
        if (System.IO.File.Exists(ttml)) return ttml;

        return null;
    }

    /// <summary>
    /// 查找同目录下的封面文件 (cover.jpg/png 等)
    /// </summary>
    public static string? FindCoverFile(string audioFilePath)
    {
        var dir = Path.GetDirectoryName(audioFilePath)!;
        var baseName = Path.GetFileNameWithoutExtension(audioFilePath);

        var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        foreach (var ext in extensions)
        {
            var path = Path.Combine(dir, baseName + ext);
            if (System.IO.File.Exists(path)) return path;
        }

        // 也检查通用封面名
        foreach (var ext in extensions)
        {
            var path = Path.Combine(dir, "cover" + ext);
            if (System.IO.File.Exists(path)) return path;
            path = Path.Combine(dir, "folder" + ext);
            if (System.IO.File.Exists(path)) return path;
        }

        return null;
    }
}
