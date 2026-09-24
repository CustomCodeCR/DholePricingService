# syntax=docker/dockerfile:1

# Base image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# Ubuntu mirrors can briefly publish an index that references a package version
# already rotated from security. Refresh package lists and retry the complete
# update/install sequence so a transient 404 does not break the deployment.
RUN set -eux; \
  attempt=1; \
  while true; do \
    rm -rf /var/lib/apt/lists/*; \
    if apt-get -o Acquire::Retries=5 update && \
       DEBIAN_FRONTEND=noninteractive apt-get -o Acquire::Retries=5 install -y --no-install-recommends tzdata libgdiplus; then \
      break; \
    fi; \
    if [ "$attempt" -ge 4 ]; then \
      echo "APT dependency installation failed after ${attempt} attempts." >&2; \
      exit 1; \
    fi; \
    delay=$((attempt * 5)); \
    echo "APT mirror was not ready; retrying in ${delay}s..." >&2; \
    sleep "$delay"; \
    attempt=$((attempt + 1)); \
  done; \
  ln -sf /usr/lib/libgdiplus.so /usr/lib/gdiplus.dll; \
  apt-get clean; \
  rm -rf /var/lib/apt/lists/*

WORKDIR /app
ENV TZ=America/Costa_Rica

# Build image
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG GITHUB_ACTOR=github-actions
WORKDIR /src

COPY ["NuGet.Config", "./NuGet.Config"]
COPY ["src/Dhole.Pricing.Workers/Dhole.Pricing.Workers.csproj", "src/Dhole.Pricing.Workers/"]
COPY ["src/Dhole.Pricing.Api/Dhole.Pricing.Api.csproj", "src/Dhole.Pricing.Api/"]
COPY ["src/Dhole.Pricing.Application/Dhole.Pricing.Application.csproj", "src/Dhole.Pricing.Application/"]
COPY ["src/Dhole.Pricing.Domain/Dhole.Pricing.Domain.csproj", "src/Dhole.Pricing.Domain/"]
COPY ["src/Dhole.Pricing.Persistence/Dhole.Pricing.Persistence.csproj", "src/Dhole.Pricing.Persistence/"]
COPY ["src/Dhole.Pricing.Infrastructure/Dhole.Pricing.Infrastructure.csproj", "src/Dhole.Pricing.Infrastructure/"]
COPY ["src/Dhole.Pricing.Contracts/Dhole.Pricing.Contracts.csproj", "src/Dhole.Pricing.Contracts/"]

# NuGet/GitHub Packages can occasionally fail on the self-hosted runner with a transient
# TLS handshake or package-download timeout. Retry the restore and disable parallel HTTP
# restores so one intermittent connection does not abort the complete Pricing deployment.
RUN --mount=type=secret,id=github_token,required=true \
  set -eu; \
  export NuGetPackageSourceCredentials_github="Username=${GITHUB_ACTOR};Password=$(cat /run/secrets/github_token);ValidAuthenticationTypes=Basic"; \
  attempt=1; \
  while true; do \
    if dotnet restore "src/Dhole.Pricing.Workers/Dhole.Pricing.Workers.csproj" --configfile /src/NuGet.Config --disable-parallel; then \
      break; \
    fi; \
    if [ "$attempt" -ge 4 ]; then \
      echo "NuGet restore failed after ${attempt} attempts." >&2; \
      exit 1; \
    fi; \
    delay=$((attempt * 10)); \
    echo "NuGet restore attempt ${attempt} failed; retrying in ${delay}s..." >&2; \
    sleep "$delay"; \
    attempt=$((attempt + 1)); \
  done

COPY . .

WORKDIR "/src/src/Dhole.Pricing.Workers"
RUN dotnet build "Dhole.Pricing.Workers.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

# Publish image
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src/src/Dhole.Pricing.Workers

RUN dotnet publish "Dhole.Pricing.Workers.csproj" \
  -c $BUILD_CONFIGURATION \
  -o /app/publish \
  --no-restore \
  /p:UseAppHost=false

# Final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN chmod 755 /app

ENTRYPOINT ["dotnet", "Dhole.Pricing.Workers.dll"]
