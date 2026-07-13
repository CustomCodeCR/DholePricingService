# Base image
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
RUN apt-get update && \
  apt-get install -y tzdata apt-utils libgdiplus libc6-dev && \
  ln -s /usr/lib/libgdiplus.so /usr/lib/gdiplus.dll && \
  rm -rf /var/lib/apt/lists/*

WORKDIR /app
ENV TZ=America/Costa_Rica

# Build image
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["src/Dhole.Pricing.Workers/Dhole.Pricing.Workers.csproj", "src/Dhole.Pricing.Workers/"]
COPY ["src/Dhole.Pricing.Api/Dhole.Pricing.Api.csproj", "src/Dhole.Pricing.Api/"]
COPY ["src/Dhole.Pricing.Application/Dhole.Pricing.Application.csproj", "src/Dhole.Pricing.Application/"]
COPY ["src/Dhole.Pricing.Domain/Dhole.Pricing.Domain.csproj", "src/Dhole.Pricing.Domain/"]
COPY ["src/Dhole.Pricing.Persistence/Dhole.Pricing.Persistence.csproj", "src/Dhole.Pricing.Persistence/"]
COPY ["src/Dhole.Pricing.Infrastructure/Dhole.Pricing.Infrastructure.csproj", "src/Dhole.Pricing.Infrastructure/"]
COPY ["src/Dhole.Pricing.Contracts/Dhole.Pricing.Contracts.csproj", "src/Dhole.Pricing.Contracts/"]

RUN dotnet restore "src/Dhole.Pricing.Workers/Dhole.Pricing.Workers.csproj"

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
  /p:UseAppHost=false

# Final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN chmod 755 /app

ENTRYPOINT ["dotnet", "Dhole.Pricing.Workers.dll"]
