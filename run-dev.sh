#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

# Evita que API y Workers intenten generar los mismos artefactos al mismo tiempo.
find src -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
dotnet restore "DholePricingService.slnx"
dotnet build "DholePricingService.slnx" --no-restore -m:1

dotnet run --project "src/Dhole.Pricing.Api/Dhole.Pricing.Api.csproj" --no-build > "/tmp/Dhole.Pricing.Api.log" 2>&1 &
echo "Iniciado Dhole.Pricing.Api. Log: /tmp/Dhole.Pricing.Api.log"
dotnet run --project "src/Dhole.Pricing.Workers/Dhole.Pricing.Workers.csproj" --no-build > "/tmp/Dhole.Pricing.Workers.log" 2>&1 &
echo "Iniciado Dhole.Pricing.Workers. Log: /tmp/Dhole.Pricing.Workers.log"

wait
