# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Orpheus.csproj", "."]
RUN dotnet restore "./Orpheus.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./Orpheus.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Orpheus.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app

# Switch to root to install python3, ffmpeg, and latest yt-dlp via pip
USER root

# Create cache directory for persistent storage (as root)
RUN mkdir -p /data/cache && chown -R $APP_UID:$APP_UID /data

RUN apt-get update 
RUN apt-get install -y python3 python3-pip ffmpeg python3-venv libopus0
RUN cp /usr/lib/x86_64-linux-gnu/libopus.so.0.8.0 /usr/lib/x86_64-linux-gnu/libopus.so
RUN apt-get clean 
RUN rm -rf /var/lib/apt/lists/*

# Create a virtual environment for yt-dlp
RUN python3 -m venv /opt/yt-dlp-venv

# Install yt-dlp into the virtual environment using its specific pip
RUN /opt/yt-dlp-venv/bin/pip install -U "yt-dlp[default]"

# Add the virtual environment's bin directory to the PATH
# This makes yt-dlp accessible to the application run by the ENTRYPOINT
ENV PATH="/opt/yt-dlp-venv/bin:$PATH"

# Switch back to app user
USER $APP_UID

COPY --from=publish /app/publish .

# Define volume for persistent cache storage
VOLUME ["/data"]

ENTRYPOINT ["dotnet", "Orpheus.dll"]

# Document usage of environment variables and volumes
# When running the container, set the token and mount a volume for cache persistence:
# 
# Build:
# docker build --no-cache -t orpheus .
#
# Run with named volume (recommended):
# docker volume create orpheus-cache
# docker run -d --name orpheus -e DISCORD_TOKEN=your_token_here -v orpheus-cache:/data orpheus
#
# Run with bind mount:
# mkdir -p ./orpheus-data
# docker run -d --name orpheus -e DISCORD_TOKEN=your_token_here -v ./orpheus-data:/data orpheus
#
# See DOCKER_CACHE_PERSISTENCE.md for complete setup guide