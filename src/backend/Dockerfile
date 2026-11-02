# Build stage: restore against the project files only, so a source-only change does not
# invalidate the NuGet layer.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY global.json Directory.Build.props ./
COPY EncodeLabs.Domain/EncodeLabs.Domain.csproj EncodeLabs.Domain/
COPY EncodeLabs.Application/EncodeLabs.Application.csproj EncodeLabs.Application/
COPY EncodeLabs.Infrastructure/EncodeLabs.Infrastructure.csproj EncodeLabs.Infrastructure/
COPY EncodeLabs.Api/EncodeLabs.Api.csproj EncodeLabs.Api/
RUN dotnet restore EncodeLabs.Api/EncodeLabs.Api.csproj

COPY . .
RUN dotnet publish EncodeLabs.Api/EncodeLabs.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is only here so Compose can run an HTTP health check against /health.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Provided by the base image; the API has no reason to run as root.
USER $APP_UID

ENTRYPOINT ["dotnet", "EncodeLabs.Api.dll"]
