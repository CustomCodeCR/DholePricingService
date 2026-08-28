from pathlib import Path

p = Path('src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs')
s = p.read_text(encoding='utf-8')
old = '''            (RateStatus.PendingApproval, RateStatus.RequestedByClient) => true,
            (RateStatus.Open, RateStatus.Sent) => true,'''
new = '''            (RateStatus.PendingApproval, RateStatus.RequestedByClient) => true,
            (RateStatus.PendingApproval, RateStatus.Open) => true,
            (RateStatus.Open, RateStatus.Sent) => true,'''
if old not in s:
    raise SystemExit('open transition anchor not found')
s = s.replace(old, new, 1)
p.write_text(s, encoding='utf-8')
