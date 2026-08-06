using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Abstractions.Reports;

public interface IRateReportDataFactory
{
    string CreateDataJson(RateHeader rate);
}
