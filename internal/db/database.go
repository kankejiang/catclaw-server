package db

import (
	"database/sql"
	"fmt"
	"time"

	_ "github.com/mattn/go-sqlite3"
)

// Database wraps the SQLite connection for the music metadata cache.
type Database struct {
	conn *sql.DB
}

// Song represents a cached song in the server database.
type Song struct {
	ID           int64  `json:"id"`
	Title        string `json:"title"`
	ArtistID     int64  `json:"artist_id"`
	AlbumID      int64  `json:"album_id"`
	Artist       string `json:"artist"`
	Album        string `json:"album"`
	Duration     int    `json:"duration"`
	FilePath     string `json:"file_path"`
	FileSize     int64  `json:"file_size"`
	Bitrate      int    `json:"bitrate"`
	TrackNumber  int    `json:"track_number"`
	Year         int    `json:"year"`
	Genre        string `json:"genre"`
	DateAdded    int64  `json:"date_added"`
	DateModified int64  `json:"date_modified"`
	CoverArtPath string `json:"cover_art_path,omitempty"`
	LyricsPath   string `json:"lyrics_path,omitempty"`
	AllArtists   string `json:"all_artists"`
}

// Artist represents an artist in the server database.
type Artist struct {
	ID          int64  `json:"id"`
	Name        string `json:"name"`
	Cover       string `json:"cover,omitempty"`
	Gender      string `json:"gender,omitempty"`
	Birthday    string `json:"birthday,omitempty"`
	Region      string `json:"region,omitempty"`
	Description string `json:"description,omitempty"`
}

// Album represents an album in the server database.
type Album struct {
	ID       int64  `json:"id"`
	Title    string `json:"title"`
	ArtistID int64  `json:"artist_id"`
	Artist   string `json:"artist"`
}

// Playlist represents a playlist.
type Playlist struct {
	ID        int64  `json:"id"`
	Name      string `json:"name"`
	CreatedAt int64  `json:"created_at"`
	SongCount int    `json:"song_count"`
}

// MetadataResponse is the complete metadata export sent to clients.
type MetadataResponse struct {
	Songs     []Song     `json:"songs"`
	Artists   []Artist   `json:"artists"`
	Albums    []Album    `json:"albums"`
	Playlists []Playlist `json:"playlists"`
	Version   int64      `json:"version"`
}

// New creates a new Database and ensures the schema exists.
func New(path string) (*Database, error) {
	conn, err := sql.Open("sqlite3", path+"?_journal=WAL&_busy_timeout=5000")
	if err != nil {
		return nil, fmt.Errorf("open database: %w", err)
	}

	conn.SetMaxOpenConns(1) // SQLite is single-writer
	conn.SetConnMaxLifetime(0)

	db := &Database{conn: conn}
	if err := db.migrate(); err != nil {
		return nil, fmt.Errorf("migrate: %w", err)
	}
	return db, nil
}

func (db *Database) Close() error {
	return db.conn.Close()
}

func (db *Database) migrate() error {
	schema := `
	CREATE TABLE IF NOT EXISTS Artists (
		Id INTEGER PRIMARY KEY AUTOINCREMENT,
		Name TEXT NOT NULL UNIQUE,
		Cover TEXT,
		Gender TEXT,
		Birthday TEXT,
		Region TEXT,
		Description TEXT
	);

	CREATE TABLE IF NOT EXISTS Albums (
		Id INTEGER PRIMARY KEY AUTOINCREMENT,
		Title TEXT NOT NULL,
		ArtistId INTEGER NOT NULL,
		UNIQUE(Title, ArtistId)
	);

	CREATE TABLE IF NOT EXISTS Songs (
		Id INTEGER PRIMARY KEY AUTOINCREMENT,
		Title TEXT NOT NULL,
		ArtistId INTEGER NOT NULL DEFAULT 0,
		AlbumId INTEGER NOT NULL DEFAULT 0,
		Duration INTEGER NOT NULL DEFAULT 0,
		FilePath TEXT NOT NULL UNIQUE,
		FileSize INTEGER NOT NULL DEFAULT 0,
		Bitrate INTEGER NOT NULL DEFAULT 0,
		TrackNumber INTEGER NOT NULL DEFAULT 0,
		Year INTEGER NOT NULL DEFAULT 0,
		Genre TEXT,
		DateAdded INTEGER NOT NULL DEFAULT 0,
		DateModified INTEGER NOT NULL DEFAULT 0,
		CoverArtPath TEXT,
		LyricsPath TEXT
	);

	CREATE TABLE IF NOT EXISTS SongArtists (
		Id INTEGER PRIMARY KEY AUTOINCREMENT,
		SongId INTEGER NOT NULL,
		ArtistId INTEGER NOT NULL,
		FOREIGN KEY (SongId) REFERENCES Songs(Id),
		FOREIGN KEY (ArtistId) REFERENCES Artists(Id)
	);

	CREATE TABLE IF NOT EXISTS Playlists (
		Id INTEGER PRIMARY KEY AUTOINCREMENT,
		Name TEXT NOT NULL,
		CreatedAt INTEGER NOT NULL DEFAULT 0,
		UpdatedAt INTEGER NOT NULL DEFAULT 0
	);

	CREATE TABLE IF NOT EXISTS PlaylistSongs (
		Id INTEGER PRIMARY KEY AUTOINCREMENT,
		PlaylistId INTEGER NOT NULL,
		SongId INTEGER NOT NULL,
		Position INTEGER NOT NULL DEFAULT 0,
		FOREIGN KEY (PlaylistId) REFERENCES Playlists(Id),
		FOREIGN KEY (SongId) REFERENCES Songs(Id)
	);

	CREATE TABLE IF NOT EXISTS Favorites (
		Id INTEGER PRIMARY KEY AUTOINCREMENT,
		SongId INTEGER NOT NULL UNIQUE,
		AddedAt INTEGER NOT NULL DEFAULT 0,
		FOREIGN KEY (SongId) REFERENCES Songs(Id)
	);

	CREATE TABLE IF NOT EXISTS PlayHistory (
		Id INTEGER PRIMARY KEY AUTOINCREMENT,
		SongId INTEGER NOT NULL,
		PlayedAt INTEGER NOT NULL DEFAULT 0,
		PlayCount INTEGER NOT NULL DEFAULT 1
	);

	CREATE INDEX IF NOT EXISTS idx_songs_artist ON Songs(ArtistId);
	CREATE INDEX IF NOT EXISTS idx_songs_album ON Songs(AlbumId);
	CREATE INDEX IF NOT EXISTS idx_songs_title ON Songs(Title);
	CREATE INDEX IF NOT EXISTS idx_song_artists_song ON SongArtists(SongId);
	CREATE INDEX IF NOT EXISTS idx_song_artists_artist ON SongArtists(ArtistId);
	CREATE INDEX IF NOT EXISTS idx_albums_artist ON Albums(ArtistId);
	`

	_, err := db.conn.Exec(schema)
	return err
}

// EnsureArtist returns the artist ID, creating if needed.
func (db *Database) EnsureArtist(name string) (int64, error) {
	if name == "" {
		name = "未知艺术家"
	}
	var id int64
	err := db.conn.QueryRow("SELECT Id FROM Artists WHERE Name = ?", name).Scan(&id)
	if err == sql.ErrNoRows {
		res, err := db.conn.Exec("INSERT INTO Artists (Name) VALUES (?)", name)
		if err != nil {
			return 0, err
		}
		return res.LastInsertId()
	}
	return id, err
}

// EnsureAlbum returns the album ID, creating if needed.
func (db *Database) EnsureAlbum(title string, artistID int64) (int64, error) {
	if title == "" {
		title = "未知专辑"
	}
	var id int64
	err := db.conn.QueryRow("SELECT Id FROM Albums WHERE Title = ? AND ArtistId = ?", title, artistID).Scan(&id)
	if err == sql.ErrNoRows {
		res, err := db.conn.Exec("INSERT INTO Albums (Title, ArtistId) VALUES (?, ?)", title, artistID)
		if err != nil {
			return 0, err
		}
		return res.LastInsertId()
	}
	return id, err
}

// InsertSong inserts a song, returning the row ID (or existing ID if duplicate path).
func (db *Database) InsertSong(s *Song) (int64, error) {
	var existing int64
	err := db.conn.QueryRow("SELECT Id FROM Songs WHERE FilePath = ?", s.FilePath).Scan(&existing)
	if err == nil {
		return existing, nil
	}

	now := time.Now().Unix()
	res, err := db.conn.Exec(`
		INSERT INTO Songs (Title, ArtistId, AlbumId, Duration, FilePath, FileSize,
			Bitrate, TrackNumber, Year, Genre, DateAdded, DateModified, CoverArtPath, LyricsPath)
		VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
	`, s.Title, s.ArtistID, s.AlbumID, s.Duration, s.FilePath, s.FileSize,
		s.Bitrate, s.TrackNumber, s.Year, s.Genre, now, s.DateModified, s.CoverArtPath, s.LyricsPath)
	if err != nil {
		return 0, err
	}
	return res.LastInsertId()
}

// GetAllSongs returns all songs with artist and album names joined.
func (db *Database) GetAllSongs() ([]Song, error) {
	rows, err := db.conn.Query(`
		SELECT s.Id, s.Title, s.ArtistId, s.AlbumId,
			COALESCE(a.Name, '未知艺术家') as Artist,
			COALESCE(al.Title, '未知专辑') as Album,
			s.Duration, s.FilePath, s.FileSize, s.Bitrate,
			s.TrackNumber, s.Year, COALESCE(s.Genre, '') as Genre,
			s.DateAdded, s.DateModified,
			COALESCE(s.CoverArtPath, '') as CoverArtPath,
			COALESCE(s.LyricsPath, '') as LyricsPath
		FROM Songs s
		LEFT JOIN Artists a ON s.ArtistId = a.Id
		LEFT JOIN Albums al ON s.AlbumId = al.Id
		ORDER BY s.Title
	`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var songs []Song
	for rows.Next() {
		var s Song
		if err := rows.Scan(&s.ID, &s.Title, &s.ArtistID, &s.AlbumID,
			&s.Artist, &s.Album,
			&s.Duration, &s.FilePath, &s.FileSize, &s.Bitrate,
			&s.TrackNumber, &s.Year, &s.Genre,
			&s.DateAdded, &s.DateModified,
			&s.CoverArtPath, &s.LyricsPath); err != nil {
			return nil, err
		}
		songs = append(songs, s)
	}
	return songs, nil
}

// GetSongByID returns a single song.
func (db *Database) GetSongByID(id int64) (*Song, error) {
	var s Song
	err := db.conn.QueryRow(`
		SELECT s.Id, s.Title, s.ArtistId, s.AlbumId,
			COALESCE(a.Name, '未知艺术家') as Artist,
			COALESCE(al.Title, '未知专辑') as Album,
			s.Duration, s.FilePath, s.FileSize, s.Bitrate,
			s.TrackNumber, s.Year, COALESCE(s.Genre, '') as Genre,
			s.DateAdded, s.DateModified,
			COALESCE(s.CoverArtPath, '') as CoverArtPath,
			COALESCE(s.LyricsPath, '') as LyricsPath
		FROM Songs s
		LEFT JOIN Artists a ON s.ArtistId = a.Id
		LEFT JOIN Albums al ON s.AlbumId = al.Id
		WHERE s.Id = ?
	`, id).Scan(&s.ID, &s.Title, &s.ArtistID, &s.AlbumID,
		&s.Artist, &s.Album,
		&s.Duration, &s.FilePath, &s.FileSize, &s.Bitrate,
		&s.TrackNumber, &s.Year, &s.Genre,
		&s.DateAdded, &s.DateModified,
		&s.CoverArtPath, &s.LyricsPath)
	if err != nil {
		return nil, err
	}
	return &s, nil
}

// SearchSongs searches by keyword in title, artist, or album.
func (db *Database) SearchSongs(keyword string) ([]Song, error) {
	kw := "%" + keyword + "%"
	rows, err := db.conn.Query(`
		SELECT s.Id, s.Title, s.ArtistId, s.AlbumId,
			COALESCE(a.Name, '未知艺术家') as Artist,
			COALESCE(al.Title, '未知专辑') as Album,
			s.Duration, s.FilePath, s.FileSize, s.Bitrate,
			s.TrackNumber, s.Year, COALESCE(s.Genre, '') as Genre,
			s.DateAdded, s.DateModified,
			COALESCE(s.CoverArtPath, '') as CoverArtPath,
			COALESCE(s.LyricsPath, '') as LyricsPath
		FROM Songs s
		LEFT JOIN Artists a ON s.ArtistId = a.Id
		LEFT JOIN Albums al ON s.AlbumId = al.Id
		WHERE s.Title LIKE ? OR a.Name LIKE ? OR al.Title LIKE ?
		ORDER BY s.Title
		LIMIT 200
	`, kw, kw, kw)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var songs []Song
	for rows.Next() {
		var s Song
		if err := rows.Scan(&s.ID, &s.Title, &s.ArtistID, &s.AlbumID,
			&s.Artist, &s.Album,
			&s.Duration, &s.FilePath, &s.FileSize, &s.Bitrate,
			&s.TrackNumber, &s.Year, &s.Genre,
			&s.DateAdded, &s.DateModified,
			&s.CoverArtPath, &s.LyricsPath); err != nil {
			return nil, err
		}
		songs = append(songs, s)
	}
	return songs, nil
}

// GetAllArtists returns all artists.
func (db *Database) GetAllArtists() ([]Artist, error) {
	rows, err := db.conn.Query(`SELECT Id, Name, COALESCE(Cover, '') FROM Artists ORDER BY Name`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var artists []Artist
	for rows.Next() {
		var a Artist
		if err := rows.Scan(&a.ID, &a.Name, &a.Cover); err != nil {
			return nil, err
		}
		artists = append(artists, a)
	}
	return artists, nil
}

// GetAllAlbums returns all albums.
func (db *Database) GetAllAlbums() ([]Album, error) {
	rows, err := db.conn.Query(`
		SELECT al.Id, al.Title, al.ArtistId, COALESCE(a.Name, '未知艺术家')
		FROM Albums al
		LEFT JOIN Artists a ON al.ArtistId = a.Id
		ORDER BY al.Title
	`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var albums []Album
	for rows.Next() {
		var al Album
		if err := rows.Scan(&al.ID, &al.Title, &al.ArtistID, &al.Artist); err != nil {
			return nil, err
		}
		albums = append(albums, al)
	}
	return albums, nil
}

// GetSongCount returns the total number of songs.
func (db *Database) GetSongCount() (int, error) {
	var count int
	err := db.conn.QueryRow("SELECT COUNT(*) FROM Songs").Scan(&count)
	return count, err
}

// GetDatabaseVersion returns a version number that changes on every write (for sync).
func (db *Database) GetDatabaseVersion() (int64, error) {
	var count int64
	err := db.conn.QueryRow("SELECT COALESCE(MAX(DateAdded), 0) FROM Songs").Scan(&count)
	return count, err
}

// EnsureSongArtist creates a SongArtist association.
func (db *Database) EnsureSongArtist(songID, artistID int64) error {
	var existing int64
	err := db.conn.QueryRow("SELECT Id FROM SongArtists WHERE SongId = ? AND ArtistId = ?",
		songID, artistID).Scan(&existing)
	if err == sql.ErrNoRows {
		_, err = db.conn.Exec("INSERT INTO SongArtists (SongId, ArtistId) VALUES (?, ?)", songID, artistID)
	}
	return err
}
