from pathlib import Path

path = Path('src/Dhole.Pricing.Persistence/Migrations/ServiceDbContextModelSnapshot.cs')
text = path.read_text(encoding='utf-8')

anchor = '''                    b.Property<string>("IncotermCode")
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("incoterm_code");
'''
insert = anchor + '''
                    b.Property<string>("PickupAddress")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("pickup_address");

                    b.Property<decimal?>("PickupLatitude")
                        .HasPrecision(10, 7)
                        .HasColumnType("numeric(10,7)")
                        .HasColumnName("pickup_latitude");

                    b.Property<decimal?>("PickupLongitude")
                        .HasPrecision(10, 7)
                        .HasColumnType("numeric(10,7)")
                        .HasColumnName("pickup_longitude");
'''

# There are two IncotermCode properties in the snapshot. The CostIncoterm one is required;
# the RateHeader one is nullable. Patch only the nullable RateHeader anchor.
if text.count(anchor) != 1:
    raise SystemExit(f'Expected one nullable RateHeader IncotermCode anchor, found {text.count(anchor)}')
if 'b.Property<string>("PickupAddress")' in text:
    raise SystemExit('Pickup fields already exist in the model snapshot.')

path.write_text(text.replace(anchor, insert, 1), encoding='utf-8')
print('ServiceDbContext model snapshot updated with pickup fields.')
