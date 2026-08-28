from pathlib import Path
p = Path('.github/scripts/apply-pricing-service-currencies-20260828.py')
t = p.read_text(encoding='utf-8')

replacements = []

def swap(old: str, new: str, label: str):
    global t
    if old not in t:
        raise SystemExit(f'{label} patch block not found in implementation script')
    t = t.replace(old, new, 1)

# CostConfiguration formats HasMany on a separate line.
old = '''replace_once(cost_cfg,
''' + "'''        builder.HasMany(x => x.Incoterms)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n        builder.Navigation(x => x.Incoterms).UsePropertyAccessMode(PropertyAccessMode.Field);\n'''" + ''',
''' + "'''        builder.HasMany(x => x.Incoterms)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n        builder.Navigation(x => x.Incoterms).UsePropertyAccessMode(PropertyAccessMode.Field);\n        builder.HasMany(x => x.Services)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n        builder.Navigation(x => x.Services).UsePropertyAccessMode(PropertyAccessMode.Field);\n'''" + ''')'''
new = '''replace_once(cost_cfg,
''' + "'''        builder\n            .HasMany(x => x.Incoterms)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n\n        builder.Navigation(x => x.Incoterms).UsePropertyAccessMode(PropertyAccessMode.Field);\n'''" + ''',
''' + "'''        builder\n            .HasMany(x => x.Incoterms)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n\n        builder.Navigation(x => x.Incoterms).UsePropertyAccessMode(PropertyAccessMode.Field);\n\n        builder\n            .HasMany(x => x.Services)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n        builder.Navigation(x => x.Services).UsePropertyAccessMode(PropertyAccessMode.Field);\n'''" + ''')'''
swap(old, new, 'cost configuration')

# ShipmentMode and RateType are not adjacent in the current aggregate.
old = '''replace_once(rate,
''' + "'''    public RateType RateType { get; private set; } = RateType.Tariff;\n    public ShipmentMode ShipmentMode { get; private set; } = ShipmentMode.Fcl;\n'''" + ''',
''' + "'''    public RateType RateType { get; private set; } = RateType.Tariff;\n    public ShipmentMode ShipmentMode { get; private set; } = ShipmentMode.Fcl;\n    public RateOperationType OperationType { get; private set; } = RateOperationType.TransitDomestic;\n'''" + ''')'''
new = '''replace_once(rate,
''' + "'''    public ShipmentMode ShipmentMode { get; private set; } = ShipmentMode.Fcl;\n'''" + ''',
''' + "'''    public ShipmentMode ShipmentMode { get; private set; } = ShipmentMode.Fcl;\n    public RateOperationType OperationType { get; private set; } = RateOperationType.TransitDomestic;\n'''" + ''')'''
swap(old, new, 'rate operation property')

# Current SetAmounts has no optional default in the signature and formats margin over multiple lines.
old = '''replace_once(rate,
''' + "'''    public void SetAmounts(Guid? updatedBy = null)\n    {'''" + ''',
''' + "'''    public void SetOperationType(RateOperationType operationType, Guid? updatedBy = null)\n    {\n        OperationType = operationType;\n        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());\n    }\n\n    public void ConfigureServices(IReadOnlyCollection<RateServiceSelection>? services, Guid? updatedBy = null)\n    {\n        var selections = services ?? [];\n        var normalized = selections.Where(x => x.Id != Guid.Empty).GroupBy(x => x.Id).Select(x => x.First()).ToArray();\n        if (normalized.Length != selections.Count)\n            throw new InvalidOperationException(\"Los servicios de la tarifa no pueden estar vacíos ni repetidos.\");\n        _rateServices.Clear();\n        foreach (var service in normalized)\n            _rateServices.Add(new RateService(Id, service.Id, service.Name, service.Code));\n        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());\n    }\n\n    public void SetAmounts(Guid? updatedBy = null)\n    {'''" + ''')'''
new = '''replace_once(rate,
''' + "'''    public void SetAmounts(Guid? updatedBy)\n    {'''" + ''',
''' + "'''    public void SetOperationType(RateOperationType operationType, Guid? updatedBy = null)\n    {\n        OperationType = operationType;\n        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());\n    }\n\n    public void ConfigureServices(IReadOnlyCollection<RateServiceSelection>? services, Guid? updatedBy = null)\n    {\n        var selections = services ?? [];\n        var normalized = selections.Where(x => x.Id != Guid.Empty).GroupBy(x => x.Id).Select(x => x.First()).ToArray();\n        if (normalized.Length != selections.Count)\n            throw new InvalidOperationException(\"Los servicios de la tarifa no pueden estar vacíos ni repetidos.\");\n        _rateServices.Clear();\n        foreach (var service in normalized)\n            _rateServices.Add(new RateService(Id, service.Id, service.Name, service.Code));\n        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());\n    }\n\n    public void SetAmounts(Guid? updatedBy)\n    {'''" + ''')'''
swap(old, new, 'set amounts signature')

old = '''replace_once(rate,
''' + "'''        TotalCostAmount = _rateDetails.Sum(x => x.CostAmount * x.Quantity);\n        TotalSaleAmount = _rateDetails.Sum(x => x.SaleAmount * x.Quantity);\n        TotalUtilityAmount = TotalSaleAmount - TotalCostAmount;\n        MarginPercentage = TotalSaleAmount <= 0m ? 0m : (TotalUtilityAmount / TotalSaleAmount) * 100m;\n'''" + ''','''
# Preserve the replacement body already authored by extracting it from the original script and only change its search pattern.
new = '''replace_once(rate,
''' + "'''        TotalCostAmount = _rateDetails.Sum(x => x.CostAmount * x.Quantity);\n        TotalSaleAmount = _rateDetails.Sum(x => x.SaleAmount * x.Quantity);\n        TotalUtilityAmount = TotalSaleAmount - TotalCostAmount;\n        MarginPercentage =\n            TotalSaleAmount <= 0m\n                ? 0m\n                : Math.Round(\n                    TotalUtilityAmount / TotalSaleAmount * 100m,\n                    2,\n                    MidpointRounding.AwayFromZero\n                );\n'''" + ''','''
swap(old, new, 'set amounts old math')

# RateHeader configuration currently includes a default on ShipmentMode and line-break HasMany style.
old = '''replace_once(rate_cfg,
''' + "'''        builder.Property(x => x.ShipmentMode).HasConversion<string>().HasMaxLength(20).IsRequired();\n'''" + ''',
''' + "'''        builder.Property(x => x.ShipmentMode).HasConversion<string>().HasMaxLength(20).IsRequired();\n        builder.Property(x => x.OperationType).HasConversion<string>().HasMaxLength(30).IsRequired();\n'''" + ''')'''
new = '''replace_once(rate_cfg,
''' + "'''        builder.Property(x => x.ShipmentMode).HasConversion<string>().HasMaxLength(20).IsRequired().HasDefaultValue(Dhole.Pricing.Domain.Rates.Enums.ShipmentMode.Fcl);\n'''" + ''',
''' + "'''        builder.Property(x => x.ShipmentMode).HasConversion<string>().HasMaxLength(20).IsRequired().HasDefaultValue(Dhole.Pricing.Domain.Rates.Enums.ShipmentMode.Fcl);\n        builder.Property(x => x.OperationType).HasConversion<string>().HasMaxLength(30).IsRequired().HasDefaultValue(Dhole.Pricing.Domain.Rates.Enums.RateOperationType.TransitDomestic);\n'''" + ''')'''
swap(old, new, 'rate shipment configuration')

old = '''replace_once(rate_cfg,
''' + "'''        builder.HasMany(x => x.RateDetails)\n'''" + ''',
''' + "'''        builder.HasMany(x => x.RateServices)\n            .WithOne()\n            .HasForeignKey(x => x.RateHeaderId)\n            .OnDelete(DeleteBehavior.Cascade);\n        builder.Navigation(x => x.RateServices).UsePropertyAccessMode(PropertyAccessMode.Field);\n\n        builder.HasMany(x => x.RateDetails)\n'''" + ''')'''
new = '''replace_once(rate_cfg,
''' + "'''        builder\n            .HasMany(x => x.RateDetails)\n'''" + ''',
''' + "'''        builder\n            .HasMany(x => x.RateServices)\n            .WithOne()\n            .HasForeignKey(x => x.RateHeaderId)\n            .OnDelete(DeleteBehavior.Cascade);\n        builder.Navigation(x => x.RateServices).UsePropertyAccessMode(PropertyAccessMode.Field);\n\n        builder\n            .HasMany(x => x.RateDetails)\n'''" + ''')'''
swap(old, new, 'rate services relationship')

# Command records need the RateServiceSelection namespace.
anchor = "# Rate DTO adds persisted context and both currency totals.\n"
if anchor not in t:
    raise SystemExit('command using insertion anchor not found')
t = t.replace(anchor, '''for cmd in [
    'src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommand.cs',
    'src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommand.cs',
]:
    replace_once(cmd,
''' + "'''using Dhole.Pricing.Domain.Rates.Enums;\n'''" + ''',
''' + "'''using Dhole.Pricing.Domain.Rates.Enums;\nusing Dhole.Pricing.Domain.Rates.Entities;\n'''" + ''')

''' + anchor, 1)

p.write_text(t, encoding='utf-8')
print('runtime patch script compatibility fixes applied')
