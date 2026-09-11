from pathlib import Path


def replace_dashboard_result() -> None:
    path = Path("src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs")
    text = path.read_text(encoding="utf-8")
    start = text.index("private static async Task<IResult> GetRateDashboardAsync")
    end = text.index("private static async Task<IResult> GetRatesAsync", start)
    segment = text[start:end]
    old = "return EndpointResults.FromPaged(result, httpContext);"
    new = "return EndpointResults.FromResult(result, httpContext);"
    if old not in segment:
        raise RuntimeError("No se encontró el retorno esperado del dashboard para corregir.")
    segment = segment.replace(old, new, 1)
    text = text[:start] + segment + text[end:]
    path.write_text(text, encoding="utf-8")


def update_model_snapshot() -> None:
    path = Path("src/Dhole.Pricing.Persistence/Migrations/ServiceDbContextModelSnapshot.cs")
    text = path.read_text(encoding="utf-8")
    rate_detail_start = text.index('modelBuilder.Entity("Dhole.Pricing.Domain.Rates.Entities.RateDetail"')
    rate_header_start = text.index('modelBuilder.Entity("Dhole.Pricing.Domain.Rates.Entities.RateHeader"', rate_detail_start)
    block = text[rate_detail_start:rate_header_start]
    if 'b.Property<string>("BillToClient")' in block:
        return

    marker = '''                    b.Property<string>("ChargeBasis")\n'''
    insert = '''                    b.Property<string>("BillToClient")\n                        .HasMaxLength(200)\n                        .HasColumnType("character varying(200)")\n                        .HasColumnName("bill_to_client");\n\n'''
    if marker not in block:
        raise RuntimeError("No se encontró el punto de inserción de BillToClient en el snapshot.")
    block = block.replace(marker, insert + marker, 1)
    text = text[:rate_detail_start] + block + text[rate_header_start:]
    path.write_text(text, encoding="utf-8")


def update_paged_rate_projection() -> None:
    path = Path("src/Dhole.Pricing.Persistence/Repositories/RateHeaderRepository.cs")
    text = path.read_text(encoding="utf-8")
    old = '''                        d.ApplyDestinationTax,\n                        d.DestinationTaxRate,\n                        d.DestinationTaxAmount\n                    ))'''
    new = '''                        d.ApplyDestinationTax,\n                        d.DestinationTaxRate,\n                        d.DestinationTaxAmount,\n                        d.BillToClient\n                    ))'''
    if old in text:
        text = text.replace(old, new, 1)
    elif new not in text:
        raise RuntimeError("No se encontró la proyección RateDetailDto del listado de tarifas.")
    path.write_text(text, encoding="utf-8")


def validate_feature_shape() -> None:
    checks = {
        "src/Dhole.Pricing.Domain/Rates/Entities/RateDetail.cs": ["BillToClient", "ConfigureBillToClient"],
        "src/Dhole.Pricing.Contracts/Rates/Request/CreateRateDetailRequest.cs": ["BillToClient"],
        "src/Dhole.Pricing.Contracts/Rates/Request/UpsertRateExtraDetailRequest.cs": ["BillToClient"],
        "src/Dhole.Pricing.Contracts/Rates/Response/RateDetailDto.cs": ["BillToClient"],
        "src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommandHandler.cs": ["ConfigureBillToClient"],
        "src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommandHandler.cs": ["ConfigureBillToClient"],
        "src/Dhole.Pricing.Application/Services/RateFixedCostSynchronizer.cs": ["BillToClient"],
        "src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs": ["detail.BillToClient"],
        "src/Dhole.Pricing.Persistence/Repositories/RateHeaderRepository.cs": ["d.BillToClient"],
        "src/Dhole.Pricing.Persistence/Migrations/20260911033000_AddRateDetailBillToClient.cs": ["bill_to_client"],
    }
    for filename, needles in checks.items():
        text = Path(filename).read_text(encoding="utf-8")
        for needle in needles:
            if needle not in text:
                raise RuntimeError(f"Falta {needle!r} en {filename}")


replace_dashboard_result()
update_model_snapshot()
update_paged_rate_projection()
validate_feature_shape()
print("Billing client patch validated.")
