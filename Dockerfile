# syntax=docker/dockerfile:1
# CampusTransit - single-container build of the Blazor Web App.
# Runs unchanged on Render, Koyeb, Fly.io, Google Cloud Run and Northflank.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the NuGet layer is cached between builds.
COPY CampusTransit.csproj ./
RUN dotnet restore CampusTransit.csproj

COPY . ./
RUN dotnet publish CampusTransit.csproj -c Release -o /app --no-restore
RUN test -f /app/wwwroot/_framework/blazor.web.js

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
RUN test -f /app/wwwroot/_framework/blazor.web.js

# Run unprivileged. /app must stay writable because the SQLite file is created there.
RUN chown -R 1654:1654 /app
USER 1654

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_NOLOGO=1

# The SQLite file is created next to the app on first run and seeded automatically.
EXPOSE 8080

# Render, Koyeb and Cloud Run inject PORT at runtime; 8080 is the local default.
CMD ASPNETCORE_URLS="http://+:${PORT:-8080}" dotnet CampusTransit.dll
