namespace Dhole.Pricing.Contracts.Rates.Response;

public static class RateDtoFinancialExtensions
{
    public static RateDto WithRecalculatedFinancials(this RateDto rate)
    {
        if (rate.RateDetails.Count == 0)
            return rate;

        var exchangeRate = rate.ExchangeRateApplied is > 0m
            ? rate.ExchangeRateApplied
            : rate.ExchangeRateSale;

        decimal costUsd = 0m;
        decimal saleUsd = 0m;
        decimal costCrc = 0m;
        decimal saleCrc = 0m;
        var recognized = false;

        foreach (var detail in rate.RateDetails)
        {
            var quantity = detail.Quantity > 0m ? detail.Quantity : 1m;
            var cost = detail.CostAmount * quantity;
            var sale = detail.SaleAmount * quantity;
            var code = detail.CurrencyCode.Trim().ToUpperInvariant();

            if (code == "USD")
            {
                recognized = true;
                costUsd += cost;
                saleUsd += sale;
                if (exchangeRate is > 0m)
                {
                    costCrc += cost * exchangeRate.Value;
                    saleCrc += sale * exchangeRate.Value;
                }
            }
            else if (code == "CRC")
            {
                recognized = true;
                costCrc += cost;
                saleCrc += sale;
                if (exchangeRate is > 0m)
                {
                    costUsd += cost / exchangeRate.Value;
                    saleUsd += sale / exchangeRate.Value;
                }
            }
        }

        if (!recognized)
            return rate;

        costUsd = decimal.Round(costUsd, 2, MidpointRounding.AwayFromZero);
        saleUsd = decimal.Round(saleUsd, 2, MidpointRounding.AwayFromZero);
        costCrc = decimal.Round(costCrc, 2, MidpointRounding.AwayFromZero);
        saleCrc = decimal.Round(saleCrc, 2, MidpointRounding.AwayFromZero);

        var utilityUsd = saleUsd - costUsd;
        var utilityCrc = saleCrc - costCrc;
        var nativeCode = rate.CurrencyCode.Trim().ToUpperInvariant();
        var totalCostAmount = nativeCode == "CRC"
            ? costCrc
            : nativeCode == "USD"
                ? costUsd
                : rate.TotalCostAmount;
        var totalSaleAmount = nativeCode == "CRC"
            ? saleCrc
            : nativeCode == "USD"
                ? saleUsd
                : rate.TotalSaleAmount;
        var totalUtilityAmount = totalSaleAmount - totalCostAmount;
        var margin = totalSaleAmount <= 0m
            ? 0m
            : totalUtilityAmount / totalSaleAmount * 100m;

        return rate with
        {
            TotalCostAmount = totalCostAmount,
            TotalSaleAmount = totalSaleAmount,
            TotalUtilityAmount = totalUtilityAmount,
            TotalCostUsd = costUsd,
            TotalSaleUsd = saleUsd,
            TotalUtilityUsd = utilityUsd,
            TotalCostCrc = costCrc,
            TotalSaleCrc = saleCrc,
            TotalUtilityCrc = utilityCrc,
            MarginPercentage = margin,
        };
    }
}
