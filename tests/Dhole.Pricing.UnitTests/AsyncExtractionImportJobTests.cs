using Dhole.Pricing.Application.Imports;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class AsyncExtractionImportJobTests
{
    [TestMethod]
    public void Job_SchedulesRetryAndCompletesWithImportCounts()
    {
        var job = CreateJob(maxAttemptCount: 3);

        job.MarkProcessing("pricing-worker-a", DateTime.UtcNow.AddMinutes(5));
        job.ScheduleRetry(
            "Pricing.DatabaseUnavailable",
            "temporary",
            DateTime.UtcNow.AddSeconds(10)
        );
        job.MarkProcessing("pricing-worker-b", DateTime.UtcNow.AddMinutes(5));
        job.MarkCompleted(persistedRows: 8, skippedRows: 2);

        Assert.AreEqual(
            PricingImportFromExtractionJobStatus.Completed,
            job.Status
        );
        Assert.AreEqual(2, job.AttemptCount);
        Assert.AreEqual(8, job.PersistedRows);
        Assert.AreEqual(2, job.SkippedRows);
        Assert.IsNull(job.LeaseOwner);
        Assert.IsNotNull(job.CompletedAtUtc);
    }

    [TestMethod]
    public void Job_CompletionIsIdempotent()
    {
        var job = CreateJob(maxAttemptCount: 3);

        job.MarkProcessing("pricing-worker", DateTime.UtcNow.AddMinutes(5));
        job.MarkCompleted(4, 1);
        var versionAfterFirstCompletion = job.Version;
        job.MarkCompleted(4, 1);

        Assert.AreEqual(versionAfterFirstCompletion, job.Version);
        Assert.AreEqual(4, job.PersistedRows);
    }

    [TestMethod]
    public void Worker_UsesPersistExtractionServiceAndNoInternalTransportClient()
    {
        var workerType = typeof(Dhole.Pricing.Workers.Worker)
            .Assembly.GetType(
                "Dhole.Pricing.Worker.Workers.PricingImportFromExtractionWorker",
                throwOnError: true
            )!;
        var dependencyTypes = workerType
            .GetConstructors(
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
            )
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        CollectionAssert.Contains(
            dependencyTypes,
            typeof(ExtractAndPersistFclPricingImportService)
        );
        Assert.IsFalse(
            dependencyTypes.Any(type =>
                type == typeof(HttpClient)
                || type.FullName?.Contains(
                    "Grpc",
                    StringComparison.OrdinalIgnoreCase
                ) == true
            ),
            "The Pricing worker must persist directly through Application."
        );
    }

    private static PricingImportFromExtractionJob CreateJob(
        int maxAttemptCount
    )
    {
        return PricingImportFromExtractionJob.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"response":{"success":true}}""",
            "correlation-id",
            maxAttemptCount
        );
    }
}
