using System.Globalization;
using Dhole.Pricing.Domain.Costs.Enums;

namespace Dhole.Pricing.Infrastructure.Cache;

internal static class CostCacheKeys
{
    public const string QueryVersion = "pricing:costs:queries:version";

    public static string CostById(Guid costId)
    {
        return $"pricing:costs:id:{GuidPart(costId)}";
    }

    public static string CostsSelect()
    {
        return "pricing:costs:select";
    }

    public static string ActiveCosts(
        string version,
        CostType? costType = null,
        CostDetailType? costDetailType = null,
        Guid? carrierId = null,
        Guid? agentId = null,
        Guid? portId = null,
        CostPortRole? portRole = null,
        Guid? currencyId = null
    )
    {
        return $"pricing:costs:active:v:{version}"
            + $":type:{EnumPart(costType)}"
            + $":detail:{EnumPart(costDetailType)}"
            + $":carrier:{GuidPart(carrierId)}"
            + $":agent:{GuidPart(agentId)}"
            + $":port:{GuidPart(portId)}"
            + $":role:{EnumPart(portRole)}"
            + $":currency:{GuidPart(currencyId)}";
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
}
