# syntax=docker/dockerfile:1

# Stage 1: restore and publish with the SDK image (not shipped in the final image).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY IotBackend/IotBackend.csproj IotBackend/
RUN dotnet restore IotBackend/IotBackend.csproj

COPY IotBackend/ IotBackend/
RUN dotnet publish IotBackend/IotBackend.csproj -c Release -o /app/publish --no-restore

# Stage 2: ASP.NET runtime only.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is used by the healthcheck in docker-compose.yml (the base image does not ship it).
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Kestrel serves plain HTTP on 8080; terminate TLS in a reverse proxy if you expose it.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "IotBackend.dll"]
