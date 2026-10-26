# syntax=docker/dockerfile:1.7

# ---------------------------------------------------------------------------------------------
# Build stage. The SDK image is large and never ships; only its output does.
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, against the project files and their lock files alone. Docker caches this layer,
# so a change to a .cs file rebuilds the code without re-downloading every package. Locked mode
# fails the build if a lock file is stale, so the image contains exactly the reviewed package graph.
COPY Directory.Build.props Directory.Packages.props NuGet.config global.json ./
COPY src/CarWash.Hub.Domain/CarWash.Hub.Domain.csproj src/CarWash.Hub.Domain/packages.lock.json src/CarWash.Hub.Domain/
COPY src/CarWash.Hub.Application/CarWash.Hub.Application.csproj src/CarWash.Hub.Application/packages.lock.json src/CarWash.Hub.Application/
COPY src/CarWash.Hub.Infrastructure/CarWash.Hub.Infrastructure.csproj src/CarWash.Hub.Infrastructure/packages.lock.json src/CarWash.Hub.Infrastructure/
COPY src/CarWash.Hub.Persistence/CarWash.Hub.Persistence.csproj src/CarWash.Hub.Persistence/packages.lock.json src/CarWash.Hub.Persistence/
COPY src/CarWash.Hub.Api/CarWash.Hub.Api.csproj src/CarWash.Hub.Api/packages.lock.json src/CarWash.Hub.Api/
RUN dotnet restore src/CarWash.Hub.Api/CarWash.Hub.Api.csproj --locked-mode

COPY src/ src/
RUN dotnet publish src/CarWash.Hub.Api/CarWash.Hub.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

# ---------------------------------------------------------------------------------------------
# Runtime stage.
#
# glibc-based (the default tag), deliberately NOT Alpine. Alpine ships without ICU, and
# Directory.Build.props sets InvariantGlobalization=false because the ar-SA culture needs it. On
# Alpine every Arabic response would silently come back in English.
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Rolling logs live in a volume; the folder must exist and belong to the non-root user first.
RUN mkdir -p /app/logs && chown app:app /app/logs

# Non-root: the "app" user the base image provides.
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1

EXPOSE 8080

# Liveness only - not /health/ready, so a database outage does not make the orchestrator restart a
# healthy process in a loop. The runtime image has no curl, so the probe speaks HTTP through bash.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["/bin/bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q ' 200 '"]

COPY --from=build --chown=app:app /app/publish .

ENTRYPOINT ["dotnet", "CarWash.Hub.Api.dll"]
