namespace Dhole.Pricing.Domain.Imports.Enums;

public enum PricingImportFromExtractionJobStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    RetryScheduled = 4,
    Failed = 5,
}
