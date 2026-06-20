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
COPY --from=build /app/publish .

# State (store.json) + rolling logs live here; mount a volume to persist across restarts/rebuilds.
RUN mkdir -p /app/App_Data
ENV ASPNETCORE_URLS=http://+:5228 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 5228
ENTRYPOINT ["dotnet", "Phonix.Api.dll"]
