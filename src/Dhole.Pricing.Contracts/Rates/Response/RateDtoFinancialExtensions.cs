namespace Dhole.Pricing.Contracts.Rates.Response;

public static class RateDtoFinancialExtensions
{
    public static RateDto WithRecalculatedFinancials(this RateDto rate)
    {
        var exchangeRate = rate.ExchangeRateApplied is > 0m
            ? rate.ExchangeRateApplied
            : rate.ExchangeRateSale;

        decimal costUsd = 0m;
        decimal saleUsd = 0m;
        decimal costCrc = 0m;
        decimal saleCrc = 0m;
        var recognizedDetailCurrency = false;

        foreach (var detail in rate.RateDetails)
        {
            var quantity = detail.Quantity > 0m ? detail.Quantity : 1m;
            var cost = detail.CostAmount * quantity;
            var sale = detail.SaleAmount * quantity;
            var code = CanonicalCurrency(detail.CurrencyCode, detail.CurrencyName);

            if (code == "USD")
            {
                recognizedDetailCurrency = true;
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
                recognizedDetailCurrency = true;
                costCrc += cost;
                saleCrc += sale;
                if (exchangeRate is > 0m)
                {
                    costUsd += cost / exchangeRate.Value;
                    saleUsd += sale / exchangeRate.Value;
                }
            }
        }

        var detailsHaveFinancialValues = recognizedDetailCurrency &&
            (costUsd != 0m || saleUsd != 0m || costCrc != 0m || saleCrc != 0m);

        // Listados antiguos o snapshots incompletos pueden no traer RateDetails con una
        // divisa canónica. En ese caso jamás debemos borrar los importes ya persistidos.
        if (!detailsHaveFinancialValues)
        {
            costUsd = rate.TotalCostUsd;
            saleUsd = rate.TotalSaleUsd;
            costCrc = rate.TotalCostCrc;
            saleCrc = rate.TotalSaleCrc;

            var dualTotalsHaveValues =
                costUsd != 0m || saleUsd != 0m || costCrc != 0m || saleCrc != 0m;

            // Último respaldo: TotalCostAmount/TotalSaleAmount son los totales nativos
            // de la tarifa y sí existen incluso en tarifas creadas antes de los campos
            // duales USD/CRC.
            if (!dualTotalsHaveValues &&
                (rate.TotalCostAmount != 0m || rate.TotalSaleAmount != 0m))
            {
                var nativeCode = CanonicalCurrency(rate.CurrencyCode, rate.CurrencyName);

                if (nativeCode == "CRC")
                {
                    costCrc = rate.TotalCostAmount;
                    saleCrc = rate.TotalSaleAmount;
                    if (exchangeRate is > 0m)
                    {
                        costUsd = costCrc / exchangeRate.Value;
                        saleUsd = saleCrc / exchangeRate.Value;
                    }
                }
                else
                {
                    // Pricing históricamente usa USD como moneda comercial base. Si el
                    // catálogo no permite reconocer el código, conservar el monto nativo
                    // como USD es preferible a mostrar falsamente cero en Tarifas oficiales.
                    costUsd = rate.TotalCostAmount;
                    saleUsd = rate.TotalSaleAmount;
                    if (exchangeRate is > 0m)
                    {
                        costCrc = costUsd * exchangeRate.Value;
                        saleCrc = saleUsd * exchangeRate.Value;
                    }
                }
            }
        }

        // Si solamente uno de los pares estaba persistido, completar el otro usando el
        // mismo TC de venta/aplicado que usa Pricing para la revisión.
        if (exchangeRate is > 0m)
        {
            if ((costUsd != 0m || saleUsd != 0m) && costCrc == 0m && saleCrc == 0m)
            {
                costCrc = costUsd * exchangeRate.Value;
                saleCrc = saleUsd * exchangeRate.Value;
            }
            else if ((costCrc != 0m || saleCrc != 0m) && costUsd == 0m && saleUsd == 0m)
            {
                costUsd = costCrc / exchangeRate.Value;
                saleUsd = saleCrc / exchangeRate.Value;
            }
        }

        costUsd = decimal.Round(costUsd, 2, MidpointRounding.AwayFromZero);
        saleUsd = decimal.Round(saleUsd, 2, MidpointRounding.AwayFromZero);
        costCrc = decimal.Round(costCrc, 2, MidpointRounding.AwayFromZero);
        saleCrc = decimal.Round(saleCrc, 2, MidpointRounding.AwayFromZero);

        var utilityUsd = saleUsd - costUsd;
        var utilityCrc = saleCrc - costCrc;
        var nativeCurrency = CanonicalCurrency(rate.CurrencyCode, rate.CurrencyName);
        var totalCostAmount = nativeCurrency == "CRC"
            ? costCrc
            : nativeCurrency == "USD"
                ? costUsd
                : rate.TotalCostAmount != 0m
                    ? rate.TotalCostAmount
                    : costUsd;
        var totalSaleAmount = nativeCurrency == "CRC"
            ? saleCrc
            : nativeCurrency == "USD"
                ? saleUsd
                : rate.TotalSaleAmount != 0m
                    ? rate.TotalSaleAmount
                    : saleUsd;
        var totalUtilityAmount = totalSaleAmount - totalCostAmount;
        var margin = totalSaleAmount > 0m
            ? totalUtilityAmount / totalSaleAmount * 100m
            : rate.MarginPercentage;

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

    private static string CanonicalCurrency(string? code, string? name)
    {
        var value = $"{code} {name}".Trim().ToUpperInvariant();

        if (value.Contains("CRC", StringComparison.Ordinal) ||
            value.Contains("COLÓN", StringComparison.Ordinal) ||
            value.Contains("COLON", StringComparison.Ordinal) ||
            value.Contains("COLONES", StringComparison.Ordinal))
            return "CRC";

        if (value.Contains("USD", StringComparison.Ordinal) ||
            value.Contains("DÓLAR", StringComparison.Ordinal) ||
            value.Contains("DOLAR", StringComparison.Ordinal) ||
            value.Contains("DOLLAR", StringComparison.Ordinal))
            return "USD";

        return string.Empty;
    }
}
