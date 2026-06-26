package api

import (
	"encoding/json"
	"fmt"
	"io"
	"io/fs"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"

	"github.com/kankejiang/catclaw-server/internal/db"
	"github.com/kankejiang/catclaw-server/internal/dht"
	"github.com/kankejiang/catclaw-server/internal/limiter"
	"github.com/kankejiang/catclaw-server/internal/scanner"
)

// RouterConfig holds configuration for the HTTP router.
type RouterConfig struct {
	Database *db.Database
	Limiter  limiter.Limiter
	DHTNode  *dht.Node
	Scanner  *scanner.Scanner
	MusicDir string
	WebFS    fs.FS
}

// NewRouter creates the HTTP handler with all routes.
func NewRouter(cfg RouterConfig) http.Handler {
	mux := http.NewServeMux()

	// API routes
	mux.HandleFunc("/api/songs", handleSongs(cfg))
	mux.HandleFunc("/api/songs/", handleSongByID(cfg))
	mux.HandleFunc("/api/stream/", handleStream(cfg))
	mux.HandleFunc("/api/search", handleSearch(cfg))
	mux.HandleFunc("/api/artists", handleArtists(cfg))
	mux.HandleFunc("/api/albums", handleAlbums(cfg))
	mux.HandleFunc("/api/scan", handleScan(cfg))
	mux.HandleFunc("/api/status", handleStatus(cfg))

	// DHT / P2P endpoints
	mux.HandleFunc("/api/dht/devices", handleDHTDevices(cfg))
	mux.HandleFunc("/api/dht/contacts", handleDHTContacts(cfg))

	// Rate limiter config
	mux.HandleFunc("/api/config/ratelimit", handleRateLimitConfig(cfg))
	mux.HandleFunc("/api/config/musicdir", handleMusicDirConfig(cfg))

	// Serve embedded web UI
	mux.Handle("/", http.FileServer(http.FS(cfg.WebFS)))

	return withCORS(withLogging(mux))
}

// ── Middleware ──

func withCORS(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Access-Control-Allow-Origin", "*")
		w.Header().Set("Access-Control-Allow-Methods", "GET, POST, PUT, OPTIONS")
		w.Header().Set("Access-Control-Allow-Headers", "Content-Type, Range")
		if r.Method == "OPTIONS" {
			w.WriteHeader(204)
			return
		}
		next.ServeHTTP(w, r)
	})
}

func withLogging(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		start := time.Now()
		next.ServeHTTP(w, r)
		log.Printf("%s %s %s", r.Method, r.URL.Path, time.Since(start).Round(time.Millisecond))
	})
}

// ── Handlers ──

func handleSongs(cfg RouterConfig) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		songs, err := cfg.Database.GetAllSongs()
		if err != nil {
			http.Error(w, err.Error(), 500)
			return
		}
		writeJSON(w, songs)
	}
}

func handleSongByID(cfg RouterConfig) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		idStr := strings.TrimPrefix(r.URL.Path, "/api/songs/")
		// Handle /api/songs/{id}/cover
		if strings.HasSuffix(idStr, "/cover") {
			idStr = strings.TrimSuffix(idStr, "/cover")
			id, err := strconv.ParseInt(idStr, 10, 64)
			if err != nil {
				http.Error(w, "invalid id", 400)
				return
			}
			handleCover(cfg, w, r, id)
			return
		}
		// Handle /api/songs/{id}
		id, err := strconv.ParseInt(idStr, 10, 64)
		if err != nil {
			http.Error(w, "invalid id", 400)
			return
		}
		song, err := cfg.Database.GetSongByID(id)
		if err != nil {
			http.Error(w, "not found", 404)
			return
		}
		writeJSON(w, song)
	}
}

func handleCover(cfg RouterConfig, w http.ResponseWriter, r *http.Request, songID int64) {
	song, err := cfg.Database.GetSongByID(songID)
	if err != nil {
		http.Error(w, "not found", 404)
		return
	}

	// Try to find cover art
	coverPaths := []string{}
	if song.CoverArtPath != "" {
		coverPaths = append(coverPaths, song.CoverArtPath)
	}
	// Look for common cover files in the same directory
	dir := filepath.Dir(song.FilePath)
	for _, name := range []string{"cover.jpg", "cover.png", "folder.jpg", "front.jpg", "albumart.jpg"} {
		coverPaths = append(coverPaths, filepath.Join(dir, name))
	}

	for _, p := range coverPaths {
		if f, err := os.Open(p); err == nil {
			defer f.Close()
			ext := strings.ToLower(filepath.Ext(p))
			ct := "image/jpeg"
			if ext == ".png" {
				ct = "image/png"
			}
			w.Header().Set("Content-Type", ct)
			io.Copy(w, f)
			return
		}
	}
	http.Error(w, "no cover art", 404)
}

func handleStream(cfg RouterConfig) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		idStr := strings.TrimPrefix(r.URL.Path, "/api/stream/")
		id, err := strconv.ParseInt(idStr, 10, 64)
		if err != nil {
			http.Error(w, "invalid id", 400)
			return
		}

		song, err := cfg.Database.GetSongByID(id)
		if err != nil {
			http.Error(w, "not found", 404)
			return
		}

		f, err := os.Open(song.FilePath)
		if err != nil {
			http.Error(w, "file not found", 404)
			return
		}
		defer f.Close()

		fileInfo, _ := f.Stat()
		fileSize := fileInfo.Size()

		// Determine content type
		ext := strings.ToLower(filepath.Ext(song.FilePath))
		contentType := contentTypeForExt(ext)

		w.Header().Set("Content-Type", contentType)
		w.Header().Set("Accept-Ranges", "bytes")

		// Handle Range request
		rangeHeader := r.Header.Get("Range")
		if rangeHeader != "" {
			handleRangeRequest(w, r, f, fileSize, contentType, cfg.Limiter)
			return
		}

		// Full file response with rate limiting
		w.Header().Set("Content-Length", fmt.Sprintf("%d", fileSize))
		reader := cfg.Limiter.Wrap(f)
		http.ServeContent(w, r, song.Title, time.Unix(song.DateModified, 0), reader.(io.ReadSeeker))
	}
}

func handleRangeRequest(w http.ResponseWriter, r *http.Request, f *os.File, fileSize int64, contentType string, lim limiter.Limiter) {
	var start, end int64
	fmt.Sscanf(r.Header.Get("Range"), "bytes=%d-%d", &start, &end)

	if end == 0 || end >= fileSize {
		end = fileSize - 1
	}

	w.Header().Set("Content-Range", fmt.Sprintf("bytes %d-%d/%d", start, end, fileSize))
	w.Header().Set("Content-Length", fmt.Sprintf("%d", end-start+1))
	w.Header().Set("Content-Type", contentType)
	w.WriteHeader(http.StatusPartialContent)

	f.Seek(start, 0)
	reader := lim.Wrap(io.LimitReader(f, end-start+1))
	io.Copy(w, reader)
}

func handleSearch(cfg RouterConfig) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		q := r.URL.Query().Get("q")
		if q == "" {
			http.Error(w, "missing query parameter 'q'", 400)
			return
		}
		songs, err := cfg.Database.SearchSongs(q)
		if err != nil {
			http.Error(w, err.Error(), 500)
			return
		}
		if songs == nil {
			songs = []db.Song{}
		}
		writeJSON(w, songs)
	}
}

func handleArtists(cfg RouterConfig) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		artists, err := cfg.Database.GetAllArtists()
		if err != nil {
			http.Error(w, err.Error(), 500)
			return
		}
		writeJSON(w, artists)
	}
}

func handleAlbums(cfg RouterConfig) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		albums, err := cfg.Database.GetAllAlbums()
		if err != nil {
			http.Error(w, err.Error(), 500)
			return
		}
		writeJSON(w, albums)
	}
}

func handleScan(cfg RouterConfig) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodPost {
			http.Error(w, "method not allowed", 405)
			return
		}
		result, err := cfg.Scanner.Scan()
		if err != nil {
			http.Error(w, err.Error(), 500)
			return
		}
		writeJSON(w, result)
	}
}

func handleStatus(cfg RouterConfig) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		songCount, _ := cfg.Database.GetSongCount()
		artists, _ := cfg.Database.GetAllArtists()
		albums, _ := cfg.Database.GetAllAlbums()

		status := map[string]interface{}{
			"songs":      songCount,
			"artists":    len(artists),
			"albums":     len(albums),
			"dht_peers":  cfg.DHTNode.RoutingTableSize(),
			"rate_limit": cfg.Limiter.Rate(),
			"device":     cfg.DHTNode.Contact().DeviceName,
		}
		writeJSON(w, status)
	}
}

func handleDHTDevices(cfg RouterConfig) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		devices := cfg.DHTNode.FindDevices()
		// Parse device JSON values
		var result []map[string]interface{}
		for _, d := range devices {
			var info map[string]interface{}
			if err := json.Unmarshal([]byte(d), &info); err == nil {
				result = append(result, info)
			}
		}
		if result == nil {
			result = []map[string]interface{}{}
		}
		writeJSON(w, result)
	}
}

func handleDHTContacts(cfg RouterConfig) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		contacts := cfg.DHTNode.AllContacts()
		if contacts == nil {
			contacts = []dht.Contact{}
		}
		writeJSON(w, contacts)
	}
}

func handleRateLimitConfig(cfg RouterConfig) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		switch r.Method {
		case http.MethodGet:
			writeJSON(w, map[string]int{"rate_limit": cfg.Limiter.Rate()})
		case http.MethodPut:
			var body struct {
				RateLimit int `json:"rate_limit"`
			}
			if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
				http.Error(w, "invalid body", 400)
				return
			}
			cfg.Limiter.SetRate(body.RateLimit)
			writeJSON(w, map[string]int{"rate_limit": cfg.Limiter.Rate()})
		default:
			http.Error(w, "method not allowed", 405)
		}
	}
}

func handleMusicDirConfig(cfg RouterConfig) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		switch r.Method {
		case http.MethodGet:
			writeJSON(w, map[string]string{"music_dir": cfg.Scanner.MusicDir()})
		case http.MethodPut:
			var body struct {
				MusicDir string `json:"music_dir"`
			}
			if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
				http.Error(w, "invalid body", 400)
				return
			}
			if body.MusicDir == "" {
				http.Error(w, "music_dir is required", 400)
				return
			}
			cfg.Scanner.SetMusicDir(body.MusicDir)
			writeJSON(w, map[string]string{"music_dir": body.MusicDir})
		default:
			http.Error(w, "method not allowed", 405)
		}
	}
}

// ── Helpers ──

func writeJSON(w http.ResponseWriter, data interface{}) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	json.NewEncoder(w).Encode(data)
}

func contentTypeForExt(ext string) string {
	switch ext {
	case ".mp3":
		return "audio/mpeg"
	case ".flac":
		return "audio/flac"
	case ".ogg", ".opus":
		return "audio/ogg"
	case ".wav":
		return "audio/wav"
	case ".m4a", ".aac":
		return "audio/mp4"
	case ".wma":
		return "audio/x-ms-wma"
	default:
		return "application/octet-stream"
	}
}
