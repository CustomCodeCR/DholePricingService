from pathlib import Path


def load(path: str) -> str:
    return Path(path).read_text(encoding='utf-8')


def save(path: str, text: str) -> None:
    Path(path).write_text(text, encoding='utf-8')


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise SystemExit(f'{label} not found')
    return text.replace(old, new, 1)

# Domain: editable executive is a commercial snapshot on the rate, independent from auth user.
path = 'src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs'
text = load(path)
text = replace_once(
    text,
    '    public string? ClientName { get; private set; }\n    public string? IdtraNumber { get; private set; }',
    '    public string? ClientName { get; private set; }\n    public string? ExecutiveName { get; private set; }\n    public string? IdtraNumber { get; private set; }',
    'RateHeader ExecutiveName property',
)
text = replace_once(
    text,
    '        PickupAddress = Normalize(pickupAddress);\n        PickupLatitude = pickupLatitude;\n        PickupLongitude = pickupLongitude;\n    }\n\n    public void ReplaceContainerAllocations(',
    '        PickupAddress = Normalize(pickupAddress);\n        PickupLatitude = pickupLatitude;\n        PickupLongitude = pickupLongitude;\n    }\n\n    public void ConfigureExecutive(string? executiveName)\n    {\n        // El ejecutivo comercial es editable por Pricing hasta nuevo aviso.\n        // No se deriva del usuario autenticado ni se bloquea contra Auth.\n        ExecutiveName = Normalize(executiveName);\n    }\n\n    public void ReplaceContainerAllocations(',
    'RateHeader ConfigureExecutive',
)
save(path, text)

# EF mapping.
path = 'src/Dhole.Pricing.Persistence/Configurations/Rates/RateHeaderConfiguration.cs'
text = load(path)
text = replace_once(
    text,
    '        builder.Property(x => x.ClientName).HasMaxLength(250).IsRequired(false);\n\n        builder.Property(x => x.IdtraNumber)',
    '        builder.Property(x => x.ClientName).HasMaxLength(250).IsRequired(false);\n\n        builder.Property(x => x.ExecutiveName).HasMaxLength(250).IsRequired(false);\n\n        builder.Property(x => x.IdtraNumber)',
    'ExecutiveName EF mapping',
)
save(path, text)

# Contracts.
path = 'src/Dhole.Pricing.Contracts/Rates/Request/CreateRateRequest.cs'
text = load(path)
text = replace_once(
    text,
    '    string? ClientName = null,\n    string? IdtraNumber = null,',
    '    string? ClientName = null,\n    string? ExecutiveName = null,\n    string? IdtraNumber = null,',
    'CreateRateRequest ExecutiveName',
)
save(path, text)

path = 'src/Dhole.Pricing.Contracts/Rates/Request/UpdateRateRequest.cs'
text = load(path)
text = replace_once(
    text,
    '    string? ClientName = null,\n    string? IdtraNumber = null,',
    '    string? ClientName = null,\n    string? ExecutiveName = null,\n    string? IdtraNumber = null,',
    'UpdateRateRequest ExecutiveName',
)
text = replace_once(
    text,
    '    decimal KgPerCbm = 500m,\n    IReadOnlyCollection<RateCargoLineRequest>? CargoLines = null\n);',
    '    decimal KgPerCbm = 500m,\n    IReadOnlyCollection<RateCargoLineRequest>? CargoLines = null,\n    string? PickupAddress = null,\n    decimal? PickupLatitude = null,\n    decimal? PickupLongitude = null\n);',
    'UpdateRateRequest pickup snapshot',
)
save(path, text)

# Commands.
path = 'src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommand.cs'
text = load(path)
text = replace_once(
    text,
    '    string? ClientName,\n    string? IdtraNumber,',
    '    string? ClientName,\n    string? ExecutiveName,\n    string? IdtraNumber,',
    'CreateRateCommand ExecutiveName',
)
save(path, text)

path = 'src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommand.cs'
text = load(path)
text = replace_once(
    text,
    '    string? ClientName,\n    string? IdtraNumber,',
    '    string? ClientName,\n    string? ExecutiveName,\n    string? IdtraNumber,',
    'UpdateRateCommand ExecutiveName',
)
text = replace_once(
    text,
    '    IReadOnlyCollection<RateCargoLineCommandItem> CargoLines,\n    bool CanApproveLowMargin,',
    '    IReadOnlyCollection<RateCargoLineCommandItem> CargoLines,\n    string? PickupAddress,\n    decimal? PickupLatitude,\n    decimal? PickupLongitude,\n    bool CanApproveLowMargin,',
    'UpdateRateCommand pickup snapshot',
)
save(path, text)

# API endpoint binds the editable values exactly as entered; auth identity stays audit-only.
path = 'src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs'
text = load(path)
text = replace_once(
    text,
    '                request.ContainerQuantity,\n                request.ClientName,\n                request.IdtraNumber,',
    '                request.ContainerQuantity,\n                request.ClientName,\n                request.ExecutiveName,\n                request.IdtraNumber,',
    'Create endpoint ExecutiveName',
)
text = replace_once(
    text,
    '                request.ContainerQuantity,\n                request.ClientName,\n                request.IdtraNumber,',
    '                request.ContainerQuantity,\n                request.ClientName,\n                request.ExecutiveName,\n                request.IdtraNumber,',
    'Update endpoint ExecutiveName',
)
text = replace_once(
    text,
    '                request.TotalVolumeCbm,\n                cargoLines,\n                canApproveLowMargin,\n                httpContext.GetCurrentUserId()',
    '                request.TotalVolumeCbm,\n                cargoLines,\n                request.PickupAddress,\n                request.PickupLatitude,\n                request.PickupLongitude,\n                canApproveLowMargin,\n                httpContext.GetCurrentUserId()',
    'Update endpoint pickup snapshot',
)
save(path, text)

# Create/update handlers persist both snapshots.
path = 'src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommandHandler.cs'
text = load(path)
text = replace_once(
    text,
    '            rate.ConfigurePickupLocation(\n                command.PickupAddress,\n                command.PickupLatitude,\n                command.PickupLongitude\n            );\n\n            var cargoProfile',
    '            rate.ConfigurePickupLocation(\n                command.PickupAddress,\n                command.PickupLatitude,\n                command.PickupLongitude\n            );\n            rate.ConfigureExecutive(command.ExecutiveName);\n\n            var cargoProfile',
    'Create handler ConfigureExecutive',
)
save(path, text)

path = 'src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommandHandler.cs'
text = load(path)
text = replace_once(
    text,
    '                command.RateType,\n                command.UpdatedBy\n            );\n\n            rate.ReplaceContainerAllocations',
    '                command.RateType,\n                command.UpdatedBy\n            );\n            rate.ConfigureExecutive(command.ExecutiveName);\n            rate.ConfigurePickupLocation(\n                command.PickupAddress,\n                command.PickupLatitude,\n                command.PickupLongitude\n            );\n\n            rate.ReplaceContainerAllocations',
    'Update handler commercial snapshots',
)
save(path, text)

# Duplicating a quote keeps client/executive and EXW/FCA pickup history.
path = 'src/Dhole.Pricing.Application/Features/Rates/DuplicateRate/DuplicateRateCommandHandler.cs'
text = load(path)
text = replace_once(
    text,
    '            );\n\n            IReadOnlyCollection<(Guid ContainerTypeId, int Quantity)> requestedContainers =',
    '            );\n            duplicate.ConfigureExecutive(source.ExecutiveName);\n            duplicate.ConfigurePickupLocation(\n                source.PickupAddress,\n                source.PickupLatitude,\n                source.PickupLongitude\n            );\n\n            IReadOnlyCollection<(Guid ContainerTypeId, int Quantity)> requestedContainers =',
    'Duplicate commercial snapshots',
)
save(path, text)

# Response DTO + mapping.
path = 'src/Dhole.Pricing.Contracts/Rates/Response/RateDto.cs'
text = load(path)
text = replace_once(
    text,
    '    string? ClientName,\n    string? IdtraNumber,',
    '    string? ClientName,\n    string? ExecutiveName,\n    string? IdtraNumber,',
    'RateDto ExecutiveName',
)
save(path, text)

path = 'src/Dhole.Pricing.Application/Features/Rates/RateMappings.cs'
text = load(path)
text = replace_once(
    text,
    '            rate.ClientName,\n            rate.IdtraNumber,',
    '            rate.ClientName,\n            rate.ExecutiveName,\n            rate.IdtraNumber,',
    'RateMappings ExecutiveName',
)
save(path, text)

# Idempotent hand-written migration matching the existing pickup migration style.
migration = Path('src/Dhole.Pricing.Persistence/Migrations/20260828020500_AddRateExecutiveName.cs')
if migration.exists():
    raise SystemExit('ExecutiveName migration already exists')
migration.write_text('''using Dhole.Pricing.Persistence.DbContexts;\nusing Microsoft.EntityFrameworkCore.Infrastructure;\nusing Microsoft.EntityFrameworkCore.Migrations;\n\n#nullable disable\n\nnamespace Dhole.Pricing.Persistence.Migrations;\n\n[DbContext(typeof(ServiceDbContext))]\n[Migration("20260828020500_AddRateExecutiveName")]\npublic sealed class AddRateExecutiveName : Migration\n{\n    protected override void Up(MigrationBuilder migrationBuilder)\n    {\n        migrationBuilder.Sql(\n            """\n            ALTER TABLE pricing."RateHeaders"\n                ADD COLUMN IF NOT EXISTS executive_name character varying(250);\n            """\n        );\n    }\n\n    protected override void Down(MigrationBuilder migrationBuilder)\n    {\n        migrationBuilder.Sql(\n            """\n            ALTER TABLE pricing."RateHeaders"\n                DROP COLUMN IF EXISTS executive_name;\n            """\n        );\n    }\n}\n''', encoding='utf-8')

print('Editable client/executive persistence patch applied.')
