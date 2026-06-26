package scanner

import (
	"fmt"
	"log"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"github.com/dhowden/tag"
	"github.com/kankejiang/catclaw-server/internal/db"
)

// Scanner scans a music directory and stores metadata in the database.
type Scanner struct {
	musicDir string
	mu       sync.RWMutex
	database *db.Database
}

// New creates a new Scanner.
func New(musicDir string, database *db.Database) *Scanner {
	return &Scanner{
		musicDir: musicDir,
		database: database,
	}
}

// MusicDir returns the current music directory.
func (s *Scanner) MusicDir() string {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.musicDir
}

// SetMusicDir updates the music directory.
func (s *Scanner) SetMusicDir(dir string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.musicDir = dir
}

// ScanResult holds the result of a scan.
type ScanResult struct {
	Total    int `json:"total"`
	New      int `json:"new"`
	Errors   int `json:"errors"`
	Duration string `json:"duration"`
}

// Scan performs a full scan of the music directory.
func (s *Scanner) Scan() (*ScanResult, error) {
	start := time.Now()
	result := &ScanResult{}
	dir := s.MusicDir()

	log.Printf("[scanner] Starting scan of %s", dir)

	err := filepath.Walk(dir, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			result.Errors++
			return nil // Skip files we can't access
		}

		if info.IsDir() {
			return nil
		}

		ext := strings.ToLower(filepath.Ext(path))
		if !isMusicFile(ext) {
			return nil
		}

		result.Total++

		song, err := s.parseFile(path, info)
		if err != nil {
			result.Errors++
			log.Printf("[scanner] Error parsing %s: %v", path, err)
			return nil
		}

		// Ensure artist and album exist
		artistID, err := s.database.EnsureArtist(song.Artist)
		if err != nil {
			result.Errors++
			return nil
		}
		song.ArtistID = artistID

		albumID, err := s.database.EnsureAlbum(song.Album, artistID)
		if err != nil {
			result.Errors++
			return nil
		}
		song.AlbumID = albumID

		id, err := s.database.InsertSong(song)
		if err != nil {
			result.Errors++
			log.Printf("[scanner] Error inserting %s: %v", path, err)
			return nil
		}

		if id > 0 {
			result.New++
		}

		// Create SongArtist association
		if err := s.database.EnsureSongArtist(id, artistID); err != nil {
			log.Printf("[scanner] Error creating SongArtist: %v", err)
		}

		return nil
	})

	result.Duration = time.Since(start).Round(time.Millisecond).String()
	log.Printf("[scanner] Scan complete: %d total, %d new, %d errors in %s",
		result.Total, result.New, result.Errors, result.Duration)

	return result, err
}

func (s *Scanner) parseFile(path string, info os.FileInfo) (*db.Song, error) {
	f, err := os.Open(path)
	if err != nil {
		return nil, fmt.Errorf("open: %w", err)
	}
	defer f.Close()

	metadata, err := tag.ReadFrom(f)
	if err != nil {
		// If we can't read tags, create a minimal entry from filename
		return &db.Song{
			Title:        strings.TrimSuffix(info.Name(), filepath.Ext(info.Name())),
			Artist:       "",
			Album:        "",
			FilePath:     path,
			FileSize:     info.Size(),
			DateModified: info.ModTime().Unix(),
		}, nil
	}

	// Extract artists
	var allArtists []string
	artist := metadata.Artist()
	if artist != "" {
		allArtists = strings.Split(artist, "/")
		for i := range allArtists {
			allArtists[i] = strings.TrimSpace(allArtists[i])
		}
	} else {
		allArtists = []string{""}
	}

	primaryArtist := allArtists[0]
	album := metadata.Album()

	trackNum, _ := metadata.Track()
	year := metadata.Year()
	genre := metadata.Genre()

	duration := 0
	bitrate := 0
	if props := metadata.File(); props != nil {
		duration = int(props.Length().Milliseconds())
		bitrate = props.BitRate()
	}

	return &db.Song{
		Title:        metadata.Title(),
		Artist:       primaryArtist,
		AllArtists:   strings.Join(allArtists, " / "),
		Album:        album,
		Duration:     duration,
		FilePath:     path,
		FileSize:     info.Size(),
		Bitrate:      bitrate,
		TrackNumber:  trackNum,
		Year:         year,
		Genre:        genre,
		DateModified: info.ModTime().Unix(),
	}, nil
}

func isMusicFile(ext string) bool {
	switch ext {
	case ".mp3", ".flac", ".wav", ".ogg", ".opus", ".m4a", ".aac",
		".wma", ".ape", ".dsf", ".dff", ".aiff", ".alac", ".wv", ".tta":
		return true
	}
	return false
}
