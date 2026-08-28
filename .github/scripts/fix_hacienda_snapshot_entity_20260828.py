from pathlib import Path

path = Path('src/Dhole.Pricing.Persistence/Migrations/ServiceDbContextModelSnapshot.cs')
text = path.read_text()

block = '''                    b.Property<decimal?>("ExchangeRateApplied")
                        .HasPrecision(18, 6)
                        .HasColumnType("numeric(18,6)")
                        .HasColumnName("exchange_rate_applied");

                    b.Property<DateTime?>("ExchangeRateCapturedAtUtc")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("exchange_rate_captured_at_utc");

                    b.Property<DateTime?>("ExchangeRateDate")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("exchange_rate_date");

                    b.Property<bool>("ExchangeRateManualOverride")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("boolean")
                        .HasDefaultValue(false)
                        .HasColumnName("exchange_rate_manual_override");

                    b.Property<decimal?>("ExchangeRatePurchase")
                        .HasPrecision(18, 6)
                        .HasColumnType("numeric(18,6)")
                        .HasColumnName("exchange_rate_purchase");

                    b.Property<decimal?>("ExchangeRateSale")
                        .HasPrecision(18, 6)
                        .HasColumnType("numeric(18,6)")
                        .HasColumnName("exchange_rate_sale");

                    b.Property<string>("ExchangeRateSource")
                        .HasMaxLength(160)
                        .HasColumnType("character varying(160)")
                        .HasColumnName("exchange_rate_source");

'''

# The previous emergency patch inserted this block into Cost. Remove that accidental block.
if block not in text:
    raise SystemExit('Exchange-rate snapshot block not found for relocation')
text = text.replace(block, '', 1)

rate_marker = '            modelBuilder.Entity("Dhole.Pricing.Domain.Rates.Entities.RateHeader", b =>\n'
pos = text.find(rate_marker)
if pos < 0:
    raise SystemExit('RateHeader model snapshot section not found')

head = text[:pos]
rate_section = text[pos:]
insert_marker = '''                    b.Property<string>("CurrencyName")
                        .IsRequired()
                        .HasMaxLength(120)
                        .HasColumnType("character varying(120)")
                        .HasColumnName("currency_name");

'''
if insert_marker not in rate_section:
    raise SystemExit('RateHeader CurrencyName marker not found')

rate_section = rate_section.replace(insert_marker, insert_marker + block, 1)
text = head + rate_section
path.write_text(text)

# Guardrails: the block must appear exactly once and only after RateHeader starts.
final = path.read_text()
if final.count('b.Property<decimal?>("ExchangeRatePurchase")') != 1:
    raise SystemExit('ExchangeRatePurchase snapshot count is not exactly 1')
if final.find('b.Property<decimal?>("ExchangeRatePurchase")') < final.find(rate_marker):
    raise SystemExit('Exchange-rate fields are still outside RateHeader')

print('Hacienda snapshot fields relocated to RateHeader')
