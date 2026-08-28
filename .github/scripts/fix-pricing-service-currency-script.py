from pathlib import Path
p = Path('.github/scripts/apply-pricing-service-currencies-20260828.py')
t = p.read_text(encoding='utf-8')

replacements = []

old = '''replace_once(cost_cfg,
''' + "'''        builder.HasMany(x => x.Incoterms)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n        builder.Navigation(x => x.Incoterms).UsePropertyAccessMode(PropertyAccessMode.Field);\n'''" + ''',
''' + "'''        builder.HasMany(x => x.Incoterms)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n        builder.Navigation(x => x.Incoterms).UsePropertyAccessMode(PropertyAccessMode.Field);\n        builder.HasMany(x => x.Services)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n        builder.Navigation(x => x.Services).UsePropertyAccessMode(PropertyAccessMode.Field);\n'''" + ''')'''
new = '''replace_once(cost_cfg,
''' + "'''        builder\n            .HasMany(x => x.Incoterms)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n\n        builder.Navigation(x => x.Incoterms).UsePropertyAccessMode(PropertyAccessMode.Field);\n'''" + ''',
''' + "'''        builder\n            .HasMany(x => x.Incoterms)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n\n        builder.Navigation(x => x.Incoterms).UsePropertyAccessMode(PropertyAccessMode.Field);\n\n        builder\n            .HasMany(x => x.Services)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n        builder.Navigation(x => x.Services).UsePropertyAccessMode(PropertyAccessMode.Field);\n'''" + ''')'''
replacements.append((old, new, 'cost configuration'))

old = '''replace_once(rate,
''' + "'''    public RateType RateType { get; private set; } = RateType.Tariff;\n    public ShipmentMode ShipmentMode { get; private set; } = ShipmentMode.Fcl;\n'''" + ''',
''' + "'''    public RateType RateType { get; private set; } = RateType.Tariff;\n    public ShipmentMode ShipmentMode { get; private set; } = ShipmentMode.Fcl;\n    public RateOperationType OperationType { get; private set; } = RateOperationType.TransitDomestic;\n'''" + ''')'''
new = '''replace_once(rate,
''' + "'''    public ShipmentMode ShipmentMode { get; private set; } = ShipmentMode.Fcl;\n'''" + ''',
''' + "'''    public ShipmentMode ShipmentMode { get; private set; } = ShipmentMode.Fcl;\n    public RateOperationType OperationType { get; private set; } = RateOperationType.TransitDomestic;\n'''" + ''')'''
replacements.append((old, new, 'rate operation property'))

for old, new, label in replacements:
    if old not in t:
        raise SystemExit(f'{label} patch block not found in implementation script')
    t = t.replace(old, new, 1)

p.write_text(t, encoding='utf-8')
print('runtime patch script compatibility fixes applied')
