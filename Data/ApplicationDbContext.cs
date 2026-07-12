using System;
using CatClawMusicServer.Models;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Song> Songs => Set<Song>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistSong> PlaylistSongs => Set<PlaylistSong>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<PlayHistory> PlayHistories => Set<PlayHistory>();

    // ── V2 新增 ──
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<SongGenre> SongGenres => Set<SongGenre>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<Scrobble> Scrobbles => Set<Scrobble>();
    public DbSet<PlayQueue> PlayQueues => Set<PlayQueue>();
    public DbSet<StatsDaily> StatsDaily => Set<StatsDaily>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── 表名（禁用复数化）──
        modelBuilder.Entity<Song>().ToTable("Songs");
        modelBuilder.Entity<Artist>().ToTable("Artists");
        modelBuilder.Entity<Album>().ToTable("Albums");
        modelBuilder.Entity<Playlist>().ToTable("Playlists");
        modelBuilder.Entity<PlaylistSong>().ToTable("PlaylistSongs");
        modelBuilder.Entity<Favorite>().ToTable("Favorites");
        modelBuilder.Entity<PlayHistory>().ToTable("PlayHistory");
        modelBuilder.Entity<User>().ToTable("Users");
        modelBuilder.Entity<RefreshToken>().ToTable("RefreshTokens");
        modelBuilder.Entity<Device>().ToTable("Devices");
        modelBuilder.Entity<Genre>().ToTable("Genres");
        modelBuilder.Entity<SongGenre>().ToTable("SongGenres");
        modelBuilder.Entity<Rating>().ToTable("Ratings");
        modelBuilder.Entity<Scrobble>().ToTable("Scrobbles");
        modelBuilder.Entity<PlayQueue>().ToTable("PlayQueues");

        // ── 索引 ──
        modelBuilder.Entity<Song>().HasIndex(s => s.Title);
        modelBuilder.Entity<Artist>().HasIndex(a => a.Name);
        modelBuilder.Entity<Album>().HasIndex(a => a.Title);
        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<Genre>().HasIndex(g => g.Name).IsUnique();
        modelBuilder.Entity<RefreshToken>().HasIndex(rt => rt.Token);
        modelBuilder.Entity<Device>().HasIndex(d => new { d.UserId, d.DeviceId }).IsUnique();

        // ── 原有关系 ──
        modelBuilder.Entity<Song>()
            .HasOne(s => s.Artist)
            .WithMany(a => a.Songs)
            .HasForeignKey(s => s.ArtistId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Song>()
            .HasOne(s => s.Album)
            .WithMany(a => a.Songs)
            .HasForeignKey(s => s.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Album>()
            .HasOne(a => a.Artist)
            .WithMany(a => a.Albums)
            .HasForeignKey(a => a.ArtistId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlaylistSong>()
            .HasOne(ps => ps.Playlist)
            .WithMany(p => p.Songs)
            .HasForeignKey(ps => ps.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlaylistSong>()
            .HasOne(ps => ps.Song)
            .WithMany(s => s.PlaylistSongs)
            .HasForeignKey(ps => ps.SongId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlaylistSong>()
            .HasIndex(ps => new { ps.PlaylistId, ps.SongId })
            .IsUnique();

        modelBuilder.Entity<PlayHistory>()
            .HasOne(h => h.Song)
            .WithMany(s => s.PlayHistories)
            .HasForeignKey(h => h.SongId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── V2 新增关系 ──

        // Playlist → User
        modelBuilder.Entity<Playlist>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Favorite → User + Song
        modelBuilder.Entity<Favorite>()
            .HasOne(f => f.User)
            .WithMany(u => u.Favorites)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Favorite>()
            .HasOne(f => f.Song)
            .WithMany(s => s.Favorites)
            .HasForeignKey(f => f.SongId)
            .OnDelete(DeleteBehavior.Cascade);

        // Favorite 唯一约束改为 (UserId, SongId) — 每用户每歌只能收藏一次
        modelBuilder.Entity<Favorite>()
            .HasIndex(f => new { f.UserId, f.SongId })
            .IsUnique();

        // Rating → User + Song
        modelBuilder.Entity<Rating>()
            .HasOne(r => r.User)
            .WithMany(u => u.Ratings)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Rating>()
            .HasOne(r => r.Song)
            .WithMany(s => s.Ratings)
            .HasForeignKey(r => r.SongId)
            .OnDelete(DeleteBehavior.Cascade);

        // Rating 唯一约束 (UserId, SongId)
        modelBuilder.Entity<Rating>()
            .HasIndex(r => new { r.UserId, r.SongId })
            .IsUnique();

        // Scrobble → User + Song
        modelBuilder.Entity<Scrobble>()
            .HasOne(s => s.User)
            .WithMany(u => u.Scrobbles)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Scrobble>()
            .HasOne(sc => sc.Song)
            .WithMany(song => song.Scrobbles)
            .HasForeignKey(sc => sc.SongId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Scrobble>()
            .HasIndex(s => new { s.UserId, s.Timestamp });

        // PlayQueue → User（每用户最多一条）
        modelBuilder.Entity<PlayQueue>()
            .HasOne(pq => pq.User)
            .WithMany(u => u.PlayQueues)
            .HasForeignKey(pq => pq.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlayQueue>()
            .HasIndex(pq => pq.UserId)
            .IsUnique();

        // RefreshToken → User
        modelBuilder.Entity<RefreshToken>()
            .HasOne(rt => rt.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Device → User
        modelBuilder.Entity<Device>()
            .HasOne(d => d.User)
            .WithMany(u => u.Devices)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // SongGenre 复合主键
        modelBuilder.Entity<SongGenre>()
            .HasKey(sg => new { sg.SongId, sg.GenreId });

        modelBuilder.Entity<SongGenre>()
            .HasOne(sg => sg.Song)
            .WithMany(s => s.SongGenres)
            .HasForeignKey(sg => sg.SongId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SongGenre>()
            .HasOne(sg => sg.Genre)
            .WithMany(g => g.SongGenres)
            .HasForeignKey(sg => sg.GenreId)
            .OnDelete(DeleteBehavior.Cascade);

        // StatsDaily: 每用户每天一条记录
        modelBuilder.Entity<StatsDaily>()
            .HasIndex(s => new { s.UserId, s.Date })
            .IsUnique();
    }
}
