using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CatClawMusicServer.Models;

/// <summary>每日播放统计预聚合（按用户+日期）</summary>
public class StatsDaily
{
    [Key]
    public long Id { get; set; }

    public long UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public DateTime Date { get; set; }

    public int PlayCount { get; set; }

    public long TotalDurationMs { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
