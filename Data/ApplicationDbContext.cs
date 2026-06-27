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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 表名（禁用复数化）
        modelBuilder.Entity<Song>().ToTable("Songs");
        modelBuilder.Entity<Artist>().ToTable("Artists");
        modelBuilder.Entity<Album>().ToTable("Albums");
        modelBuilder.Entity<Playlist>().ToTable("Playlists");
        modelBuilder.Entity<PlaylistSong>().ToTable("PlaylistSongs");
        modelBuilder.Entity<Favorite>().ToTable("Favorites");
        modelBuilder.Entity<PlayHistory>().ToTable("PlayHistory");

        // 索引
        modelBuilder.Entity<Song>().HasIndex(s => s.Title);
        modelBuilder.Entity<Artist>().HasIndex(a => a.Name);
        modelBuilder.Entity<Album>().HasIndex(a => a.Title);

        // 关系配置
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

        modelBuilder.Entity<Favorite>()
            .HasOne(f => f.Song)
            .WithMany(s => s.Favorites)
            .HasForeignKey(f => f.SongId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlayHistory>()
            .HasOne(h => h.Song)
            .WithMany(s => s.PlayHistories)
            .HasForeignKey(h => h.SongId)
            .OnDelete(DeleteBehavior.Cascade);

        // 唯一约束：PlaylistSong(PlaylistId, SongId)
        modelBuilder.Entity<PlaylistSong>()
            .HasIndex(ps => new { ps.PlaylistId, ps.SongId })
            .IsUnique();

        // 唯一约束：Favorite(SongId) - 每首歌只能收藏一次（简化）
        modelBuilder.Entity<Favorite>()
            .HasIndex(f => f.SongId)
            .IsUnique();
    }
}
