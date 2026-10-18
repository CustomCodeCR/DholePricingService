using Dhole.Pricing.Domain.Competitors.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Competitors;

internal sealed class CompetitorAnalysisDocumentConfiguration : IEntityTypeConfiguration<CompetitorAnalysisDocument>
{
    public void Configure(EntityTypeBuilder<CompetitorAnalysisDocument> builder)
    {
        builder.ToTable("CompetitorAnalysisDocuments");
        builder.HasKey(x => x.Id).HasName("PK_CompetitorAnalysisDocuments");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.StorageId).IsRequired();
        builder.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SizeInBytes).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(x => x.StorageId).IsUnique()
            .HasDatabaseName("UX_CompetitorAnalysisDocuments_StorageId");
        builder.HasIndex(x => x.CreatedAtUtc)
            .HasDatabaseName("IX_CompetitorAnalysisDocuments_CreatedAtUtc");
    }
}
