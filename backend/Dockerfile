# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore against just the project file first so layer caching survives source-only changes.
COPY src/Phonix.Api/Phonix.Api.csproj src/Phonix.Api/
RUN dotnet restore src/Phonix.Api/Phonix.Api.csproj

COPY src/ src/
RUN dotnet publish src/Phonix.Api/Phonix.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
# curl is used by the compose healthcheck to probe /health.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .

# State (store.json) + rolling logs live here; mount a volume to persist across restarts/rebuilds.
# Owned by the built-in non-root "app" user (UID 1654, ships in the .NET 8 aspnet image) so the app can
# write store.json, logs, and the Data Protection key ring while the container runs unprivileged. A named
# volume mounted here inherits this ownership, so persistence keeps working.
RUN mkdir -p /app/App_Data && chown -R app:app /app
ENV ASPNETCORE_URLS=http://+:5228 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# Drop root: run the API as the non-root "app" user to avoid a container-escape blast radius.
USER app

EXPOSE 5228
ENTRYPOINT ["dotnet", "Phonix.Api.dll"]
