using System.Text.Json;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Features.Rates;

internal sealed record RateCargoProfile(
    int TotalPackages,
    int TotalPallets,
    decimal TotalWeightKg,
    decimal TotalVolumeCbm,
    decimal KgPerCbm,
    string? CargoLinesJson
);

internal static class RateCargoProfileFactory
{
    internal const decimal LandConsolidatedMaxHeightCm = 270m;
    internal const decimal MaritimeConsolidatedMaxHeightCm = 269m;

    public static RateCargoProfile Create(
        ShipmentMode shipmentMode,
        decimal kgPerCbm,
        IReadOnlyCollection<RateCargoLineCommandItem> lines,
        int fallbackPackages,
        int fallbackPallets,
        decimal fallbackWeightKg,
        decimal fallbackVolumeCbm,
        string? equipmentCode = null
    )
    {
        var effectiveFactor = kgPerCbm > 0m
            ? kgPerCbm
            : shipmentMode == ShipmentMode.Ltl ? 333m : 500m;

        if (lines.Count == 0)
        {
            return new RateCargoProfile(
                Math.Max(fallbackPackages, 0),
                Math.Max(fallbackPallets, 0),
                Math.Max(fallbackWeightKg, 0m),
                Math.Max(fallbackVolumeCbm, 0m),
                effectiveFactor,
                null
            );
        }

        var maximumHeightCm = ResolveMaximumHeightCm(shipmentMode, equipmentCode);

        var snapshots = new List<RateCargoLineDto>(lines.Count);
        var packages = 0;
        var pallets = 0;
        var weight = 0m;
        var volume = 0m;

        foreach (var line in lines)
        {
            if (
                line.Packages < 0
                || line.Pallets < 0
                || line.WeightKg < 0m
                || line.LengthCm < 0m
                || line.WidthCm < 0m
                || line.HeightCm < 0m
            )
            {
                throw new InvalidOperationException("Los valores de las líneas de carga no pueden ser negativos.");
            }

            if (maximumHeightCm.HasValue && line.HeightCm > maximumHeightCm.Value)
            {
                var context = shipmentMode == ShipmentMode.Ltl
                    ? "consolidado terrestre"
                    : string.IsNullOrWhiteSpace(equipmentCode)
                        ? "consolidado marítimo"
                        : $"contenedor {equipmentCode.Trim()}";
                throw new InvalidOperationException(
                    $"La altura de la carga no puede superar {maximumHeightCm.Value:0.##} cm para {context}."
                );
            }

            var lineVolume = line.LengthCm * line.WidthCm * line.HeightCm / 1_000_000m;
            lineVolume *= Math.Max(line.Packages, 1);

            packages += line.Packages;
            pallets += line.Pallets;
            weight += line.WeightKg;
            volume += lineVolume;

            snapshots.Add(
                new RateCargoLineDto(
                    string.IsNullOrWhiteSpace(line.Description) ? null : line.Description.Trim(),
                    line.Packages,
                    line.Pallets,
                    line.WeightKg,
                    line.LengthCm,
                    line.WidthCm,
                    line.HeightCm,
                    Math.Round(lineVolume, 6, MidpointRounding.AwayFromZero)
                )
            );
        }

        return new RateCargoProfile(
            packages,
            pallets,
            Math.Round(weight, 4, MidpointRounding.AwayFromZero),
            Math.Round(volume, 6, MidpointRounding.AwayFromZero),
            effectiveFactor,
            JsonSerializer.Serialize(snapshots)
        );
    }

    private static decimal? ResolveMaximumHeightCm(ShipmentMode shipmentMode, string? equipmentCode)
    {
        if (shipmentMode == ShipmentMode.Ltl)
            return LandConsolidatedMaxHeightCm;

        if (shipmentMode != ShipmentMode.Lcl)
            return null;

        var normalizedEquipment = (equipmentCode ?? string.Empty)
            .Trim()
            .ToUpperInvariant()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (
            normalizedEquipment.Contains("AIR", StringComparison.Ordinal)
            || normalizedEquipment.Contains("ULD", StringComparison.Ordinal)
            || normalizedEquipment.Contains("PALLET", StringComparison.Ordinal)
            || normalizedEquipment.Contains("LOOSE", StringComparison.Ordinal)
        )
        {
            return null;
        }

        return MaritimeConsolidatedMaxHeightCm;
    }

    public static IReadOnlyCollection<RateCargoLineDto> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<RateCargoLineDto>();

        try
        {
            var lines = JsonSerializer.Deserialize<List<RateCargoLineDto>>(json);
            return lines is null ? Array.Empty<RateCargoLineDto>() : lines;
        }
        catch (JsonException)
        {
            return Array.Empty<RateCargoLineDto>();
        }
    }
}
