from pathlib import Path
p = Path('.github/scripts/apply-pricing-service-currencies-20260828.py')
t = p.read_text(encoding='utf-8')
old = '''replace_once(cost_cfg,
''' + "'''        builder.HasMany(x => x.Incoterms)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n        builder.Navigation(x => x.Incoterms).UsePropertyAccessMode(PropertyAccessMode.Field);\n'''" + ''',
''' + "'''        builder.HasMany(x => x.Incoterms)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n        builder.Navigation(x => x.Incoterms).UsePropertyAccessMode(PropertyAccessMode.Field);\n        builder.HasMany(x => x.Services)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n        builder.Navigation(x => x.Services).UsePropertyAccessMode(PropertyAccessMode.Field);\n'''" + ''')'''
new = '''replace_once(cost_cfg,
''' + "'''        builder\n            .HasMany(x => x.Incoterms)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n\n        builder.Navigation(x => x.Incoterms).UsePropertyAccessMode(PropertyAccessMode.Field);\n'''" + ''',
''' + "'''        builder\n            .HasMany(x => x.Incoterms)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n\n        builder.Navigation(x => x.Incoterms).UsePropertyAccessMode(PropertyAccessMode.Field);\n\n        builder\n            .HasMany(x => x.Services)\n            .WithOne()\n            .HasForeignKey(x => x.CostId)\n            .OnDelete(DeleteBehavior.Cascade);\n        builder.Navigation(x => x.Services).UsePropertyAccessMode(PropertyAccessMode.Field);\n'''" + ''')'''
if old not in t:
    raise SystemExit('cost configuration patch block not found in implementation script')
p.write_text(t.replace(old, new, 1), encoding='utf-8')
print('runtime patch script compatibility fix applied')
