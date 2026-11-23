using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Abstractions.Messaging;

/// <summary>Persists the immutable accepted QUO event in the SAME transaction as commercial status.</summary>
public interface IAcceptedQuoteEventWriter
{
    Task QueueAsync(RateHeader rate, RateStatus previousStatus,
        string acceptedSnapshotJson, Guid? acceptedBy, string? observations,
        CancellationToken cancellationToken);
}
