using System.Text.RegularExpressions;

namespace CatClawMusicServer.Services;

/// <summary>
/// 歌词解析服务 — 支持 LRC（逐行时间轴）、TTML/AMLL（逐字时间轴）、纯文本。
/// 返回结构化歌词数据供前端渲染。
/// </summary>
public static partial class LyricsParser
{
    /// <summary>解析歌词文件内容，返回结构化结果</summary>
    public static LyricsResult Parse(string content, string fileExtension)
    {
        if (string.IsNullOrWhiteSpace(content))
            return LyricsResult.Plain("");

        var ext = fileExtension.ToLowerInvariant().TrimStart('.');

        return ext switch
        {
            "lrc" => ParseLrc(content),
            "ttml" => ParseTtml(content),
            _ => LyricsResult.Plain(content)
        };
    }

    // ── LRC 解析 ──
    private static LyricsResult ParseLrc(string content)
    {
        var lines = new List<LyricsLine>();
        string? translation = null;
        var translationLines = new List<string>();

        // 匹配 [mm:ss.xx] 或 [mm:ss:xx] 格式
        var timeRegex = LrcTimeRegex();

        foreach (var rawLine in content.Split('\n', StringSplitOptions.None))
        {
            var line = rawLine.TrimEnd('\r');

            // 跳过空行
            if (string.IsNullOrWhiteSpace(line)) continue;

            // 跳过元数据行 [ti:xxx] [ar:xxx] 等（不含数字的标签）
            if (MetadataTagRegex().IsMatch(line)) continue;

            // 匹配时间标签
            var matches = timeRegex.Matches(line);
            if (matches.Count > 0)
            {
                var text = timeRegex.Replace(line, "").Trim();
                var timeMs = ParseTimeMs(matches[0]);

                if (timeMs >= 0)
                {
                    lines.Add(new LyricsLine
                    {
                        StartTimeMs = timeMs,
                        Text = text,
                        Words = null
                    });
                }
            }
            else
            {
                // 无时间标签的纯文本行
                translationLines.Add(line);
            }
        }

        // 按时间排序
        lines.Sort((a, b) => a.StartTimeMs.CompareTo(b.StartTimeMs));

        // 如果有非时间标签的文本行，作为翻译
        if (translationLines.Count > 0 && lines.Count > 0)
            translation = string.Join("\n", translationLines);

        return new LyricsResult
        {
            Type = "lrc",
            Synced = lines.Count > 0,
            Lines = lines,
            Translation = translation,
            Content = content
        };
    }

    // ── TTML 解析（简化版，提取 <p> 标签中的时间轴和文本）──
    private static LyricsResult ParseTtml(string content)
    {
        var lines = new List<LyricsLine>();

        // 匹配 <p begin="00:00:05.100" end="00:00:08.200">text</p>
        var pRegex = TtmlParagraphRegex();
        foreach (Match match in pRegex.Matches(content))
        {
            var beginStr = match.Groups[1].Value;
            var innerHtml = match.Groups[2].Value;

            var beginMs = ParseTtmlTimeMs(beginStr);
            if (beginMs < 0) continue;

            // 提取纯文本（移除 HTML 标签）
            var text = HtmlTagRegex().Replace(innerHtml, "").Trim();
            if (string.IsNullOrEmpty(text)) continue;

            // 尝试提取逐字时间轴 <span begin="00:05.100">word</span>
            var words = new List<LyricsWord>();
            var spanRegex = TtmlSpanRegex();
            foreach (Match spanMatch in spanRegex.Matches(innerHtml))
            {
                var wordBegin = ParseTtmlTimeMs(spanMatch.Groups[1].Value);
                var wordText = HtmlTagRegex().Replace(spanMatch.Groups[2].Value, "").Trim();
                if (wordBegin >= 0 && !string.IsNullOrEmpty(wordText))
                {
                    words.Add(new LyricsWord { StartTimeMs = wordBegin, Text = wordText });
                }
            }

            lines.Add(new LyricsLine
            {
                StartTimeMs = beginMs,
                Text = text,
                Words = words.Count > 0 ? words : null
            });
        }

        lines.Sort((a, b) => a.StartTimeMs.CompareTo(b.StartTimeMs));

        return new LyricsResult
        {
            Type = "ttml",
            Synced = lines.Count > 0,
            Lines = lines,
            Content = content
        };
    }

    // ── 时间解析辅助 ──
    private static long ParseTimeMs(Match match)
    {
        var mm = int.Parse(match.Groups[1].Value);
        var ss = int.Parse(match.Groups[2].Value);
        var xx = int.Parse(match.Groups[3].Value.PadRight(2, '0')[..2]); // 统一为百分秒
        return mm * 60_000L + ss * 1000L + xx * 10L;
    }

    private static long ParseTtmlTimeMs(string timeStr)
    {
        // 支持 HH:MM:SS.mmm 和 MM:SS.mmm 格式
        var parts = timeStr.Split(':');
        try
        {
            if (parts.Length == 3)
            {
                var h = int.Parse(parts[0]);
                var m = int.Parse(parts[1]);
                var secParts = parts[2].Split('.');
                var s = int.Parse(secParts[0]);
                var ms = secParts.Length > 1 ? int.Parse(secParts[1].PadRight(3, '0')[..3]) : 0;
                return h * 3600_000L + m * 60_000L + s * 1000L + ms;
            }
            if (parts.Length == 2)
            {
                var m = int.Parse(parts[0]);
                var secParts = parts[1].Split('.');
                var s = int.Parse(secParts[0]);
                var ms = secParts.Length > 1 ? int.Parse(secParts[1].PadRight(3, '0')[..3]) : 0;
                return m * 60_000L + s * 1000L + ms;
            }
        }
        catch { }
        return -1;
    }

    // ── 正则表达式（GeneratedRegex，.NET 8 编译时生成）──
    [GeneratedRegex(@"\[(\d{1,3}):(\d{2})[.:](\d{1,3})\]")]
    private static partial Regex LrcTimeRegex();

    [GeneratedRegex(@"^\[[a-zA-Z]+:[^\]]*\]$")]
    private static partial Regex MetadataTagRegex();

    [GeneratedRegex(@"<p[^>]*\sbegin=""([^""]+)""[^>]*>([\s\S]*?)</p>", RegexOptions.IgnoreCase)]
    private static partial Regex TtmlParagraphRegex();

    [GeneratedRegex(@"<span[^>]*\sbegin=""([^""]+)""[^>]*>([\s\S]*?)</span>", RegexOptions.IgnoreCase)]
    private static partial Regex TtmlSpanRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}

// ── 歌词数据结构 ──

public class LyricsResult
{
    public string Type { get; set; } = "plain"; // "lrc" | "ttml" | "plain"
    public bool Synced { get; set; }
    public string Content { get; set; } = "";
    public string? Translation { get; set; }
    public List<LyricsLine> Lines { get; set; } = new();

    public static LyricsResult Plain(string text) => new()
    {
        Type = "plain",
        Synced = false,
        Content = text
    };
}

public class LyricsLine
{
    public long StartTimeMs { get; set; }
    public string Text { get; set; } = "";
    public List<LyricsWord>? Words { get; set; }
}

public class LyricsWord
{
    public long StartTimeMs { get; set; }
    public string Text { get; set; } = "";
}
