from pathlib import Path


def replace(path: str, old: str, new: str):
    p = Path(path)
    text = p.read_text()
    if old not in text:
        raise SystemExit(f"pattern not found in {path}: {old[:120]!r}")
    p.write_text(text.replace(old, new, 1))


def insert_after(path: str, marker: str, addition: str):
    replace(path, marker, marker + addition)


# 1) Application abstraction for Hacienda exchange rate.
Path("src/Dhole.Pricing.Application/Abstractions/Services/IPricingExchangeRateProvider.cs").write_text('''namespace Dhole.Pricing.Application.Abstractions.Services;\n\npublic sealed record PricingExchangeRateSnapshot(\n    decimal Purchase,\n    decimal Sale,\n    DateTime RateDate,\n    DateTime CapturedAtUtc,\n    string Source\n);\n\npublic interface IPricingExchangeRateProvider\n{\n    Task<PricingExchangeRateSnapshot?> GetUsdCrcAsync(CancellationToken cancellationToken = default);\n}\n''')

# 2) Infrastructure provider.
Path("src/Dhole.Pricing.Infrastructure/ExchangeRates").mkdir(parents=True, exist_ok=True)
Path("src/Dhole.Pricing.Infrastructure/ExchangeRates/HaciendaExchangeRateProvider.cs").write_text('''using System.Globalization;\nusing System.Text.Json;\nusing Dhole.Pricing.Application.Abstractions.Services;\n\nnamespace Dhole.Pricing.Infrastructure.ExchangeRates;\n\npublic sealed class HaciendaExchangeRateProvider(HttpClient httpClient) : IPricingExchangeRateProvider\n{\n    public async Task<PricingExchangeRateSnapshot?> GetUsdCrcAsync(\n        CancellationToken cancellationToken = default\n    )\n    {\n        try\n        {\n            using var response = await httpClient.GetAsync("indicadores/tc/dolar", cancellationToken);\n            if (!response.IsSuccessStatusCode) return null;\n\n            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);\n            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);\n            var root = document.RootElement;\n\n            if (!TryReadRate(root, "compra", out var purchase, out var purchaseDate)\n                || !TryReadRate(root, "venta", out var sale, out var saleDate)\n                || purchase <= 0m\n                || sale <= 0m)\n            {\n                return null;\n            }\n\n            var date = saleDate != default ? saleDate : purchaseDate;\n            return new PricingExchangeRateSnapshot(\n                Purchase: purchase,\n                Sale: sale,\n                RateDate: DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),\n                CapturedAtUtc: DateTime.UtcNow,\n                Source: "Ministerio de Hacienda de Costa Rica"\n            );\n        }\n        catch (HttpRequestException)\n        {\n            return null;\n        }\n        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)\n        {\n            return null;\n        }\n        catch (JsonException)\n        {\n            return null;\n        }\n    }\n\n    private static bool TryReadRate(\n        JsonElement root,\n        string propertyName,\n        out decimal value,\n        out DateTime date\n    )\n    {\n        value = 0m;\n        date = default;\n\n        if (!root.TryGetProperty(propertyName, out var node)) return false;\n        if (!node.TryGetProperty("valor", out var valueNode)) return false;\n\n        if (valueNode.ValueKind == JsonValueKind.Number)\n        {\n            if (!valueNode.TryGetDecimal(out value)) return false;\n        }\n        else if (!decimal.TryParse(\n            valueNode.GetString(),\n            NumberStyles.Number,\n            CultureInfo.InvariantCulture,\n            out value))\n        {\n            return false;\n        }\n\n        if (node.TryGetProperty("fecha", out var dateNode))\n        {\n            DateTime.TryParse(\n                dateNode.GetString(),\n                CultureInfo.InvariantCulture,\n                DateTimeStyles.AssumeUniversal,\n                out date\n            );\n        }\n\n        return true;\n    }\n}\n''')

# 3) Register typed HttpClient.
replace(
    "src/Dhole.Pricing.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs",
    "using Dhole.Pricing.Infrastructure.GrpcClients;\n",
    "using Dhole.Pricing.Infrastructure.GrpcClients;\nusing Dhole.Pricing.Infrastructure.ExchangeRates;\n",
)
replace(
    "src/Dhole.Pricing.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs",
    "        services.AddPricingAuthRecipientClient(configuration);\n\n        services.AddScoped<ExtractAndPersistFclPricingImportService>();",
    "        services.AddPricingAuthRecipientClient(configuration);\n        services.AddHttpClient<IPricingExchangeRateProvider, HaciendaExchangeRateProvider>(client =>\n        {\n            client.BaseAddress = new Uri(\"https://api.hacienda.go.cr/\");\n            client.Timeout = TimeSpan.FromSeconds(10);\n            client.DefaultRequestHeaders.Accept.ParseAdd(\"application/json\");\n        });\n\n        services.AddScoped<ExtractAndPersistFclPricingImportService>();",
)

# 4) Domain snapshot fields + method.
replace(
    "src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs",
    "    public string CurrencyCode { get; private set; } = string.Empty;\n\n    public int FreeDays",
    "    public string CurrencyCode { get; private set; } = string.Empty;\n\n    public decimal? ExchangeRatePurchase { get; private set; }\n    public decimal? ExchangeRateSale { get; private set; }\n    public decimal? ExchangeRateApplied { get; private set; }\n    public DateTime? ExchangeRateDate { get; private set; }\n    public DateTime? ExchangeRateCapturedAtUtc { get; private set; }\n    public string? ExchangeRateSource { get; private set; }\n    public bool ExchangeRateManualOverride { get; private set; }\n\n    public int FreeDays",
)
insert_after(
    "src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs",
    "    public void ConfigurePickupLocation(\n        string? pickupAddress,\n        decimal? pickupLatitude,\n        decimal? pickupLongitude\n    )\n    {\n        var applies = string.Equals(IncotermCode, \"EXW\", StringComparison.OrdinalIgnoreCase)\n            || string.Equals(IncotermCode, \"FCA\", StringComparison.OrdinalIgnoreCase);\n\n        if (!applies)\n        {\n            PickupAddress = null;\n            PickupLatitude = null;\n            PickupLongitude = null;\n            return;\n        }\n\n        if (pickupLatitude is < -90m or > 90m)\n            throw new InvalidOperationException(\"La latitud de recolección no es válida.\");\n        if (pickupLongitude is < -180m or > 180m)\n            throw new InvalidOperationException(\"La longitud de recolección no es válida.\");\n\n        PickupAddress = Normalize(pickupAddress);\n        PickupLatitude = pickupLatitude;\n        PickupLongitude = pickupLongitude;\n    }\n",
    '''\n    public void ConfigureExchangeRateSnapshot(\n        decimal? purchase,\n        decimal? sale,\n        decimal applied,\n        DateTime? rateDate,\n        DateTime capturedAtUtc,\n        string source,\n        Guid? updatedBy\n    )\n    {\n        if (applied <= 0m)\n            throw new InvalidOperationException("El tipo de cambio aplicado debe ser mayor que cero.");\n        if (purchase.HasValue && purchase.Value <= 0m)\n            throw new InvalidOperationException("El tipo de cambio de compra no es válido.");\n        if (sale.HasValue && sale.Value <= 0m)\n            throw new InvalidOperationException("El tipo de cambio de venta no es válido.");\n\n        ExchangeRatePurchase = purchase;\n        ExchangeRateSale = sale;\n        ExchangeRateApplied = applied;\n        ExchangeRateDate = rateDate?.Date;\n        ExchangeRateCapturedAtUtc = capturedAtUtc;\n        ExchangeRateSource = Normalize(source) ?? "Manual";\n        ExchangeRateManualOverride = !sale.HasValue || Math.Abs(applied - sale.Value) > 0.0001m;\n        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());\n    }\n'''
)

# 5) EF mapping.
replace(
    "src/Dhole.Pricing.Persistence/Configurations/Rates/RateHeaderConfiguration.cs",
    "        builder.Property(x => x.CurrencyCode).HasMaxLength(20).IsRequired();\n\n        builder.Property(x => x.FreeDays)",
    "        builder.Property(x => x.CurrencyCode).HasMaxLength(20).IsRequired();\n\n        builder.Property(x => x.ExchangeRatePurchase).HasPrecision(18, 6).IsRequired(false);\n        builder.Property(x => x.ExchangeRateSale).HasPrecision(18, 6).IsRequired(false);\n        builder.Property(x => x.ExchangeRateApplied).HasPrecision(18, 6).IsRequired(false);\n        builder.Property(x => x.ExchangeRateDate).IsRequired(false);\n        builder.Property(x => x.ExchangeRateCapturedAtUtc).IsRequired(false);\n        builder.Property(x => x.ExchangeRateSource).HasMaxLength(160).IsRequired(false);\n        builder.Property(x => x.ExchangeRateManualOverride).IsRequired().HasDefaultValue(false);\n\n        builder.Property(x => x.FreeDays)",
)

# 6) Migration. SQL is idempotent so production repair is safe.
Path("src/Dhole.Pricing.Persistence/Migrations/20260828205000_AddHaciendaExchangeRateSnapshot.cs").write_text('''using Dhole.Pricing.Persistence.DbContexts;\nusing Microsoft.EntityFrameworkCore.Infrastructure;\nusing Microsoft.EntityFrameworkCore.Migrations;\n\n#nullable disable\n\nnamespace Dhole.Pricing.Persistence.Migrations;\n\n[DbContext(typeof(ServiceDbContext))]\n[Migration("20260828205000_AddHaciendaExchangeRateSnapshot")]\npublic sealed class AddHaciendaExchangeRateSnapshot : Migration\n{\n    protected override void Up(MigrationBuilder migrationBuilder)\n    {\n        migrationBuilder.Sql(\n            """\n            ALTER TABLE pricing."RateHeaders"\n                ADD COLUMN IF NOT EXISTS exchange_rate_purchase numeric(18,6),\n                ADD COLUMN IF NOT EXISTS exchange_rate_sale numeric(18,6),\n                ADD COLUMN IF NOT EXISTS exchange_rate_applied numeric(18,6),\n                ADD COLUMN IF NOT EXISTS exchange_rate_date timestamp with time zone,\n                ADD COLUMN IF NOT EXISTS exchange_rate_captured_at_utc timestamp with time zone,\n                ADD COLUMN IF NOT EXISTS exchange_rate_source character varying(160),\n                ADD COLUMN IF NOT EXISTS exchange_rate_manual_override boolean NOT NULL DEFAULT FALSE;\n            """\n        );\n    }\n\n    protected override void Down(MigrationBuilder migrationBuilder)\n    {\n        migrationBuilder.Sql(\n            """\n            ALTER TABLE pricing."RateHeaders"\n                DROP COLUMN IF EXISTS exchange_rate_manual_override,\n                DROP COLUMN IF EXISTS exchange_rate_source,\n                DROP COLUMN IF EXISTS exchange_rate_captured_at_utc,\n                DROP COLUMN IF EXISTS exchange_rate_date,\n                DROP COLUMN IF EXISTS exchange_rate_applied,\n                DROP COLUMN IF EXISTS exchange_rate_sale,\n                DROP COLUMN IF EXISTS exchange_rate_purchase;\n            """\n        );\n    }\n}\n''')

# 7) API contract accepts only the editable applied value. Official values always come from Hacienda server-side.
replace(
    "src/Dhole.Pricing.Contracts/Rates/Request/CreateRateRequest.cs",
    "    decimal? PickupLongitude = null\n);",
    "    decimal? PickupLongitude = null,\n    decimal? ExchangeRateApplied = null\n);",
)
replace(
    "src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommand.cs",
    "    decimal? PickupLongitude,\n    bool CanApproveImportedRate,",
    "    decimal? PickupLongitude,\n    decimal? ExchangeRateApplied,\n    bool CanApproveImportedRate,",
)

# 8) Create handler obtains fresh Hacienda values at the moment of creation and preserves manual override.
replace(
    "src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommandHandler.cs",
    "    IPricingConfigCatalogClient configCatalog,\n    IPricingAuditService audit,",
    "    IPricingConfigCatalogClient configCatalog,\n    IPricingExchangeRateProvider exchangeRateProvider,\n    IPricingAuditService audit,",
)
replace(
    "src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommandHandler.cs",
    "        var resolvedDetails = new List<ResolvedRateExtraDetail>();",
    "        var officialExchangeRate = await exchangeRateProvider.GetUsdCrcAsync(cancellationToken);\n        var requestedAppliedExchangeRate = command.ExchangeRateApplied is > 0m\n            ? command.ExchangeRateApplied.Value\n            : officialExchangeRate?.Sale;\n        if (requestedAppliedExchangeRate is null or <= 0m)\n            return Result.Failure<Guid>(PricingErrors.ExchangeRateUnavailable);\n\n        var resolvedDetails = new List<ResolvedRateExtraDetail>();",
)
replace(
    "src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommandHandler.cs",
    "            rate.ConfigureExecutive(command.ExecutiveName);\n\n            var cargoProfile",
    "            rate.ConfigureExecutive(command.ExecutiveName);\n            rate.ConfigureExchangeRateSnapshot(\n                officialExchangeRate?.Purchase,\n                officialExchangeRate?.Sale,\n                requestedAppliedExchangeRate.Value,\n                officialExchangeRate?.RateDate,\n                officialExchangeRate?.CapturedAtUtc ?? DateTime.UtcNow,\n                officialExchangeRate?.Source ?? \"Manual (Hacienda no disponible al crear)\",\n                command.CreatedBy\n            );\n\n            var cargoProfile",
)

# 9) Endpoint: expose current purchase/sale and pass editable value into create command.
replace(
    "src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs",
    "        group.MapPost(\"/\", CreateRateAsync).RequireScope(PricingConstants.Scopes.RateCreate);",
    "        group\n            .MapGet(\"/exchange-rate/usd-crc\", GetUsdCrcExchangeRateAsync)\n            .RequireScope(PricingConstants.Scopes.RateView);\n\n        group.MapPost(\"/\", CreateRateAsync).RequireScope(PricingConstants.Scopes.RateCreate);",
)
insert_after(
    "src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs",
    "    private static IResult GetRateReportTemplateDefinition()\n",
    ""  # no-op marker check below; method inserted separately
)
# Insert method immediately before report definition.
replace(
    "src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs",
    "    private static IResult GetRateReportTemplateDefinition()\n    {",
    '''    private static async Task<IResult> GetUsdCrcExchangeRateAsync(\n        IPricingExchangeRateProvider exchangeRateProvider,\n        HttpContext httpContext,\n        CancellationToken cancellationToken\n    )\n    {\n        var snapshot = await exchangeRateProvider.GetUsdCrcAsync(cancellationToken);\n        if (snapshot is null)\n        {\n            return Results.Problem(\n                title: "Tipo de cambio no disponible",\n                detail: "No fue posible consultar el tipo de cambio del dólar en Hacienda en este momento.",\n                statusCode: StatusCodes.Status503ServiceUnavailable\n            );\n        }\n\n        return EndpointResults.Ok(new\n        {\n            purchase = snapshot.Purchase,\n            sale = snapshot.Sale,\n            rateDate = snapshot.RateDate,\n            capturedAtUtc = snapshot.CapturedAtUtc,\n            source = snapshot.Source\n        });\n    }\n\n    private static IResult GetRateReportTemplateDefinition()\n    {'''
)
replace(
    "src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs",
    "                request.PickupLongitude,\n                canApproveImportedRate,",
    "                request.PickupLongitude,\n                request.ExchangeRateApplied,\n                canApproveImportedRate,",
)

# 10) Domain error.
replace(
    "src/Dhole.Pricing.Domain/Shared/PricingErrors.cs",
    "    public static readonly Error RateInvalidValidityRange = new(\n",
    "    public static readonly Error ExchangeRateUnavailable = new(\n        \"Pricing.ExchangeRateUnavailable\",\n        \"No fue posible obtener el tipo de cambio de Hacienda. Puede ingresar manualmente un tipo de cambio aplicado mayor que cero para continuar.\"\n    );\n\n    public static readonly Error RateInvalidValidityRange = new(\n",
)

# 11) DTO and mapping.
replace(
    "src/Dhole.Pricing.Contracts/Rates/Response/RateDto.cs",
    "    string CurrencyCode,\n    int FreeDays,",
    "    string CurrencyCode,\n    decimal? ExchangeRatePurchase,\n    decimal? ExchangeRateSale,\n    decimal? ExchangeRateApplied,\n    DateTime? ExchangeRateDate,\n    DateTime? ExchangeRateCapturedAtUtc,\n    string? ExchangeRateSource,\n    bool ExchangeRateManualOverride,\n    int FreeDays,",
)
replace(
    "src/Dhole.Pricing.Application/Features/Rates/RateMappings.cs",
    "            rate.CurrencyCode,\n            rate.FreeDays,",
    "            rate.CurrencyCode,\n            rate.ExchangeRatePurchase,\n            rate.ExchangeRateSale,\n            rate.ExchangeRateApplied,\n            rate.ExchangeRateDate,\n            rate.ExchangeRateCapturedAtUtc,\n            rate.ExchangeRateSource,\n            rate.ExchangeRateManualOverride,\n            rate.FreeDays,",
)
replace(
    "src/Dhole.Pricing.Application/Auditing/PricingAuditSnapshots.cs",
    "            rateHeader.CurrencyCode,\n\n            rateHeader.FreeDays,",
    "            rateHeader.CurrencyCode,\n            rateHeader.ExchangeRatePurchase,\n            rateHeader.ExchangeRateSale,\n            rateHeader.ExchangeRateApplied,\n            rateHeader.ExchangeRateDate,\n            rateHeader.ExchangeRateCapturedAtUtc,\n            rateHeader.ExchangeRateSource,\n            rateHeader.ExchangeRateManualOverride,\n\n            rateHeader.FreeDays,",
)

# 12) Duplicates get a new Hacienda snapshot at the time the new quote is created.
replace(
    "src/Dhole.Pricing.Application/Features/Rates/DuplicateRate/DuplicateRateCommandHandler.cs",
    "    IPricingConfigCatalogClient configCatalog,\n    IPricingAuditService audit,",
    "    IPricingConfigCatalogClient configCatalog,\n    IPricingExchangeRateProvider exchangeRateProvider,\n    IPricingAuditService audit,",
)
replace(
    "src/Dhole.Pricing.Application/Features/Rates/DuplicateRate/DuplicateRateCommandHandler.cs",
    "        var rateCode = await rateCodeGenerator.GenerateAsync(cancellationToken);\n\n        RateHeader duplicate;",
    "        var rateCode = await rateCodeGenerator.GenerateAsync(cancellationToken);\n        var currentExchangeRate = await exchangeRateProvider.GetUsdCrcAsync(cancellationToken);\n\n        RateHeader duplicate;",
)
replace(
    "src/Dhole.Pricing.Application/Features/Rates/DuplicateRate/DuplicateRateCommandHandler.cs",
    "            duplicate.ConfigureExecutive(source.ExecutiveName);\n            duplicate.ConfigurePickupLocation(",
    "            duplicate.ConfigureExecutive(source.ExecutiveName);\n            var duplicateAppliedExchangeRate = currentExchangeRate?.Sale ?? source.ExchangeRateApplied;\n            if (duplicateAppliedExchangeRate is > 0m)\n            {\n                duplicate.ConfigureExchangeRateSnapshot(\n                    currentExchangeRate?.Purchase,\n                    currentExchangeRate?.Sale,\n                    duplicateAppliedExchangeRate.Value,\n                    currentExchangeRate?.RateDate ?? source.ExchangeRateDate,\n                    currentExchangeRate?.CapturedAtUtc ?? DateTime.UtcNow,\n                    currentExchangeRate?.Source ?? source.ExchangeRateSource ?? \"Manual\",\n                    command.CreatedBy\n                );\n            }\n            duplicate.ConfigurePickupLocation(",
)

print("Hacienda exchange-rate patch applied")
