from pathlib import Path


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text(encoding='utf-8')
    if old not in text:
        raise SystemExit(f'No se encontró {label} en {path}')
    path.write_text(text.replace(old, new, 1), encoding='utf-8')

# 1) API contract: persist the map pickup location with the rate.
request_path = Path('src/Dhole.Pricing.Contracts/Rates/Request/CreateRateRequest.cs')
replace_once(
    request_path,
    '    decimal KgPerCbm = 500m,\n    IReadOnlyCollection<RateCargoLineRequest>? CargoLines = null\n);',
    '    decimal KgPerCbm = 500m,\n    IReadOnlyCollection<RateCargoLineRequest>? CargoLines = null,\n    string? PickupAddress = null,\n    decimal? PickupLatitude = null,\n    decimal? PickupLongitude = null\n);',
    'campos finales de CreateRateRequest',
)

# 2) Command carries location into the aggregate.
command_path = Path('src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommand.cs')
replace_once(
    command_path,
    '    IReadOnlyCollection<RateCargoLineCommandItem> CargoLines,\n    bool CanApproveImportedRate,',
    '    IReadOnlyCollection<RateCargoLineCommandItem> CargoLines,\n    string? PickupAddress,\n    decimal? PickupLatitude,\n    decimal? PickupLongitude,\n    bool CanApproveImportedRate,',
    'campos de ubicación en CreateRateCommand',
)

# 3) Endpoint maps request fields to command.
endpoint_path = Path('src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs')
replace_once(
    endpoint_path,
    '                cargoLines,\n                canApproveImportedRate,\n                canApproveLowMargin,',
    '                cargoLines,\n                request.PickupAddress,\n                request.PickupLatitude,\n                request.PickupLongitude,\n                canApproveImportedRate,\n                canApproveLowMargin,',
    'mapeo de ubicación en CreateRateAsync',
)

# 4) Aggregate stores and validates pickup coordinates only for EXW/FCA.
entity_path = Path('src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs')
replace_once(
    entity_path,
    '    public Guid? IncotermId { get; private set; }\n    public string? IncotermName { get; private set; }\n    public string? IncotermCode { get; private set; }\n\n    public Guid CurrencyId',
    '    public Guid? IncotermId { get; private set; }\n    public string? IncotermName { get; private set; }\n    public string? IncotermCode { get; private set; }\n\n    public string? PickupAddress { get; private set; }\n    public decimal? PickupLatitude { get; private set; }\n    public decimal? PickupLongitude { get; private set; }\n\n    public Guid CurrencyId',
    'propiedades de ubicación en RateHeader',
)
replace_once(
    entity_path,
    '    public void ReplaceContainerAllocations(\n',
    '''    public void ConfigurePickupLocation(\n        string? pickupAddress,\n        decimal? pickupLatitude,\n        decimal? pickupLongitude\n    )\n    {\n        var applies = string.Equals(IncotermCode, "EXW", StringComparison.OrdinalIgnoreCase)\n            || string.Equals(IncotermCode, "FCA", StringComparison.OrdinalIgnoreCase);\n\n        if (!applies)\n        {\n            PickupAddress = null;\n            PickupLatitude = null;\n            PickupLongitude = null;\n            return;\n        }\n\n        if (pickupLatitude is < -90m or > 90m)\n            throw new InvalidOperationException("La latitud de recolección no es válida.");\n        if (pickupLongitude is < -180m or > 180m)\n            throw new InvalidOperationException("La longitud de recolección no es válida.");\n\n        PickupAddress = Normalize(pickupAddress);\n        PickupLatitude = pickupLatitude;\n        PickupLongitude = pickupLongitude;\n    }\n\n    public void ReplaceContainerAllocations(\n''',
    'método ConfigurePickupLocation',
)

# 5) Creation handler saves the location immediately after constructing the rate.
handler_path = Path('src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommandHandler.cs')
replace_once(
    handler_path,
    '''            rate = importedRate is null\n                ? CreateManualRate(command, rateCode)\n                : CreateFromImportedRate(command, importedRate, rateCode);\n\n            var cargoProfile = RateCargoProfileFactory.Create(''',
    '''            rate = importedRate is null\n                ? CreateManualRate(command, rateCode)\n                : CreateFromImportedRate(command, importedRate, rateCode);\n\n            rate.ConfigurePickupLocation(\n                command.PickupAddress,\n                command.PickupLatitude,\n                command.PickupLongitude\n            );\n\n            var cargoProfile = RateCargoProfileFactory.Create(''',
    'persistencia en CreateRateCommandHandler',
)

# 6) EF mapping.
config_path = Path('src/Dhole.Pricing.Persistence/Configurations/Rates/RateHeaderConfiguration.cs')
replace_once(
    config_path,
    '        builder.Property(x => x.IncotermCode).HasMaxLength(40).IsRequired(false);\n\n        builder.Property(x => x.CurrencyId)',
    '        builder.Property(x => x.IncotermCode).HasMaxLength(40).IsRequired(false);\n\n        builder.Property(x => x.PickupAddress).HasMaxLength(1000).IsRequired(false);\n        builder.Property(x => x.PickupLatitude).HasPrecision(10, 7).IsRequired(false);\n        builder.Property(x => x.PickupLongitude).HasPrecision(10, 7).IsRequired(false);\n\n        builder.Property(x => x.CurrencyId)',
    'configuración EF de pickup',
)

# 7) Return persisted location in rate DTO.
dto_path = Path('src/Dhole.Pricing.Contracts/Rates/Response/RateDto.cs')
replace_once(
    dto_path,
    '    string? IncotermCode,\n    int ContainerQuantity,',
    '    string? IncotermCode,\n    string? PickupAddress,\n    decimal? PickupLatitude,\n    decimal? PickupLongitude,\n    int ContainerQuantity,',
    'campos pickup en RateDto',
)

mapping_path = Path('src/Dhole.Pricing.Application/Features/Rates/RateMappings.cs')
replace_once(
    mapping_path,
    '            rate.IncotermCode,\n            rate.ContainerQuantity,',
    '            rate.IncotermCode,\n            rate.PickupAddress,\n            rate.PickupLatitude,\n            rate.PickupLongitude,\n            rate.ContainerQuantity,',
    'mapeo pickup en RateMappings',
)

# 8) Database migration. Custom migrations in this repository use explicit attributes.
migration = Path('src/Dhole.Pricing.Persistence/Migrations/20260827235500_AddRatePickupLocation.cs')
if migration.exists():
    raise SystemExit(f'La migración ya existe: {migration}')
migration.write_text('''using Dhole.Pricing.Persistence.DbContexts;\nusing Microsoft.EntityFrameworkCore.Infrastructure;\nusing Microsoft.EntityFrameworkCore.Migrations;\n\n#nullable disable\n\nnamespace Dhole.Pricing.Persistence.Migrations;\n\n[DbContext(typeof(ServiceDbContext))]\n[Migration("20260827235500_AddRatePickupLocation")]\npublic sealed class AddRatePickupLocation : Migration\n{\n    protected override void Up(MigrationBuilder migrationBuilder)\n    {\n        migrationBuilder.Sql(\n            \"\"\"\n            ALTER TABLE pricing.\"RateHeaders\"\n                ADD COLUMN IF NOT EXISTS pickup_address character varying(1000),\n                ADD COLUMN IF NOT EXISTS pickup_latitude numeric(10,7),\n                ADD COLUMN IF NOT EXISTS pickup_longitude numeric(10,7);\n            \"\"\"\n        );\n    }\n\n    protected override void Down(MigrationBuilder migrationBuilder)\n    {\n        migrationBuilder.Sql(\n            \"\"\"\n            ALTER TABLE pricing.\"RateHeaders\"\n                DROP COLUMN IF EXISTS pickup_longitude,\n                DROP COLUMN IF EXISTS pickup_latitude,\n                DROP COLUMN IF EXISTS pickup_address;\n            \"\"\"\n        );\n    }\n}\n''', encoding='utf-8')

print('Rate pickup location persistence patch applied.')
