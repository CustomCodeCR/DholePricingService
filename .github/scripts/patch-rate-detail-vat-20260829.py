from pathlib import Path
import re

ROOT = Path('.')

def read(path):
    return (ROOT / path).read_text(encoding='utf-8')

def write(path, text):
    (ROOT / path).write_text(text, encoding='utf-8')

def replace_once(path, old, new):
    text = read(path)
    if old not in text:
        raise SystemExit(f'anchor not found in {path}: {old[:100]!r}')
    write(path, text.replace(old, new, 1))

# 1) Domain: real persistent VAT state per line.
path = 'src/Dhole.Pricing.Domain/Rates/Entities/RateDetail.cs'
text = read(path)
anchor = '    public decimal Quantity { get; private set; }\n'
insert = '''    public decimal Quantity { get; private set; }\n    public bool ApplyDestinationTax { get; private set; }\n    public decimal DestinationTaxRate { get; private set; }\n\n    public decimal DestinationTaxAmount =>\n        ApplyDestinationTax && DestinationTaxRate > 0m\n            ? decimal.Round(SaleAmount * Quantity * DestinationTaxRate / 100m, 2, MidpointRounding.AwayFromZero)\n            : 0m;\n\n    public void ConfigureDestinationTax(bool applyDestinationTax, decimal destinationTaxRate)\n    {\n        if (destinationTaxRate < 0m || destinationTaxRate > 100m)\n        {\n            throw new ArgumentOutOfRangeException(nameof(destinationTaxRate));\n        }\n\n        ApplyDestinationTax = applyDestinationTax && destinationTaxRate > 0m;\n        DestinationTaxRate = ApplyDestinationTax ? destinationTaxRate : 0m;\n    }\n'''
if 'public bool ApplyDestinationTax' not in text:
    if anchor not in text: raise SystemExit('RateDetail Quantity anchor missing')
    text = text.replace(anchor, insert, 1)
    write(path, text)

# 2) EF configuration.
path = 'src/Dhole.Pricing.Persistence/Configurations/Rates/RateDetailConfiguration.cs'
text = read(path)
if 'apply_destination_tax' not in text:
    anchor = '        builder.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();\n'
    addition = anchor + '''\n        builder.Property(x => x.ApplyDestinationTax)\n            .HasColumnName("apply_destination_tax")\n            .HasDefaultValue(false);\n\n        builder.Property(x => x.DestinationTaxRate)\n            .HasColumnName("destination_tax_rate")\n            .HasPrecision(5, 2)\n            .HasDefaultValue(0m);\n'''
    if anchor not in text: raise SystemExit('RateDetailConfiguration Quantity anchor missing')
    text = text.replace(anchor, addition, 1)
    write(path, text)

# 3) Contracts.
path = 'src/Dhole.Pricing.Contracts/Rates/Response/RateDetailDto.cs'
text = read(path)
if 'ApplyDestinationTax' not in text:
    text = text.replace('    decimal Quantity,\n    string? Notes\n);', '    decimal Quantity,\n    string? Notes,\n    bool ApplyDestinationTax,\n    decimal DestinationTaxRate,\n    decimal DestinationTaxAmount\n);')
    write(path, text)

for path in [
    'src/Dhole.Pricing.Contracts/Rates/Request/CreateRateDetailRequest.cs',
    'src/Dhole.Pricing.Contracts/Rates/Request/UpsertRateExtraDetailRequest.cs',
]:
    text = read(path)
    if 'ApplyDestinationTax' not in text:
        text = text.replace('    string? ChargeBasis = null\n);', '    string? ChargeBasis = null,\n    bool ApplyDestinationTax = false,\n    decimal DestinationTaxRate = 0m\n);')
        write(path, text)

# Optional direct detail-update request, if kept by the contract surface.
path = 'src/Dhole.Pricing.Contracts/Rates/Request/UpdateRateDetailRequest.cs'
text = read(path)
if 'ApplyDestinationTax' not in text:
    text = text.replace('\n);', ',\n    bool ApplyDestinationTax = false,\n    decimal DestinationTaxRate = 0m\n);', 1)
    write(path, text)

# 4) Application command records.
for path, record_name in [
    ('src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommand.cs', 'CreateRateDetailCommandItem'),
    ('src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommand.cs', 'UpsertRateExtraDetailCommandItem'),
]:
    text = read(path)
    if 'DestinationTaxRate' not in text.split(');', 1)[0]:
        # only first detail-item record at top of each file
        text = text.replace('    ChargeBasis? ChargeBasis\n);', '    ChargeBasis? ChargeBasis,\n    bool ApplyDestinationTax = false,\n    decimal DestinationTaxRate = 0m\n);', 1)
        write(path, text)

# 5) Resolver input/output records + pass-through.
path = 'src/Dhole.Pricing.Application/Abstractions/Services/IRateExtraDetailResolver.cs'
text = read(path)
if 'ApplyDestinationTax' not in text:
    text = text.replace('    ChargeBasis? ChargeBasis\n);', '    ChargeBasis? ChargeBasis,\n    bool ApplyDestinationTax = false,\n    decimal DestinationTaxRate = 0m\n);', 1)
    # output record occurrence
    text = text.replace('    ChargeBasis? ChargeBasis\n);', '    ChargeBasis? ChargeBasis,\n    bool ApplyDestinationTax = false,\n    decimal DestinationTaxRate = 0m\n);', 1)
    write(path, text)

path = 'src/Dhole.Pricing.Application/Services/RateExtraDetailResolver.cs'
text = read(path)
if 'input.ApplyDestinationTax' not in text:
    # Every ResolvedRateExtraDetail construction ends with input/cost ChargeBasis.
    text = text.replace('                    input.ChargeBasis\n                )', '                    input.ChargeBasis,\n                    input.ApplyDestinationTax,\n                    input.DestinationTaxRate\n                )')
    text = text.replace('                    cost.ChargeBasis\n                )', '                    cost.ChargeBasis,\n                    input.ApplyDestinationTax,\n                    input.DestinationTaxRate\n                )')
    write(path, text)

# 6) API request -> application item mappings.
path = 'src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs'
text = read(path)
if 'detail.ApplyDestinationTax' not in text:
    # create mapping
    text = text.replace('                    detail.Quantity,\n                    chargeBasis\n                )', '                    detail.Quantity,\n                    chargeBasis,\n                    detail.ApplyDestinationTax,\n                    detail.DestinationTaxRate\n                )', 1)
    # update mapping has same tail later; replace next occurrence if present
    text = text.replace('                    detail.Quantity,\n                    chargeBasis\n                )', '                    detail.Quantity,\n                    chargeBasis,\n                    detail.ApplyDestinationTax,\n                    detail.DestinationTaxRate\n                )', 1)
    write(path, text)

# 7) Create handler: resolver gets tax state and created detail is configured.
path = 'src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommandHandler.cs'
text = read(path)
if 'detail.ApplyDestinationTax' not in text:
    text = text.replace('                    detail.Quantity,\n                    detail.ChargeBasis\n                ),', '                    detail.Quantity,\n                    detail.ChargeBasis,\n                    detail.ApplyDestinationTax,\n                    detail.DestinationTaxRate\n                ),', 1)
    old = '''                rate.AddRateDetail(\n                    rate.Id,\n                    detail.CostId,\n                    detail.Name,\n                    detail.CostDetailType,\n                    detail.CostType,\n                    chargeBasis,\n                    detail.CurrencyId,\n                    detail.CurrencyName,\n                    detail.CurrencyCode,\n                    detail.CostAmount,\n                    detail.SaleAmount,\n                    detail.Notes,\n                    quantity: detail.Quantity ?? 1m,\n                    updatedBy: command.CreatedBy\n                );'''
    new = old.replace('                rate.AddRateDetail(', '                var addedDetail = rate.AddRateDetail(').replace('                );', '                );\n                addedDetail.ConfigureDestinationTax(detail.ApplyDestinationTax, detail.DestinationTaxRate);', 1)
    if old not in text: raise SystemExit('Create handler AddRateDetail block missing')
    text = text.replace(old, new, 1)
    write(path, text)

# 8) Update handler: resolver gets state, then configure existing/new entity.
path = 'src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommandHandler.cs'
text = read(path)
if 'extraDetail.ApplyDestinationTax' not in text and 'detail.ApplyDestinationTax' not in text:
    # command item variable is normally extraDetail in the resolver projection. Handle either shape.
    text = text.replace('                    extraDetail.Quantity,\n                    extraDetail.ChargeBasis\n                ),', '                    extraDetail.Quantity,\n                    extraDetail.ChargeBasis,\n                    extraDetail.ApplyDestinationTax,\n                    extraDetail.DestinationTaxRate\n                ),')
    text = text.replace('                    detail.Quantity,\n                    detail.ChargeBasis\n                ),', '                    detail.Quantity,\n                    detail.ChargeBasis,\n                    detail.ApplyDestinationTax,\n                    detail.DestinationTaxRate\n                ),')

# Add configure calls after updated/added entity is resolved.
if 'modified.ConfigureDestinationTax' not in text:
    marker = '                var modified = rate.RateDetails.First(x => x.Id == detail.Id.Value);\n'
    if marker in text:
        text = text.replace(marker, marker + '                modified.ConfigureDestinationTax(detail.ApplyDestinationTax, detail.DestinationTaxRate);\n', 1)
    else:
        print('warning: modified detail marker not found')
if 'added.ConfigureDestinationTax' not in text:
    # Insert immediately after AddRateDetail call block before addedDetails.Add(added)
    marker = '                addedDetails.Add(added);'
    if marker in text:
        text = text.replace(marker, '                added.ConfigureDestinationTax(detail.ApplyDestinationTax, detail.DestinationTaxRate);\n' + marker, 1)
    else:
        print('warning: added detail marker not found')
write(path, text)

# 9) DTO mapping.
path = 'src/Dhole.Pricing.Application/Features/Rates/RateMappings.cs'
text = read(path)
if 'x.DestinationTaxAmount' not in text:
    text = text.replace('                    x.Quantity,\n                    x.Notes\n                ))', '                    x.Quantity,\n                    x.Notes,\n                    x.ApplyDestinationTax,\n                    x.DestinationTaxRate,\n                    x.DestinationTaxAmount\n                ))')
    write(path, text)

# 10) Revision snapshot includes VAT configuration.
path = 'src/Dhole.Pricing.Application/Features/Rates/RateRevisionSnapshotFactory.cs'
text = read(path)
if 'x.ApplyDestinationTax' not in text:
    text = text.replace('x.CostAmount, x.SaleAmount, x.UtilityAmount, x.Quantity, x.Notes })', 'x.CostAmount, x.SaleAmount, x.UtilityAmount, x.Quantity, x.Notes, x.ApplyDestinationTax, x.DestinationTaxRate, x.DestinationTaxAmount })')
    write(path, text)

# 11) Fixed cost synchronizer: carry VAT state across remove/recreate synchronization.
path = 'src/Dhole.Pricing.Application/Services/RateFixedCostSynchronizer.cs'
text = read(path)
if 'ApplyDestinationTax' not in text:
    # existing tuple captures current selection
    text = text.replace('                    detail.CurrencyCode\n                );', '                    detail.CurrencyCode,\n                    detail.ApplyDestinationTax,\n                    detail.DestinationTaxRate\n                );', 1)
    # Find generic fixed AddRateDetail assignment; convert to capture return.
    generic = '            rate.AddRateDetail(\n                rate.Id,\n                cost.Id,'
    if generic in text:
        text = text.replace(generic, '            var synchronizedDetail = rate.AddRateDetail(\n                rate.Id,\n                cost.Id,', 1)
        # Configure before closing loop by locating first occurrence after variable of updatedBy block.
        idx = text.index('var synchronizedDetail = rate.AddRateDetail')
        tail = text[idx:]
        close = '                updatedBy\n            );'
        if close in tail:
            pos = idx + tail.index(close) + len(close)
            cfg = '''\n            if (hasExistingAmount)\n            {\n                synchronizedDetail.ConfigureDestinationTax(\n                    existingAmount.ApplyDestinationTax,\n                    existingAmount.DestinationTaxRate\n                );\n            }'''
            text = text[:pos] + cfg + text[pos:]
    write(path, text)

print('VAT persistence patch applied.')
