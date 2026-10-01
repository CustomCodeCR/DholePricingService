using System.Text.Json;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Features.Rates;

internal sealed record RateCargoProfile(
    int TotalPackages,
    int TotalPallets,
    decimal TotalWeightKg,
    decimal TotalVolumeCbm,
    decimal ChargeableVolumeCbm,
    decimal KgPerCbm,
    string? CargoLinesJson
);

internal static class RateCargoProfileFactory
{
    internal const decimal ForcedNonStackableHeightCm = 177.8m;
    internal const decimal NonStackableBillableHeightCm = 266m;

    public static RateCargoProfile Create(
        ShipmentMode shipmentMode,
        decimal kgPerCbm,
        IReadOnlyCollection<RateCargoLineCommandItem> lines,
        int fallbackPackages,
        int fallbackPallets,
        decimal fallbackWeightKg,
        decimal fallbackVolumeCbm
    )
    {
        var effectiveFactor = kgPerCbm > 0m
            ? kgPerCbm
            : shipmentMode == ShipmentMode.Ltl ? 330m : 500m;

        if (lines.Count == 0)
        {
            var fallbackVolume = Math.Max(fallbackVolumeCbm, 0m);
            return new RateCargoProfile(
                Math.Max(fallbackPackages, 0),
                Math.Max(fallbackPallets, 0),
                Math.Max(fallbackWeightKg, 0m),
                fallbackVolume,
                fallbackVolume,
                effectiveFactor,
                null
            );
        }

        var snapshots = new List<RateCargoLineDto>(lines.Count);
        var packages = 0;
        var pallets = 0;
        var weight = 0m;
        var physicalVolume = 0m;
        var chargeableVolume = 0m;

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

            var units = Math.Max(line.Packages, 1);
            var forcedNonStackable = line.HeightCm >= ForcedNonStackableHeightCm;
            var isStackable = !forcedNonStackable && line.IsStackable;
            var billableHeightCm = isStackable
                ? line.HeightCm
                : Math.Max(NonStackableBillableHeightCm, line.HeightCm);

            var linePhysicalVolume =
                line.LengthCm * line.WidthCm * line.HeightCm * units / 1_000_000m;
            var lineBillableVolume =
                line.LengthCm * line.WidthCm * billableHeightCm * units / 1_000_000m;
            var deadSpaceCbm = Math.Max(0m, lineBillableVolume - linePhysicalVolume);

            packages += line.Packages;
            pallets += line.Pallets;
            weight += line.WeightKg;
            physicalVolume += linePhysicalVolume;
            chargeableVolume += lineBillableVolume;

            snapshots.Add(
                new RateCargoLineDto(
                    string.IsNullOrWhiteSpace(line.Description) ? null : line.Description.Trim(),
                    line.Packages,
                    line.Pallets,
                    line.WeightKg,
                    line.LengthCm,
                    line.WidthCm,
                    line.HeightCm,
                    Math.Round(linePhysicalVolume, 6, MidpointRounding.AwayFromZero),
                    isStackable,
                    Math.Round(lineBillableVolume, 6, MidpointRounding.AwayFromZero),
                    Math.Round(deadSpaceCbm, 6, MidpointRounding.AwayFromZero)
                )
            );
        }

        return new RateCargoProfile(
            packages,
            pallets,
            Math.Round(weight, 4, MidpointRounding.AwayFromZero),
            Math.Round(physicalVolume, 6, MidpointRounding.AwayFromZero),
            Math.Round(chargeableVolume, 6, MidpointRounding.AwayFromZero),
            effectiveFactor,
            JsonSerializer.Serialize(snapshots)
        );
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
