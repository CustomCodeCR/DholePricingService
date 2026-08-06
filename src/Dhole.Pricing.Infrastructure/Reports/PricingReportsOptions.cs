namespace Dhole.Pricing.Infrastructure.Reports;

public sealed class PricingReportsOptions
{
    public string BaseAddress { get; set; } = "http://localhost:5208";
    public string InternalServiceKeyHeader { get; set; } = "X-Dhole-Service-Key";
    public string InternalServiceKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 120;
}
