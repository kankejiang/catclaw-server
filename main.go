package main

import (
	"context"
	"embed"
	"flag"
	"fmt"
	"io/fs"
	"log"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/kankejiang/catclaw-server/internal/api"
	"github.com/kankejiang/catclaw-server/internal/db"
	"github.com/kankejiang/catclaw-server/internal/dht"
	"github.com/kankejiang/catclaw-server/internal/limiter"
	"github.com/kankejiang/catclaw-server/internal/scanner"
)

//go:embed web/*
var webFS embed.FS

func main() {
	var (
		httpPort       = flag.Int("http-port", 66880, "HTTP API port")
		dhtPort        = flag.Int("dht-port", 66881, "DHT UDP port")
		musicDir       = flag.String("music-dir", "/music", "Music directory to scan")
		dbPath         = flag.String("db-path", "/data/catclaw.db", "SQLite database path")
		bootstrapNodes = flag.String("bootstrap", "music.08102516.xyz:6881", "Comma-separated bootstrap nodes")
		rateLimitKB    = flag.Int("rate-limit", 128, "Rate limit in KB/s (0 = unlimited)")
		deviceName     = flag.String("device-name", "", "Device display name (defaults to hostname)")
	)
	flag.Parse()

	if *deviceName == "" {
		hostname, _ := os.Hostname()
		*deviceName = hostname
	}

	log.Printf("🐾 CatClaw Server starting...")
	log.Printf("   HTTP API:  :%d", *httpPort)
	log.Printf("   DHT Node:  :%d/udp", *dhtPort)
	log.Printf("   Music Dir: %s", *musicDir)
	log.Printf("   Database:  %s", *dbPath)
	log.Printf("   Bootstrap: %s", *bootstrapNodes)
	log.Printf("   RateLimit: %d KB/s", *rateLimitKB)
	log.Printf("   Device:    %s", *deviceName)

	// Initialize database
	database, err := db.New(*dbPath)
	if err != nil {
		log.Fatalf("Failed to open database: %v", err)
	}
	defer database.Close()

	// Initialize rate limiter
	var rl limiter.Limiter
	if *rateLimitKB > 0 {
		rl = limiter.NewTokenBucket(*rateLimitKB * 1024)
	} else {
		rl = limiter.NewUnlimited()
	}

	// Initialize DHT node
	dhtNode, err := dht.NewNode(dht.Config{
		Port:           *dhtPort,
		BootstrapNodes: parseBootstrapNodes(*bootstrapNodes),
		DeviceName:     *deviceName,
	})
	if err != nil {
		log.Fatalf("Failed to create DHT node: %v", err)
	}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	go func() {
		if err := dhtNode.Start(ctx); err != nil {
			log.Printf("DHT node error: %v", err)
		}
	}()

	// Initialize music scanner
	musicScanner := scanner.New(*musicDir, database)

	// Build HTTP router
	webSubFS, _ := fs.Sub(webFS, "web")
	handler := api.NewRouter(api.RouterConfig{
		Database:  database,
		Limiter:   rl,
		DHTNode:   dhtNode,
		Scanner:   musicScanner,
		MusicDir:  *musicDir,
		WebFS:     webSubFS.(fs.FS),
	})

	httpServer := &http.Server{
		Addr:         fmt.Sprintf(":%d", *httpPort),
		Handler:      handler,
		ReadTimeout:  30 * time.Second,
		WriteTimeout: 10 * time.Minute, // Long timeout for streaming
		IdleTimeout:  60 * time.Second,
	}

	// Start HTTP server
	go func() {
		log.Printf("HTTP server listening on :%d", *httpPort)
		if err := httpServer.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Fatalf("HTTP server error: %v", err)
		}
	}()

	// Wait for shutdown signal
	sigCh := make(chan os.Signal, 1)
	signal.Notify(sigCh, syscall.SIGINT, syscall.SIGTERM)
	<-sigCh

	log.Println("Shutting down...")

	shutdownCtx, shutdownCancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer shutdownCancel()

	httpServer.Shutdown(shutdownCtx)
	dhtNode.Stop()
	cancel()

	log.Println("Server stopped.")
}

func parseBootstrapNodes(s string) []string {
	if s == "" {
		return nil
	}
	// Simple split by comma
	var nodes []string
	for _, node := range split(s, ',') {
		trimmed := trim(node)
		if trimmed != "" {
			nodes = append(nodes, trimmed)
		}
	}
	return nodes
}

func split(s string, sep byte) []string {
	var parts []string
	start := 0
	for i := 0; i < len(s); i++ {
		if s[i] == sep {
			parts = append(parts, s[start:i])
			start = i + 1
		}
	}
	parts = append(parts, s[start:])
	return parts
}

func trim(s string) string {
	for len(s) > 0 && s[0] == ' ' {
		s = s[1:]
	}
	for len(s) > 0 && s[len(s)-1] == ' ' {
		s = s[:len(s)-1]
	}
	return s
}
