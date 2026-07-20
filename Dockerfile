# 多阶段构建：.NET 8 SDK 编译 → ASP.NET Core 8 运行时 + FFmpeg
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY CatClawMusicServer.csproj ./
RUN dotnet restore CatClawMusicServer.csproj
COPY . .
RUN dotnet publish CatClawMusicServer.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# 安装 FFmpeg（使用阿里云镜像加速）
RUN sed -i 's|deb.debian.org|mirrors.aliyun.com|g' /etc/apt/sources.list.d/debian.sources 2>/dev/null; \
    sed -i 's|deb.debian.org|mirrors.aliyun.com|g' /etc/apt/sources.list 2>/dev/null; \
    apt-get update && apt-get install -y --no-install-recommends ffmpeg \
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

# 音量卷：音乐库 + 数据持久化
VOLUME ["/music", "/data"]

# HTTP
EXPOSE 37823

ENTRYPOINT ["dotnet", "CatClawMusicServer.dll"]
