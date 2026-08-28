from pathlib import Path

path = Path('src/Dhole.Pricing.Persistence/Migrations/ServiceDbContextModelSnapshot.cs')
text = path.read_text()

old = '''                    b.Property<string>("CurrencyName")
                        .IsRequired()
                        .HasMaxLength(120)
                        .HasColumnType("character varying(120)")
                        .HasColumnName("currency_name");

                    b.Property<DateTime?>("DeletedAtUtc")'''

new = '''                    b.Property<string>("CurrencyName")
                        .IsRequired()
                        .HasMaxLength(120)
                        .HasColumnType("character varying(120)")
                        .HasColumnName("currency_name");

                    b.Property<decimal?>("ExchangeRateApplied")
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

                    b.Property<DateTime?>("DeletedAtUtc")'''

if old not in text:
    if 'b.Property<decimal?>("ExchangeRatePurchase")' in text:
        print('Hacienda model snapshot already synchronized')
    else:
        raise SystemExit('RateHeader CurrencyName snapshot marker not found')
else:
    path.write_text(text.replace(old, new, 1))
    print('Hacienda model snapshot synchronized')
