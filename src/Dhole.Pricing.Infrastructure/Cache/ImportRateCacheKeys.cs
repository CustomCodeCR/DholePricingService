using System.Globalization;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Infrastructure.Cache;

internal static class ImportRateCacheKeys
{
    public const string QueryVersion = "pricing:imports:rates:queries:version";

    public static string ImportRateById(Guid importRateId)
    {
        return $"pricing:imports:rates:id:{GuidPart(importRateId)}";
    }

    public static string ImportRatesByBatch(string version, Guid importBatchId)
    {
        return $"pricing:imports:rates:v:{version}" + $":batch:{GuidPart(importBatchId)}";
    }

    public static string ImportRates(
        string version,
        Guid? importBatchId = null,
        ImportSourceType? sourceType = null,
        ImportStatus? status = null,
        string? pol = null,
        string? pod = null,
        string? carrier = null,
        string? containerType = null,
        string? currency = null,
        DateTime? quoteDate = null
    )
    {
        return $"pricing:imports:rates:query:v:{version}"
            + $":batch:{GuidPart(importBatchId)}"
            + $":source:{EnumPart(sourceType)}"
            + $":status:{EnumPart(status)}"
            + $":pol:{TextPart(pol)}"
            + $":pod:{TextPart(pod)}"
            + $":carrier:{TextPart(carrier)}"
            + $":container:{TextPart(containerType)}"
            + $":currency:{TextPart(currency)}"
            + $":quote-date:{DatePart(quoteDate)}";
    }

    private static string GuidPart(Guid value)
    {
        return value.ToString("N");
    }

    private static string GuidPart(Guid? value)
    {
        return value.HasValue ? GuidPart(value.Value) : "all";
    }

    private static string EnumPart<TEnum>(TEnum? value)
        where TEnum : struct, Enum
    {
        return value.HasValue
            ? Convert
                .ToInt64(value.Value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture)
            : "all";
    }

    private static string TextPart(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "all"
            : Uri.EscapeDataString(value.Trim().ToLowerInvariant());
    }

    private static string DatePart(DateTime? value)
    {
        return value?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "all";
    }
}
