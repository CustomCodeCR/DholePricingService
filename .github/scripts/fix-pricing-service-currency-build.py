from pathlib import Path

p = Path('.github/scripts/apply-pricing-service-currencies-20260828.py')
t = p.read_text(encoding='utf-8')
anchor = '# -----------------------------------------------------------------------------\n# Cost <-> pricing-services association\n'
if anchor not in t:
    raise SystemExit('Cost association anchor not found')
addition = '''# Current UpdateCost handler does not otherwise reference Cost entities.
replace_once(
    'src/Dhole.Pricing.Application/Features/Costs/UpdateCost/UpdateCostCommandHandler.cs',
''' + "'''using Dhole.Pricing.Application.Services;\nusing Dhole.Pricing.Domain.Shared;\n'''" + ''',
''' + "'''using Dhole.Pricing.Application.Services;\nusing Dhole.Pricing.Domain.Costs.Entities;\nusing Dhole.Pricing.Domain.Shared;\n'''" + ''',
)

'''
t = t.replace(anchor, addition + anchor, 1)
p.write_text(t, encoding='utf-8')
print('pricing currency build compatibility patch applied')
