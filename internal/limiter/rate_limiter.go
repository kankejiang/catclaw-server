package limiter

import (
	"io"
	"sync"
	"time"
)

// Limiter is the interface for rate limiting readers.
type Limiter interface {
	// Wrap wraps a reader with rate limiting. Returns the original reader if no limit.
	Wrap(r io.Reader) io.Reader
	// SetRate updates the rate limit in bytes per second.
	SetRate(bytesPerSec int)
	// Rate returns the current rate limit in bytes per second.
	Rate() int
}

// TokenBucket implements a token bucket rate limiter.
type TokenBucket struct {
	rate       int       // bytes per second
	tokens     float64   // current tokens (in bytes)
	lastRefill time.Time
	mu         sync.Mutex
}

// NewTokenBucket creates a token bucket limiter.
func NewTokenBucket(bytesPerSec int) *TokenBucket {
	return &TokenBucket{
		rate:       bytesPerSec,
		tokens:     float64(bytesPerSec), // Start full
		lastRefill: time.Now(),
	}
}

func (tb *TokenBucket) refill() {
	now := time.Now()
	elapsed := now.Sub(tb.lastRefill).Seconds()
	tb.tokens += elapsed * float64(tb.rate)
	if tb.tokens > float64(tb.rate) {
		tb.tokens = float64(tb.rate)
	}
	tb.lastRefill = now
}

func (tb *TokenBucket) Wrap(r io.Reader) io.Reader {
	if tb.rate <= 0 {
		return r
	}
	return &rateLimitedReader{
		reader:  r,
		bucket:  tb,
		bufSize: 32 * 1024, // 32KB chunks
	}
}

func (tb *TokenBucket) SetRate(bytesPerSec int) {
	tb.mu.Lock()
	defer tb.mu.Unlock()
	tb.rate = bytesPerSec
	tb.tokens = float64(bytesPerSec)
}

func (tb *TokenBucket) Rate() int {
	tb.mu.Lock()
	defer tb.mu.Unlock()
	return tb.rate
}

type rateLimitedReader struct {
	reader  io.Reader
	bucket  *TokenBucket
	bufSize int
}

func (r *rateLimitedReader) Read(p []byte) (int, error) {
	r.bucket.mu.Lock()
	defer r.bucket.mu.Unlock()

	// Determine how many bytes we can read
	maxBytes := len(p)
	if maxBytes > r.bufSize {
		maxBytes = r.bufSize
	}

	r.bucket.refill()

	// Limit by available tokens
	allowed := int(r.bucket.tokens)
	if allowed <= 0 {
		// Sleep until we have at least 1 token
		sleepTime := time.Duration(float64(time.Second) * float64(-allowed+r.bucket.rate) / float64(r.bucket.rate))
		if sleepTime > 0 && sleepTime < 5*time.Second {
			r.bucket.mu.Unlock()
			time.Sleep(sleepTime)
			r.bucket.mu.Lock()
			r.bucket.refill()
			allowed = int(r.bucket.tokens)
		}
	}
	if allowed > maxBytes {
		allowed = maxBytes
	}
	if allowed < 1 {
		allowed = 1
	}

	readBuf := p[:allowed]
	r.bucket.mu.Unlock()
	n, err := r.reader.Read(readBuf)
	r.bucket.mu.Lock()

	if n > 0 {
		r.bucket.tokens -= float64(n)
	}
	return n, err
}

// Unlimited is a no-op limiter.
type Unlimited struct{}

func NewUnlimited() *Unlimited {
	return &Unlimited{}
}

func (u *Unlimited) Wrap(r io.Reader) io.Reader { return r }
func (u *Unlimited) SetRate(int)                {}
func (u *Unlimited) Rate() int                  { return 0 }
