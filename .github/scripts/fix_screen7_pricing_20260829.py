from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise SystemExit(f"{label} anchor not found")
    return text.replace(old, new, 1)


resolver_path = Path("src/Dhole.Pricing.Application/Services/RateExtraDetailResolver.cs")
resolver = resolver_path.read_text(encoding="utf-8-sig")
resolver = replace_once(
    resolver,
    """                cost.IsAccountant,
                input.Quantity,
                cost.ChargeBasis
            )
        );
    }

    private static (decimal CostAmount, decimal SaleAmount) ResolveGeneratedInsuranceAmounts(""",
    """                cost.IsAccountant,
                input.Quantity,
                cost.ChargeBasis,
                input.ApplyDestinationTax,
                input.DestinationTaxRate
            )
        );
    }

    private static (decimal CostAmount, decimal SaleAmount) ResolveGeneratedInsuranceAmounts(""",
    "RateExtraDetailResolver",
)
resolver_path.write_text(resolver, encoding="utf-8")


dto_path = Path("src/Dhole.Pricing.Contracts/Rates/Response/RateDto.cs")
dto = dto_path.read_text(encoding="utf-8-sig")
dto = replace_once(
    dto,
    """    IReadOnlyCollection<RateServiceDto> Services
);
""",
    """    IReadOnlyCollection<RateServiceDto> Services
)
{
    public decimal TotalTaxUsd => CalculateTaxTotals().TaxUsd;
    public decimal TotalTaxCrc => CalculateTaxTotals().TaxCrc;
    public decimal TotalSaleWithTaxUsd => TotalSaleUsd + TotalTaxUsd;
    public decimal TotalSaleWithTaxCrc => TotalSaleCrc + TotalTaxCrc;

    private (decimal TaxUsd, decimal TaxCrc) CalculateTaxTotals()
    {
        decimal taxUsd = 0m;
        decimal taxCrc = 0m;
        var exchangeRate = ExchangeRateApplied is > 0m ? ExchangeRateApplied : ExchangeRateSale;

        foreach (var detail in RateDetails)
        {
            var tax = detail.DestinationTaxAmount;
            if (tax <= 0m) continue;

            var code = detail.CurrencyCode.Trim().ToUpperInvariant();
            if (code == "USD")
            {
                taxUsd += tax;
                if (exchangeRate is > 0m) taxCrc += tax * exchangeRate.Value;
            }
            else if (code == "CRC")
            {
                taxCrc += tax;
                if (exchangeRate is > 0m) taxUsd += tax / exchangeRate.Value;
            }
        }

        return (
            decimal.Round(taxUsd, 2, MidpointRounding.AwayFromZero),
            decimal.Round(taxCrc, 2, MidpointRounding.AwayFromZero)
        );
    }
}
""",
    "RateDto",
)
dto_path.write_text(dto, encoding="utf-8")


helper_path = Path("src/Dhole.Pricing.Contracts/Rates/Response/RateDtoFinancialExtensions.cs")
helper_path.write_text(
    """namespace Dhole.Pricing.Contracts.Rates.Response;

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
""",
    encoding="utf-8",
)


mappings_path = Path("src/Dhole.Pricing.Application/Features/Rates/RateMappings.cs")
mappings = mappings_path.read_text(encoding="utf-8-sig")
mappings = replace_once(
    mappings,
    "        return new RateDto(\n",
    "        var dto = new RateDto(\n",
    "RateMappings start",
)
mappings = replace_once(
    mappings,
    """                .ToList()
        );
    }
}
""",
    """                .ToList()
        );

        return dto.WithRecalculatedFinancials();
    }
}
""",
    "RateMappings end",
)
mappings_path.write_text(mappings, encoding="utf-8")


repo_path = Path("src/Dhole.Pricing.Persistence/Repositories/RateHeaderRepository.cs")
repo = repo_path.read_text(encoding="utf-8-sig")
repo = replace_once(
    repo,
    """            .ToListAsync(cancellationToken);

        return PagedResult<RateDto>.Create(items, page.PageNumber, page.PageSize, total);
    }

    public async Task<IReadOnlyCollection<RateSelectDto>> GetForSelectAsync(""",
    """            .ToListAsync(cancellationToken);

        // RateDetails are the source of truth. Recalculate the read snapshot so historical
        // rates created before aggregate USD/CRC fields were populated never render as 0.00.
        items = items.Select(item => item.WithRecalculatedFinancials()).ToList();

        return PagedResult<RateDto>.Create(items, page.PageNumber, page.PageSize, total);
    }

    public async Task<IReadOnlyCollection<RateSelectDto>> GetForSelectAsync(""",
    "RateHeaderRepository",
)
repo_path.write_text(repo, encoding="utf-8")

print("Pricing Screen 7 patch applied.")
