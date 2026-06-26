FROM golang:1.23-alpine AS builder

WORKDIR /build

# Install build dependencies
RUN apk add --no-cache gcc musl-dev sqlite-dev

# Copy go module files first for caching
COPY go.mod go.sum ./
RUN go mod download

# Copy source code
COPY . .

# Build static binary
RUN CGO_ENABLED=1 GOOS=linux go build -ldflags="-s -w" -o /catclaw-server .

# Runtime stage
FROM alpine:3.21

RUN apk add --no-cache ca-certificates sqlite-libs tzdata

COPY --from=builder /catclaw-server /catclaw-server

# Create volume directories
RUN mkdir -p /music /data

EXPOSE 66880
EXPOSE 66881/udp

ENV MUSIC_DIR=/music
ENV DB_PATH=/data/catclaw.db
ENV BOOTSTRAP_NODES=music.08102516.xyz:6881
ENV RATE_LIMIT=128
ENV DEVICE_NAME=""

VOLUME ["/music", "/data"]

ENTRYPOINT ["/catclaw-server"]
CMD ["--music-dir", "/music", "--db-path", "/data/catclaw.db"]
