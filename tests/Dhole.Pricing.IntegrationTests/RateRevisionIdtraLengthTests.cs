using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class RateRevisionIdtraLengthTests
{
    [TestMethod]
    public void RevisionAndHeaderIdtraColumnsMustHaveSameMaximumLength()
    {
        // Model verification needs no connection to an actual database.
        var options = new DbContextOptionsBuilder<ServiceDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var db = new ServiceDbContext(options);

        var archive = db.Model.FindEntityType(typeof(RateRevision))
            ?? throw new InvalidOperationException("Missing RateRevision EF mapping.");
        var header = db.Model.FindEntityType(typeof(RateHeader))
            ?? throw new InvalidOperationException("Missing RateHeader EF mapping.");
        var archiveIdtra = archive.FindProperty(nameof(RateRevision.IdtraNumber))
            ?? throw new InvalidOperationException("Missing historical IDTRA column.");
        var headerIdtra = header.FindProperty(nameof(RateHeader.IdtraNumber))
            ?? throw new InvalidOperationException("Missing current IDTRA column.");

        Assert.AreEqual(2000, headerIdtra.GetMaxLength());
        Assert.AreEqual(headerIdtra.GetMaxLength(), archiveIdtra.GetMaxLength());
        Assert.AreEqual("character varying(2000)", archiveIdtra.GetColumnType());
    }

    [TestMethod]
    public void RevisionSnapshotRetainsMultipleIdtraValuesWithoutTruncating()
    {
        var idtra = string.Join(", ", Enumerable.Range(12552, 23)
            .Select(number => $"IDTRA999.{number}"));
        Assert.IsGreaterThan(100, idtra.Length);

        var archived = RateRevision.Create(
            Guid.NewGuid(), 2, "Open", "FCL 40NOR", idtra,
            "QUO-00000-000552", 6255m, 0m, 10m, "{}", Guid.NewGuid());

        Assert.AreEqual(idtra, archived.IdtraNumber);
        Assert.IsGreaterThan(100, archived.IdtraNumber!.Length);
    }
}
