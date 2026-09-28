using Dhole.Pricing.Domain.Competitors.Entities;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.MarketPricing;

internal sealed class CompetitorRateObservationConfiguration
    : IEntityTypeConfiguration<CompetitorRateObservation>
{
    public void Configure(EntityTypeBuilder<CompetitorRateObservation> builder)
    {
        builder.ToTable("CompetitorRateObservations");

        builder.HasKey(x => x.Id).HasName("PK_CompetitorRateObservations");
        builder.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");

        builder.Property(x => x.CompetitorTariffId).HasColumnName("competitor_tariff_id");
        builder.Property(x => x.CompetitorCompanyId).HasColumnName("competitor_company_id");
        builder.Property(x => x.CompetitorCompanyName).HasMaxLength(250).HasColumnName("competitor_company_name").IsRequired();

        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id");
        builder.Property(x => x.SourceImportId).HasColumnName("source_import_id");

        builder.Property(x => x.IncotermId).HasColumnName("incoterm_id");
        builder.Property(x => x.IncotermCode).HasMaxLength(40).HasColumnName("incoterm_code");

        builder.Property(x => x.PolId).HasColumnName("pol_id");
        builder.Property(x => x.PolName).HasMaxLength(200).HasColumnName("pol_name");
        builder.Property(x => x.PolCode).HasMaxLength(80).HasColumnName("pol_code");

        builder.Property(x => x.PoeId).HasColumnName("poe_id");
        builder.Property(x => x.PoeName).HasMaxLength(200).HasColumnName("poe_name");
        builder.Property(x => x.PoeCode).HasMaxLength(80).HasColumnName("poe_code");

        builder.Property(x => x.PodId).HasColumnName("pod_id");
        builder.Property(x => x.PodName).HasMaxLength(200).HasColumnName("pod_name");
        builder.Property(x => x.PodCode).HasMaxLength(80).HasColumnName("pod_code");

        builder.Property(x => x.CarrierId).HasColumnName("carrier_id");
        builder.Property(x => x.CarrierName).HasMaxLength(200).HasColumnName("carrier_name");
        builder.Property(x => x.CarrierCode).HasMaxLength(80).HasColumnName("carrier_code");

        builder.Property(x => x.ContainerTypeId).HasColumnName("container_type_id");
        builder.Property(x => x.ContainerTypeCode).HasMaxLength(80).HasColumnName("container_type_code");
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired();

        builder.Property(x => x.Mode)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasColumnName("mode")
            .IsRequired();

        builder.Property(x => x.Currency).HasMaxLength(20).HasColumnName("currency").IsRequired();
        builder.Property(x => x.NormalizedCurrency).HasMaxLength(20).HasColumnName("normalized_currency");
        builder.Property(x => x.OriginalAmount).HasPrecision(18, 2).HasColumnName("original_amount");
        builder.Property(x => x.NormalizedAmount).HasPrecision(18, 2).HasColumnName("normalized_amount");
        builder.Property(x => x.ExchangeRate).HasPrecision(18, 8).HasColumnName("exchange_rate");
        builder.Property(x => x.ExchangeRateDate).HasColumnType("timestamp with time zone").HasColumnName("exchange_rate_date");

        builder.Property(x => x.ValidFrom).HasColumnType("timestamp with time zone").HasColumnName("valid_from").IsRequired();
        builder.Property(x => x.ValidTo).HasColumnType("timestamp with time zone").HasColumnName("valid_to").IsRequired();

        builder.Property(x => x.OceanFreight).HasPrecision(18, 2).HasColumnName("ocean_freight");
        builder.Property(x => x.OriginCharges).HasPrecision(18, 2).HasColumnName("origin_charges");
        builder.Property(x => x.DestinationCharges).HasPrecision(18, 2).HasColumnName("destination_charges");
        builder.Property(x => x.InlandCharges).HasPrecision(18, 2).HasColumnName("inland_charges");
        builder.Property(x => x.OtherCharges).HasPrecision(18, 2).HasColumnName("other_charges");

        builder.Property(x => x.NormalizedOceanFreight).HasPrecision(18, 2).HasColumnName("normalized_ocean_freight");
        builder.Property(x => x.NormalizedAllIn).HasPrecision(18, 2).HasColumnName("normalized_all_in");

        builder.Property(x => x.RateBasis)
            .HasConversion<string>()
            .HasMaxLength(40)
            .HasColumnName("rate_basis")
            .IsRequired();

        builder.Property(x => x.ExtractionConfidence).HasPrecision(9, 6).HasColumnName("extraction_confidence");
        builder.Property(x => x.NormalizationConfidence).HasPrecision(9, 6).HasColumnName("normalization_confidence");

        builder.Property(x => x.RawPayloadJson).HasColumnType("jsonb").HasColumnName("raw_payload_json").IsRequired();

        builder.Property(x => x.ImportedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("imported_at_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("created_at_utc");

        builder.HasOne<CompetitorTariff>()
            .WithMany()
            .HasForeignKey(x => x.CompetitorTariffId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("FK_CompetitorRateObservations_CompetitorTariffs");

        builder.HasIndex(x => x.CompetitorTariffId)
            .HasDatabaseName("IX_CompetitorRateObservations_CompetitorTariffId");

        builder.HasIndex(x => new
            {
                x.Mode,
                x.IncotermId,
                x.PolId,
                x.PoeId,
                x.PodId,
                x.ContainerTypeId,
                x.CarrierId,
            })
            .HasDatabaseName("IX_CompetitorRateObservations_MarketKey");

        builder.HasIndex(x => new { x.ValidFrom, x.ValidTo })
            .HasDatabaseName("IX_CompetitorRateObservations_Validity");

        builder.HasIndex(x => x.CompetitorCompanyId)
            .HasDatabaseName("IX_CompetitorRateObservations_CompetitorCompanyId");
    }
}
