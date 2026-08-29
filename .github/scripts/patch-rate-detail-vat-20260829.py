from pathlib import Path

ROOT = Path('.')

def read(path):
    return (ROOT / path).read_text(encoding='utf-8')

def write(path, text):
    (ROOT / path).write_text(text, encoding='utf-8')

# 1) Domain: real persistent VAT state per line.
path = 'src/Dhole.Pricing.Domain/Rates/Entities/RateDetail.cs'
text = read(path)
anchor = '    public decimal Quantity { get; private set; }\n'
insert = '''    public decimal Quantity { get; private set; }\n    public bool ApplyDestinationTax { get; private set; }\n    public decimal DestinationTaxRate { get; private set; }\n\n    public decimal DestinationTaxAmount =>\n        ApplyDestinationTax && DestinationTaxRate > 0m\n            ? decimal.Round(SaleAmount * Quantity * DestinationTaxRate / 100m, 2, MidpointRounding.AwayFromZero)\n            : 0m;\n\n    public void ConfigureDestinationTax(bool applyDestinationTax, decimal destinationTaxRate)\n    {\n        if (destinationTaxRate < 0m || destinationTaxRate > 100m)\n        {\n            throw new ArgumentOutOfRangeException(nameof(destinationTaxRate));\n        }\n\n        ApplyDestinationTax = applyDestinationTax && destinationTaxRate > 0m;\n        DestinationTaxRate = ApplyDestinationTax ? destinationTaxRate : 0m;\n    }\n'''
if 'public bool ApplyDestinationTax' not in text:
    if anchor not in text: raise SystemExit('RateDetail Quantity anchor missing')
    text = text.replace(anchor, insert, 1)
    write(path, text)

# 2) EF configuration. The feature branch may already contain this edit.
path = 'src/Dhole.Pricing.Persistence/Configurations/Rates/RateDetailConfiguration.cs'
text = read(path)
if 'apply_destination_tax' not in text:
    anchor = '        builder.Property(x => x.Quantity).HasPrecision(18, 6).IsRequired().HasDefaultValue(1m);\n'
    addition = anchor + '''\n        builder.Property(x => x.ApplyDestinationTax)\n            .HasColumnName("apply_destination_tax")\n            .HasDefaultValue(false);\n\n        builder.Property(x => x.DestinationTaxRate)\n            .HasColumnName("destination_tax_rate")\n            .HasPrecision(5, 2)\n            .HasDefaultValue(0m);\n'''
    if anchor not in text: raise SystemExit('RateDetailConfiguration Quantity anchor missing')
    text = text.replace(anchor, addition, 1)
    write(path, text)

# 3) Contracts.
path = 'src/Dhole.Pricing.Contracts/Rates/Response/RateDetailDto.cs'
text = read(path)
if 'ApplyDestinationTax' not in text:
    old = '    decimal Quantity,\n    string? Notes\n);'
    new = '    decimal Quantity,\n    string? Notes,\n    bool ApplyDestinationTax,\n    decimal DestinationTaxRate,\n    decimal DestinationTaxAmount\n);'
    if old not in text: raise SystemExit('RateDetailDto tail missing')
    text = text.replace(old, new, 1)
    write(path, text)

for path in [
    'src/Dhole.Pricing.Contracts/Rates/Request/CreateRateDetailRequest.cs',
    'src/Dhole.Pricing.Contracts/Rates/Request/UpsertRateExtraDetailRequest.cs',
]:
    text = read(path)
    if 'ApplyDestinationTax' not in text:
        old = '    string? ChargeBasis = null\n);'
        new = '    string? ChargeBasis = null,\n    bool ApplyDestinationTax = false,\n    decimal DestinationTaxRate = 0m\n);'
        if old not in text: raise SystemExit(f'{path} tail missing')
        text = text.replace(old, new, 1)
        write(path, text)

path = 'src/Dhole.Pricing.Contracts/Rates/Request/UpdateRateDetailRequest.cs'
text = read(path)
if 'ApplyDestinationTax' not in text:
    close = '\n);'
    if close not in text: raise SystemExit('UpdateRateDetailRequest close missing')
    text = text.replace(close, ',\n    bool ApplyDestinationTax = false,\n    decimal DestinationTaxRate = 0m\n);', 1)
    write(path, text)

# 4) Application command records.
for path in [
    'src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommand.cs',
    'src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommand.cs',
]:
    text = read(path)
    first_record_end = text.index(');')
    if 'DestinationTaxRate' not in text[:first_record_end]:
        old = '    ChargeBasis? ChargeBasis\n);'
        new = '    ChargeBasis? ChargeBasis,\n    bool ApplyDestinationTax = false,\n    decimal DestinationTaxRate = 0m\n);'
        if old not in text: raise SystemExit(f'{path} detail command tail missing')
        text = text.replace(old, new, 1)
        write(path, text)

# 5) Resolver input/output records + pass-through.
path = 'src/Dhole.Pricing.Application/Abstractions/Services/IRateExtraDetailResolver.cs'
text = read(path)
if 'ApplyDestinationTax' not in text:
    old = '    ChargeBasis? ChargeBasis\n);'
    new = '    ChargeBasis? ChargeBasis,\n    bool ApplyDestinationTax = false,\n    decimal DestinationTaxRate = 0m\n);'
    if text.count(old) < 2: raise SystemExit('Resolver record tails missing')
    text = text.replace(old, new, 2)
    write(path, text)

path = 'src/Dhole.Pricing.Application/Services/RateExtraDetailResolver.cs'
text = read(path)
if 'input.ApplyDestinationTax' not in text:
    text = text.replace('                    input.ChargeBasis\n                )', '                    input.ChargeBasis,\n                    input.ApplyDestinationTax,\n                    input.DestinationTaxRate\n                )')
    text = text.replace('                    cost.ChargeBasis\n                )', '                    cost.ChargeBasis,\n                    input.ApplyDestinationTax,\n                    input.DestinationTaxRate\n                )')
    write(path, text)

# 6) API request -> application item mappings (create + update).
path = 'src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs'
text = read(path)
if 'detail.ApplyDestinationTax' not in text:
    old = '                    detail.Quantity,\n                    chargeBasis\n                )'
    new = '                    detail.Quantity,\n                    chargeBasis,\n                    detail.ApplyDestinationTax,\n                    detail.DestinationTaxRate\n                )'
    if text.count(old) < 2: raise SystemExit('RateEndpoints detail mappings missing')
    text = text.replace(old, new, 2)
    write(path, text)

# 7) Create handler: resolver gets tax state and created detail is configured.
path = 'src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommandHandler.cs'
text = read(path)
if 'detail.ApplyDestinationTax' not in text:
    old = '                    detail.Quantity,\n                    detail.ChargeBasis\n                ),'
    new = '                    detail.Quantity,\n                    detail.ChargeBasis,\n                    detail.ApplyDestinationTax,\n                    detail.DestinationTaxRate\n                ),'
    if old not in text: raise SystemExit('Create resolver input tail missing')
    text = text.replace(old, new, 1)

if 'addedDetail.ConfigureDestinationTax' not in text:
    old = '''                rate.AddRateDetail(\n                    rate.Id,\n                    detail.CostId,\n                    detail.Name,\n                    detail.CostDetailType,\n                    detail.CostType,\n                    chargeBasis,\n                    detail.CurrencyId,\n                    detail.CurrencyName,\n                    detail.CurrencyCode,\n                    detail.CostAmount,\n                    detail.SaleAmount,\n                    detail.Notes,\n                    quantity: detail.Quantity ?? 1m,\n                    updatedBy: command.CreatedBy\n                );'''
    new = '''                var addedDetail = rate.AddRateDetail(\n                    rate.Id,\n                    detail.CostId,\n                    detail.Name,\n                    detail.CostDetailType,\n                    detail.CostType,\n                    chargeBasis,\n                    detail.CurrencyId,\n                    detail.CurrencyName,\n                    detail.CurrencyCode,\n                    detail.CostAmount,\n                    detail.SaleAmount,\n                    detail.Notes,\n                    quantity: detail.Quantity ?? 1m,\n                    updatedBy: command.CreatedBy\n                );\n                addedDetail.ConfigureDestinationTax(\n                    detail.ApplyDestinationTax,\n                    detail.DestinationTaxRate\n                );'''
    if old not in text: raise SystemExit('Create AddRateDetail block missing')
    text = text.replace(old, new, 1)
write(path, text)

# 8) Update handler: resolver gets state, then configure existing/new entity.
path = 'src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommandHandler.cs'
text = read(path)
if 'detail.ApplyDestinationTax' not in text:
    old = '                    detail.Quantity,\n                    detail.ChargeBasis\n                ),'
    new = '                    detail.Quantity,\n                    detail.ChargeBasis,\n                    detail.ApplyDestinationTax,\n                    detail.DestinationTaxRate\n                ),'
    if old not in text: raise SystemExit('Update resolver input tail missing')
    text = text.replace(old, new, 1)

if 'modified.ConfigureDestinationTax' not in text:
    old = '                    modifiedDetails.Add(rate.RateDetails.First(x => x.Id == detail.Id.Value));'
    new = '''                    var modified = rate.RateDetails.First(x => x.Id == detail.Id.Value);\n                    modified.ConfigureDestinationTax(\n                        detail.ApplyDestinationTax,\n                        detail.DestinationTaxRate\n                    );\n                    modifiedDetails.Add(modified);'''
    if old not in text: raise SystemExit('Update modified detail line missing')
    text = text.replace(old, new, 1)

if 'added.ConfigureDestinationTax' not in text:
    old = '                    addedDetails.Add(added);'
    new = '''                    added.ConfigureDestinationTax(\n                        detail.ApplyDestinationTax,\n                        detail.DestinationTaxRate\n                    );\n                    addedDetails.Add(added);'''
    if old not in text: raise SystemExit('Update added detail line missing')
    text = text.replace(old, new, 1)
write(path, text)

# 9) DTO mapping (single get + paged browse + cache projection).
path = 'src/Dhole.Pricing.Application/Features/Rates/RateMappings.cs'
text = read(path)
if 'x.DestinationTaxAmount' not in text:
    old = '                    x.Quantity,\n                    x.Notes\n                ))'
    new = '                    x.Quantity,\n                    x.Notes,\n                    x.ApplyDestinationTax,\n                    x.DestinationTaxRate,\n                    x.DestinationTaxAmount\n                ))'
    if old not in text: raise SystemExit('RateMappings detail DTO tail missing')
    text = text.replace(old, new, 1)
    write(path, text)

path = 'src/Dhole.Pricing.Persistence/Repositories/RateHeaderRepository.cs'
text = read(path)
if 'd.DestinationTaxAmount' not in text:
    old = '''                        d.UtilityAmount,\n                        d.Quantity,\n                        d.Notes\n                    ))'''
    new = '''                        d.UtilityAmount,\n                        d.Quantity,\n                        d.Notes,\n                        d.ApplyDestinationTax,\n                        d.DestinationTaxRate,\n                        d.DestinationTaxAmount\n                    ))'''
    if old not in text: raise SystemExit('RateHeaderRepository detail projection missing')
    text = text.replace(old, new, 1)
    write(path, text)

path = 'src/Dhole.Pricing.Workers/Workers/PricingCacheWarmupWorker.cs'
text = read(path)
if 'detail.DestinationTaxAmount' not in text:
    old = '''            detail.UtilityAmount,\n            detail.Quantity,\n            detail.Notes\n        );'''
    new = '''            detail.UtilityAmount,\n            detail.Quantity,\n            detail.Notes,\n            detail.ApplyDestinationTax,\n            detail.DestinationTaxRate,\n            detail.DestinationTaxAmount\n        );'''
    if old not in text: raise SystemExit('PricingCacheWarmupWorker detail projection missing')
    text = text.replace(old, new, 1)
    write(path, text)

# 10) Revision snapshot includes VAT configuration.
path = 'src/Dhole.Pricing.Application/Features/Rates/RateRevisionSnapshotFactory.cs'
text = read(path)
if 'x.ApplyDestinationTax' not in text:
    old = 'x.CostAmount, x.SaleAmount, x.UtilityAmount, x.Quantity, x.Notes })'
    new = 'x.CostAmount, x.SaleAmount, x.UtilityAmount, x.Quantity, x.Notes, x.ApplyDestinationTax, x.DestinationTaxRate, x.DestinationTaxAmount })'
    if old not in text: raise SystemExit('Revision snapshot detail tail missing')
    text = text.replace(old, new, 1)
    write(path, text)

# 11) Fixed cost synchronizer: carry VAT state across remove/recreate synchronization.
path = 'src/Dhole.Pricing.Application/Services/RateFixedCostSynchronizer.cs'
text = read(path)
if 'detail.ApplyDestinationTax' not in text:
    old = '''                        detail.CurrencyId,\n                        detail.CurrencyName,\n                        detail.CurrencyCode\n                    );'''
    new = '''                        detail.CurrencyId,\n                        detail.CurrencyName,\n                        detail.CurrencyCode,\n                        detail.ApplyDestinationTax,\n                        detail.DestinationTaxRate\n                    );'''
    if old not in text: raise SystemExit('Fixed synchronizer tuple missing')
    text = text.replace(old, new, 1)

if 'synchronizedDetail.ConfigureDestinationTax' not in text:
    old = '''            rate.AddRateDetail(\n                rate.Id,\n                cost.Id,\n                cost.Name,\n                cost.CostDetailType,\n                cost.CostType,\n                cost.ChargeBasis,\n                targetCurrencyId,\n                targetCurrencyName,\n                targetCurrencyCode,\n                effectiveCostAmount,\n                effectiveSaleAmount,\n                cost.Notes,\n                quantity,\n                updatedBy\n            );'''
    new = '''            var synchronizedDetail = rate.AddRateDetail(\n                rate.Id,\n                cost.Id,\n                cost.Name,\n                cost.CostDetailType,\n                cost.CostType,\n                cost.ChargeBasis,\n                targetCurrencyId,\n                targetCurrencyName,\n                targetCurrencyCode,\n                effectiveCostAmount,\n                effectiveSaleAmount,\n                cost.Notes,\n                quantity,\n                updatedBy\n            );\n\n            if (hasExistingAmount)\n            {\n                synchronizedDetail.ConfigureDestinationTax(\n                    existingAmount.ApplyDestinationTax,\n                    existingAmount.DestinationTaxRate\n                );\n            }'''
    if old not in text: raise SystemExit('Fixed synchronizer AddRateDetail block missing')
    text = text.replace(old, new, 1)
write(path, text)

print('VAT persistence patch applied.')
