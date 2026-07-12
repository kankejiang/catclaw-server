# 多阶段构建：.NET 8 SDK 编译 → ASP.NET Core 8 运行时 + FFmpeg
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY CatClawMusicServer.csproj ./
RUN dotnet restore CatClawMusicServer.csproj
COPY . .
RUN dotnet publish CatClawMusicServer.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# 安装 FFmpeg（HLS 转码需要）
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# ── 服务器配置 ──
ENV MusicServer__MusicDirectory=/music
ENV MusicServer__DbPath=/data/catclaw.db
ENV MusicServer__CoverOutputDir=/data/covers
ENV MusicServer__AccessToken=
ENV MusicServer__AdminUser=admin
ENV MusicServer__AdminPassword=
ENV ASPNETCORE_URLS=http://0.0.0.0:37823

# ── 流媒体配置 ──
ENV Streaming__HlsEnabled=true
ENV Streaming__FFmpegPath=/usr/bin/ffmpeg
ENV Streaming__TranscodeCacheSizeGB=2
ENV Streaming__TranscodeDir=/data/transcode

# ── ClawCircle P2P ──
ENV ClawCircle__DhtEnabled=true
ENV ClawCircle__DhtPort=37825

# 音量卷：音乐库 + 数据持久化
VOLUME ["/music", "/data"]

# HTTP + STUN（NAT 打洞）+ DHT（节点发现）
EXPOSE 37823
EXPOSE 37824/udp
EXPOSE 37825/udp

ENTRYPOINT ["dotnet", "CatClawMusicServer.dll"]
