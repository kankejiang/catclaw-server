# 多阶段构建：.NET 8 SDK 编译 → ASP.NET Core 8 运行时
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY CatClawMusicServer.csproj ./
RUN dotnet restore CatClawMusicServer.csproj
COPY . .
RUN dotnet publish CatClawMusicServer.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# NAS 部署默认路径（可用 docker-compose 环境变量覆盖）
ENV MusicServer__MusicDirectory=/music
ENV MusicServer__DbPath=/data/catclaw.db
ENV MusicServer__CoverOutputDir=/data/covers
ENV MusicServer__AccessToken=
ENV MusicServer__AdminUser=admin
ENV MusicServer__AdminPassword=
ENV ASPNETCORE_URLS=http://0.0.0.0:37823

# 音量卷：音乐库 + 数据（DB/封面/管理员凭据）持久化
VOLUME ["/music", "/data"]

# HTTP + STUN（UDP 打洞）
EXPOSE 37823
EXPOSE 37824/udp

ENTRYPOINT ["dotnet", "CatClawMusicServer.dll"]
