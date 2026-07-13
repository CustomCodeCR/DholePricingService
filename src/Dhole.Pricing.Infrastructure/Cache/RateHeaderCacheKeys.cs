using System.Globalization;

namespace Dhole.Pricing.Infrastructure.Cache;

internal static class RateHeaderCacheKeys
{
    public const string QueryVersion = "pricing:rates:headers:queries:version";

    public static string RateHeaderById(Guid rateHeaderId)
    {
        return $"pricing:rates:headers:id:{GuidPart(rateHeaderId)}";
    }

    public static string RateHeadersSelect()
    {
        return "pricing:rates:headers:select";
    }

    public static string ValidRateHeaders(
        string version,
        Guid? agentId = null,
        Guid? carrierId = null,
        Guid? polId = null,
        Guid? poeId = null,
        Guid? podId = null,
        Guid? containerTypeId = null,
        Guid? currencyId = null,
        DateTime? quoteDate = null
    )
    {
        return $"pricing:rates:headers:valid:v:{version}"
            + $":agent:{GuidPart(agentId)}"
            + $":carrier:{GuidPart(carrierId)}"
            + $":pol:{GuidPart(polId)}"
            + $":poe:{GuidPart(poeId)}"
            + $":pod:{GuidPart(podId)}"
            + $":container:{GuidPart(containerTypeId)}"
            + $":currency:{GuidPart(currencyId)}"
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

    private static string DatePart(DateTime? value)
    {
        return value?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "all";
    }
}
